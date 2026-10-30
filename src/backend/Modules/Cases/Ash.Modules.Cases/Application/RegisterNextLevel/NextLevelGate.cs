using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

/// <summary>The level to continue and its dossier, read without tracking (the previous level is never changed).</summary>
internal sealed record NextLevelSource(Case Previous, Dossier Dossier, ushort LatestLevelSeq);

/// <summary>
/// Whether a case can be continued at the next level — one place for the preview and the registration, so they
/// always agree. Order: unknown case or no registration lookup scope → 404; not the latest level, archived dossier,
/// or a level not closed in the system → 409 with the reason.
/// </summary>
internal sealed class NextLevelGate(CasesDbContext db, ICaseAccessService access)
{
    public async Task<Result<NextLevelSource>> CheckAsync(Guid previousCasePublicId, CancellationToken ct)
    {
        var previous = await db.Cases.AsNoTracking().SingleOrDefaultAsync(c => c.PublicId == previousCasePublicId, ct);
        if (previous is null || access.GetRegistrationLookupScope() == RegistrationLookupScope.None)
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        var latest = await db.Cases.AsNoTracking()
            .Where(c => c.DossierId == previous.DossierId)
            .OrderByDescending(c => c.LevelSeq)
            .Select(c => new { c.Id, c.CaseCode, c.LevelSeq })
            .FirstAsync(ct);
        if (latest.Id != previous.Id)
        {
            return AlreadyContinued(latest.CaseCode);
        }

        var dossier = await db.Dossiers.AsNoTracking().SingleAsync(d => d.Id == previous.DossierId, ct);
        if (dossier.ArchivedAt is not null)
        {
            return Error.Conflict("case.dossier_archived", "Dosja është arkivuar; çështja nuk mund të vazhdojë në një shkallë tjetër.");
        }

        if (previous.LifecycleStatus != Case.Closed)
        {
            // No side effects in a failing request: the UI offers POST …/notify-not-closed to tell the lawyer and the Kryetar.
            return Error.Conflict("case.previous_not_closed",
                "Shkalla e mëparshme nuk është mbyllur ende në sistem. Avokati duhet të regjistrojë vendimin.");
        }

        return new NextLevelSource(previous, dossier, latest.LevelSeq);
    }

    public static Error AlreadyContinued(string caseCode) =>
        Error.Conflict("case.already_continued", $"Kjo çështje ka vazhduar tashmë në {caseCode}");
}
