using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Imports;

/// <summary>
/// "Organika" (task 053g, client decision E; Administrimi, admin.org) on the shared pipeline (053l): the HR sheet gives
/// EXISTING users their position and unit (task 053p: no manager). A row is CHANGED / UNCHANGED against the user, or an
/// ERROR: no e-mail / no position, text too long, the e-mail twice in the sheet (every such row), not a user. Users not
/// in the sheet are not touched. "Apliko" writes the CHANGED rows with a user_org_history row each.
/// TODO(akshi-q-hrmis): an HRMIS integration would keep positions up to date without a sheet.
/// </summary>
public sealed class OrganikaImport(IdentityDbContext db, ICurrentUser user, IClock clock) : IImportKind
{
    public const string KindCode = "ORGANIKA";
    public const string Email = "email";
    public const string Position = "pozicioni";
    public const string Unit = "njesia";
    public const int MaxText = 200;

    public static readonly ImportDefinition Shared = new(
        KindCode,
        "Organika — pozicioni dhe njësia",
        "Organika",
        "organika-shabllon.xlsx",
        "Përditëson pozicionin dhe njësinë organizative të përdoruesve që ekzistojnë në sistem, nga skedari i " +
        "burimeve njerëzore. Nuk krijon dhe nuk çaktivizon përdorues.",
        [
            new(Email, ImportRequirement.Required, "E-maili i përdoruesit në sistem — lidh rreshtin me përdoruesin.", "avokat1@ash.local"),
            new(Position, ImportRequirement.Required, $"Pozicioni i punës (deri në {MaxText} shkronja).", "Avokat i Shtetit"),
            new(Unit, ImportRequirement.Optional, $"Njësia ose zyra organizative (deri në {MaxText} shkronja).", "Drejtoria e Përfaqësimit"),
        ],
        [
            "E-maili duhet të ekzistojë si përdorues në sistem.",
            "Një e-mail që përsëritet në skedar nuk importohet (asnjë nga rreshtat e tij).",
            "Përdoruesit që nuk janë në skedar nuk ndryshojnë.",
            "Një kolonë opsionale bosh e fshin vlerën (p.sh. njësia bosh → pa njësi).",
        ]);

    public string Kind => KindCode;

    public ImportDefinition Definition => Shared;

    private sealed record Payload(uint UserId, string Position, string? Unit);

    public async Task<IReadOnlyList<ImportPlan>> PreviewAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        static string Key(string email) => email.Trim().ToLowerInvariant();
        var emails = rows.Select(r => r[Email]).OfType<string>().Select(Key).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => emails.Contains(u.Email))
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Position, u.OrgUnit })
            .ToListAsync(ct);
        var byEmail = users.ToDictionary(u => Key(u.Email));
        var duplicates = rows.Where(r => r[Email] is not null).GroupBy(r => Key(r[Email]!)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var result = new List<ImportPlan>(rows.Count);
        foreach (var r in rows)
        {
            var errors = new List<ImportCellError>();
            if (r[Email] is null)
            {
                errors.Add(new(Email, "Mungon e-maili."));
            }

            if (r[Position] is null)
            {
                errors.Add(new(Position, "Mungon pozicioni."));
            }

            foreach (var column in new[] { Position, Unit })
            {
                if (r[column] is { Length: > MaxText })
                {
                    errors.Add(new(column, $"Deri në {MaxText} shkronja."));
                }
            }

            var u = r[Email] is { } e && byEmail.TryGetValue(Key(e), out var found) ? found : null;
            if (r[Email] is { } email && duplicates.Contains(Key(email)))
            {
                errors.Add(new(Email, "Ky e-mail është më shumë se një herë në skedar."));
            }
            else if (r[Email] is not null && u is null)
            {
                errors.Add(new(Email, "Nuk ka përdorues me këtë e-mail."));
            }

            var name = u is null ? null : $"{u.FirstName} {u.LastName}";
            if (errors.Count > 0 || u is null)
            {
                result.Add(new ImportPlan(new ImportRowView(r.Line, r[Email], ImportStatus.Error, name, [], errors, null), null));
                continue;
            }

            var changes = new List<ImportChange>();
            if (u.Position != r[Position]) changes.Add(new(Position, u.Position, r[Position]));
            if (u.OrgUnit != r[Unit]) changes.Add(new(Unit, u.OrgUnit, r[Unit]));
            result.Add(new ImportPlan(
                new ImportRowView(r.Line, r[Email], changes.Count == 0 ? ImportStatus.Unchanged : ImportStatus.Changed, name, changes, [], null),
                new Payload(u.Id, r[Position]!, r[Unit])));
        }

        return result;
    }

    public async Task<Result<int>> ApplyAsync(ulong importId, IReadOnlyList<ImportPlan> rows, CancellationToken ct)
    {
        var changed = rows.Where(p => p.Row.Status == ImportStatus.Changed).Select(p => (Payload)p.Payload!).ToList();
        var ids = changed.Select(p => p.UserId).ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var now = clock.UtcNow;
        foreach (var p in changed)
        {
            var u = users[p.UserId];
            db.UserOrgHistory.Add(new UserOrgHistoryRecord
            {
                UserId = u.Id,
                ImportId = importId,
                OldPosition = u.Position,
                NewPosition = p.Position,
                OldOrgUnit = u.OrgUnit,
                NewOrgUnit = p.Unit,
                ChangedBy = (uint)user.UserId,
                ChangedAt = now,
            });
            u.Position = p.Position;
            u.OrgUnit = p.Unit;
        }

        await db.SaveChangesAsync(ct);
        return changed.Count;
    }
}
