using Ash.Infrastructure.Persistence;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Infrastructure;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Application.Email;

/// <summary>
/// Sends the e-mails of email_outbox (task 053c), AFTER the action that queued them has committed. It runs every
/// IntervalSeconds and whenever <see cref="Wake"/> is called (after a commit that queued mail). Each mail is claimed
/// in its own transaction with SELECT … FOR UPDATE SKIP LOCKED, so two API instances never send the same row. A
/// failed send stays PENDING with attempts + 1 and a growing wait (1, 2, 4 … minutes, at most an hour); after
/// MaxAttempts it is FAILED with its last error. A mail failure never touches the action that queued it.
/// </summary>
public sealed class EmailDispatcher(
    IServiceScopeFactory scopes,
    IEmailSender sender,
    EmailOptions options,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private readonly SemaphoreSlim _wake = new(0, 1);

    /// <summary>Asks for a run now (no-op when one is already asked for).</summary>
    public void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // a parallel Wake was first: one run is enough
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("E-mail sending is disabled (Email:Enabled = false); mails stay in email_outbox.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "The e-mail dispatcher run failed; it runs again at the next interval.");
            }

            try
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One run: up to BatchSize due mails, each tried once. Returns how many were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var tried = new List<ulong>();
        var sent = 0;
        for (var i = 0; i < options.BatchSize; i++)
        {
            var outcome = await ProcessNextAsync(tried, ct);
            if (outcome is null)
            {
                break;
            }

            if (outcome.Value)
            {
                sent++;
            }
        }

        return sent;
    }

    /// <summary>null: nothing due; true: sent; false: failed (retried later, or FAILED).</summary>
    private async Task<bool?> ProcessNextAsync(List<ulong> tried, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var db = services.GetRequiredService<NotificationsDbContext>();
        var now = services.GetRequiredService<IClock>().UtcNow;
        await unitOfWork.BeginAsync(ct);

        var except = tried.Count == 0 ? string.Empty : $"AND id NOT IN ({string.Join(',', tried)}) ";
        await using (var claim = unitOfWork.CreateCommand(
                         "SELECT id FROM email_outbox WHERE status = 'PENDING' AND next_attempt_at <= @now " + except +
                         "ORDER BY next_attempt_at, id LIMIT 1 FOR UPDATE SKIP LOCKED"))
        {
            claim.Parameters.AddWithValue("@now", now);
            if (await claim.ExecuteScalarAsync(ct) is not { } value)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return null;
            }

            tried.Add(Convert.ToUInt64(value));
        }

        var id = tried[^1];
        var row = await db.EmailOutbox.SingleAsync(r => r.Id == id, ct);
        bool ok;
        try
        {
            await sender.SendAsync(new EmailMessage(row.ToAddress, row.Subject, row.BodyText), ct);
            row.Status = EmailOutboxRow.Sent;
            row.SentAt = now;
            row.LastError = null;
            ok = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            row.Attempts++;
            row.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            if (row.Attempts >= options.MaxAttempts)
            {
                row.Status = EmailOutboxRow.Failed;
                logger.LogError(ex, "E-mail {Id} to {To} FAILED after {Attempts} attempts.", row.Id, row.ToAddress, row.Attempts);
            }
            else
            {
                row.NextAttemptAt = now + RetryDelay(row.Attempts);
                logger.LogWarning(ex, "E-mail {Id} was not sent (attempt {Attempts}); trying again at {Next}.",
                    row.Id, row.Attempts, row.NextAttemptAt);
            }

            ok = false;
        }

        await db.SaveChangesAsync(ct);
        await unitOfWork.CommitAsync(ct);
        return ok;
    }

    /// <summary>1, 2, 4, 8 … minutes after the n-th failure, at most an hour.</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempts - 1))));

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
