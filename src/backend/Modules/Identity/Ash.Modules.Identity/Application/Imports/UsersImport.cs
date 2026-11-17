using Ash.Modules.Cases.Contracts;
using Ash.Modules.Identity.Application.Offices;
using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Imports;

/// <summary>
/// "Ngarko përdorues nga Excel" (task 053l; Administrimi → Përdoruesit, admin.users) on the shared pipeline. A new
/// e-mail → NEW. An existing e-mail → EXISTS, unless perditeso = PO: then CHANGED / UNCHANGED against the user (name,
/// office or institution, roles, position, active). An ERROR row: a missing or invalid cell, the e-mail twice, an
/// unknown office / institution / role, a role that does not fit the user type, both or neither of zyra_kodi and
/// institucioni — and the guards of the user administration (task 015 / 053i) per row: the Kryetar of an office is not
/// deactivated, moved or stripped of OFFICE_HEAD; a lawyer role is not removed while its position is held on open cases;
/// not the last active administrator, not oneself. No passwords: the user signs in through B2C with the same e-mail
/// and is linked at the first login (task 051). "Apliko" writes through the same handlers as the page, so every rule
/// and audit row applies; an error there stops the whole import (rolled back).
/// </summary>
public sealed class UsersImport(
    IdentityDbContext db,
    ICurrentUser actor,
    ICaseAssignmentCounts counts,
    CreateUserHandler create,
    UpdateUserHandler update,
    GrantRoleHandler grant,
    RevokeRoleHandler revoke,
    SetUserActiveHandler setActive) : IImportKind
{
    public const string KindCode = "USERS";
    public const string Email = "email";
    public const string FirstName = "emri";
    public const string LastName = "mbiemri";
    public const string OfficeCode = "zyra_kodi";
    public const string Institution = "institucioni";
    public const string Roles = "rolet";
    public const string Position = "pozicioni";
    public const string Active = "aktiv";
    public const string Update = "perditeso";
    public const int MaxPosition = 200;

    public static readonly ImportDefinition Shared = new(
        KindCode,
        "Përdoruesit — krijim dhe përditësim",
        "Perdoruesit",
        "perdoruesit-shabllon.xlsx",
        "Krijon përdorues të rinj dhe, kur kërkohet, përditëson ekzistuesit. Nuk ka fjalëkalime: përdoruesi hyn me " +
        "llogarinë B2C, prandaj e-maili duhet të jetë i njëjti me e-mailin e hyrjes; lidhja bëhet në hyrjen e parë.",
        [
            new(Email, ImportRequirement.Required, "E-maili i hyrjes (unik në sistem).", "agim.hoxha@avokatura.gov.al"),
            new(FirstName, ImportRequirement.Required, "Emri (deri në 100 shkronja).", "Agim"),
            new(LastName, ImportRequirement.Required, "Mbiemri (deri në 100 shkronja).", "Hoxha"),
            new(OfficeCode, ImportRequirement.OneOf, "Kodi i zyrës, për përdoruesit e Avokaturës (p.sh. TR). Plotësoni zyra_kodi OSE institucioni.", "TR"),
            new(Institution, ImportRequirement.OneOf, "Emri ose NIPT-i i institucionit, për përdoruesit e institucioneve.", ""),
            new(Roles, ImportRequirement.Required,
                "Kodet ose emrat e roleve, të ndarë me „;\" (p.sh. STATE_LAWYER;SECONDARY_LAWYER ose Avokat Shteti (primar)). Duhet t'i përshtaten llojit të përdoruesit (zyrë ose institucion).",
                "STATE_LAWYER"),
            new(Position, ImportRequirement.Optional, $"Pozicioni i punës (deri në {MaxPosition} shkronja).", "Avokat i Shtetit"),
            new(Active, ImportRequirement.Optional, "PO ose JO; bosh = PO.", "PO"),
            new(Update, ImportRequirement.Optional,
                "PO = përditëso emrin, zyrën / institucionin, rolet, pozicionin dhe statusin e një përdoruesi ekzistues; bosh ose JO = rreshti raportohet si „ekziston\".",
                "JO"),
        ],
        [
            "E-maili është unik: një e-mail i përsëritur në skedar nuk importohet (asnjë nga rreshtat e tij).",
            "Një përdorues ekzistues përditësohet vetëm kur perditeso = PO; ndryshe raportohet si „ekziston\".",
            "Zyra, institucioni ose roli i panjohur → gabim në atë rresht; rreshtat e tjerë importohen.",
            "Mbrojtjet e administrimit vlejnë për çdo rresht: Kryetari i zyrës nuk çaktivizohet, nuk zhvendoset dhe nuk i hiqet roli; roli i avokatit nuk hiqet sa kohë mban çështje të hapura (transferojini më parë); administratori i fundit aktiv mbetet.",
            "Përdoruesit që nuk janë në skedar nuk ndryshojnë.",
        ]);

    public string Kind => KindCode;

    public ImportDefinition Definition => Shared;

    /// <summary>What "Apliko" writes for one row.</summary>
    private sealed record Payload(
        Guid? ExistingPublicId, string Email, string FirstName, string LastName, Guid? OfficePublicId, Guid? InstitutionPublicId,
        IReadOnlyList<string> Roles, IReadOnlyList<string> RolesToAdd, IReadOnlyList<string> RolesToRemove, string? Position,
        bool Active, bool IdentityChanged, bool ActiveChanged, bool PositionChanged);

    private static bool? YesNo(string? value) => value is null ? null
        : ImportDefinition.Normalize(value) switch { "po" => true, "jo" => false, _ => (bool?)null };

    public async Task<IReadOnlyList<ImportPlan>> PreviewAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        var emails = rows.Select(r => UserAdminSupport.NormalizeEmail(r[Email])).Where(e => e.Length > 0).Distinct().ToList();
        var existing = await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email)).ToListAsync(ct);
        var byEmail = existing.ToDictionary(u => UserAdminSupport.NormalizeEmail(u.Email));
        var existingIds = existing.Select(u => u.Id).ToList();
        var userRoles = await (from ur in db.UserRoles.AsNoTracking()
                               join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                               where existingIds.Contains(ur.UserId)
                               select new { ur.UserId, r.Code }).ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking().Select(r => new { r.Code, r.Name }).ToListAsync(ct);
        var roleByKey = new Dictionary<string, string>();
        foreach (var r in roles)
        {
            roleByKey[ImportDefinition.Normalize(r.Code)] = r.Code;
            roleByKey[ImportDefinition.Normalize(r.Name)] = r.Code;
        }

        var offices = await db.Offices.AsNoTracking().ToListAsync(ct);
        var officeByCode = offices.Where(o => o.IsActive).ToDictionary(o => o.Code.Trim().ToUpperInvariant());
        var officeById = offices.ToDictionary(o => o.Id);
        var institutions = await db.Institutions.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
        var institutionByName = institutions.GroupBy(i => ImportDefinition.Normalize(i.Name)).ToDictionary(g => g.Key, g => g.First());
        var institutionByNipt = institutions.Where(i => !string.IsNullOrWhiteSpace(i.Nipt))
            .GroupBy(i => i.Nipt!.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var institutionById = await db.Institutions.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var heads = offices.Where(o => o.HeadUserId != null).ToDictionary(o => o.HeadUserId!.Value, o => o.Name);
        var activeAdmins = await (from ur in db.UserRoles.AsNoTracking()
                                  join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                                  join u in db.Users.AsNoTracking() on ur.UserId equals u.Id
                                  where r.Code == UserAdminSupport.AdminRole && u.IsActive
                                  select u.Id).ToListAsync(ct);
        var duplicates = rows.Select(r => UserAdminSupport.NormalizeEmail(r[Email])).Where(e => e.Length > 0)
            .GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        var result = new List<ImportPlan>(rows.Count);
        foreach (var r in rows)
        {
            var errors = new List<ImportCellError>();
            var email = UserAdminSupport.NormalizeEmail(r[Email]);
            var first = r[FirstName];
            var last = r[LastName];

            if (email.Length == 0)
            {
                errors.Add(new(Email, "Mungon e-maili."));
            }
            else if (UserAdminSupport.ValidateIdentity(email, "x", "x") is not null)
            {
                errors.Add(new(Email, "E-maili nuk është i vlefshëm."));
            }
            else if (duplicates.Contains(email))
            {
                errors.Add(new(Email, "Ky e-mail është më shumë se një herë në skedar."));
            }

            if (first is null || first.Length > 100)
            {
                errors.Add(new(FirstName, "Emri është i detyrueshëm (deri në 100 shkronja)."));
            }

            if (last is null || last.Length > 100)
            {
                errors.Add(new(LastName, "Mbiemri është i detyrueshëm (deri në 100 shkronja)."));
            }

            // exactly one of the office and the institution
            OfficeRecord? office = null;
            InstitutionRecord? institution = null;
            if ((r[OfficeCode] is null) == (r[Institution] is null))
            {
                errors.Add(new(OfficeCode, "Plotësoni saktësisht njërën: zyra_kodi (përdorues i Avokaturës) OSE institucioni."));
            }
            else if (r[OfficeCode] is { } code)
            {
                if (!officeByCode.TryGetValue(code.ToUpperInvariant(), out office))
                {
                    errors.Add(new(OfficeCode, $"Nuk ka zyrë aktive me kodin „{code}\"."));
                }
            }
            else if (r[Institution] is { } inst
                     && !institutionByNipt.TryGetValue(inst.ToUpperInvariant(), out institution)
                     && !institutionByName.TryGetValue(ImportDefinition.Normalize(inst), out institution))
            {
                errors.Add(new(Institution, $"Nuk ka institucion aktiv me emrin ose NIPT-in „{inst}\"."));
            }

            // roles: codes or display names, ";"-separated, fitting the user type
            var roleCodes = new List<string>();
            if (r[Roles] is not { } roleText)
            {
                errors.Add(new(Roles, "Shënoni të paktën një rol."));
            }
            else
            {
                foreach (var token in roleText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!roleByKey.TryGetValue(ImportDefinition.Normalize(token), out var roleCode))
                    {
                        errors.Add(new(Roles, $"Roli „{token}\" nuk ekziston."));
                    }
                    else if ((office is not null || institution is not null)
                             && UserAdminSupport.ValidateRole(roleCode, institution is not null) is { } roleError)
                    {
                        errors.Add(new(Roles, roleError.Message));
                    }
                    else if (!roleCodes.Contains(roleCode))
                    {
                        roleCodes.Add(roleCode);
                    }
                }
            }

            if (r[Position] is { Length: > MaxPosition })
            {
                errors.Add(new(Position, $"Deri në {MaxPosition} shkronja."));
            }

            var active = r[Active] is null ? true : YesNo(r[Active]);
            if (active is null)
            {
                errors.Add(new(Active, "Shkruani PO ose JO."));
            }

            var wantsUpdate = r[Update] is null ? false : YesNo(r[Update]);
            if (wantsUpdate is null)
            {
                errors.Add(new(Update, "Shkruani PO ose JO."));
            }

            var current = email.Length > 0 && byEmail.TryGetValue(email, out var u) ? u : null;
            var name = current is null ? $"{first} {last}".Trim() : $"{current.FirstName} {current.LastName}";
            if (errors.Count > 0)
            {
                result.Add(Error(r, email, name, errors));
                continue;
            }

            if (current is null)
            {
                result.Add(new ImportPlan(
                    new ImportRowView(r.Line, email, ImportStatus.New, name,
                        [new(OfficeCode, null, office?.Code ?? institution!.Name), new(Roles, null, string.Join(";", roleCodes))], [], null),
                    new Payload(null, email, first!, last!, office?.PublicId, institution?.PublicId, roleCodes, roleCodes, [],
                        r[Position], active!.Value, false, active is false, r[Position] is not null)));
                continue;
            }

            if (wantsUpdate is not true)
            {
                result.Add(new ImportPlan(new ImportRowView(r.Line, email, ImportStatus.Exists, name, [], [],
                    "Përdoruesi ekziston; vendosni perditeso = PO për ta përditësuar."), null));
                continue;
            }

            // an existing user, to update: the changes, then the administration's guards
            var held = userRoles.Where(x => x.UserId == current.Id).Select(x => x.Code).ToList();
            var toAdd = roleCodes.Except(held).ToList();
            var toRemove = held.Except(roleCodes).ToList();
            var oldOrg = current.OfficeId is { } oid && officeById.TryGetValue(oid, out var oo) ? oo.Code
                : current.InstitutionId is { } iid && institutionById.TryGetValue(iid, out var oi) ? oi.Name : null;
            var newOrg = office?.Code ?? institution!.Name;
            var changes = new List<ImportChange>();
            if (current.FirstName != first) changes.Add(new(FirstName, current.FirstName, first));
            if (current.LastName != last) changes.Add(new(LastName, current.LastName, last));
            var orgChanged = current.OfficeId != office?.Id || current.InstitutionId != institution?.Id;
            if (orgChanged) changes.Add(new(office is not null ? OfficeCode : Institution, oldOrg, newOrg));
            if (toAdd.Count > 0 || toRemove.Count > 0) changes.Add(new(Roles, string.Join(";", held.Order()), string.Join(";", roleCodes.Order())));
            var positionChanged = current.Position != r[Position];
            if (positionChanged) changes.Add(new(Position, current.Position, r[Position]));
            var activeChanged = current.IsActive != active;
            if (activeChanged) changes.Add(new(Active, current.IsActive ? "PO" : "JO", active!.Value ? "PO" : "JO"));

            if ((current.InstitutionId is not null) != (institution is not null))
            {
                errors.Add(new(office is not null ? OfficeCode : Institution,
                    "Një përdorues i zyrës nuk kthehet në përdorues institucioni (dhe anasjelltas)."));
            }

            var headOf = heads.GetValueOrDefault(current.Id);
            if (headOf is not null && (orgChanged || active is false || toRemove.Contains(OfficeAdminSupport.HeadRole)))
            {
                errors.Add(new(string.Empty, $"Ky përdorues është Kryetar i zyrës {headOf}. Caktoni fillimisht një Kryetar tjetër."));
            }

            if (current.Id == actor.UserId && (active is false || toRemove.Contains(UserAdminSupport.AdminRole)))
            {
                errors.Add(new(string.Empty, "Nuk mund ta çaktivizoni veten ose t'i hiqni vetes rolin ADMIN."));
            }
            else if ((active is false || toRemove.Contains(UserAdminSupport.AdminRole))
                     && activeAdmins.Count == 1 && activeAdmins[0] == current.Id)
            {
                errors.Add(new(string.Empty, "Ky është administratori i fundit aktiv."));
            }

            foreach (var (role, caseRole, text) in new[] { ("STATE_LAWYER", "PRIMARY_LAWYER", "avokat primar"), ("SECONDARY_LAWYER", "SECONDARY_LAWYER", "avokat sekondar") })
            {
                if (toRemove.Contains(role) && await counts.CountOpenPositionsAsync(current.Id, caseRole, ct) is var open and > 0)
                {
                    errors.Add(new(Roles, $"Është {text} në {open} çështje të hapura; transferojini fillimisht."));
                }
            }

            if (errors.Count > 0)
            {
                result.Add(Error(r, email, name, errors));
                continue;
            }

            result.Add(new ImportPlan(
                new ImportRowView(r.Line, email, changes.Count == 0 ? ImportStatus.Unchanged : ImportStatus.Changed, name, changes, [], null),
                new Payload(current.PublicId, email, first!, last!, office?.PublicId, institution?.PublicId, roleCodes, toAdd, toRemove,
                    r[Position], active!.Value, current.FirstName != first || current.LastName != last || orgChanged, activeChanged,
                    positionChanged)));
        }

        return result;
    }

    private static ImportPlan Error(ImportRow r, string email, string? name, List<ImportCellError> errors) =>
        new(new ImportRowView(r.Line, email.Length > 0 ? email : null, ImportStatus.Error, name, [], errors, null), null);

    public async Task<Result<int>> ApplyAsync(ulong importId, IReadOnlyList<ImportPlan> rows, CancellationToken ct)
    {
        var written = 0;
        foreach (var plan in rows.Where(p => ImportStatus.Writes(p.Row.Status)))
        {
            var p = (Payload)plan.Payload!;
            var failure = p.ExistingPublicId is null ? await CreateAsync(p, ct) : await UpdateAsync(p, ct);
            if (failure is not null)
            {
                return Ash.SharedKernel.Results.Error.Conflict("import.row_failed",
                    $"Rreshti {plan.Row.Line} ({p.Email}): {failure.Message} Asgjë nuk u ruajt; ngarkoni skedarin përsëri.");
            }

            written++;
        }

        return written;
    }

    private async Task<Error?> CreateAsync(Payload p, CancellationToken ct)
    {
        var created = await create.Handle(new CreateUserRequest(p.Email, p.FirstName, p.LastName, p.OfficePublicId, p.InstitutionPublicId, p.Roles), ct);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (p.Position is not null)
        {
            await SetPositionAsync(created.Value.PublicId, p.Position, ct);
        }

        if (!p.Active)
        {
            var deactivated = await setActive.Handle(created.Value.PublicId, false, ct);
            if (deactivated.IsFailure)
            {
                return deactivated.Error;
            }
        }

        return null;
    }

    private async Task<Error?> UpdateAsync(Payload p, CancellationToken ct)
    {
        var id = p.ExistingPublicId!.Value;
        if (p.IdentityChanged)
        {
            var updated = await update.Handle(id, new UpdateUserRequest(p.Email, p.FirstName, p.LastName, p.OfficePublicId, p.InstitutionPublicId), ct);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        foreach (var role in p.RolesToAdd)
        {
            if ((await grant.Handle(id, role, ct)) is { IsFailure: true } granted)
            {
                return granted.Error;
            }
        }

        foreach (var role in p.RolesToRemove)
        {
            if ((await revoke.Handle(id, role, ct)) is { IsFailure: true } revoked)
            {
                return revoked.Error;
            }
        }

        if (p.PositionChanged)
        {
            await SetPositionAsync(id, p.Position, ct);
        }

        if (p.ActiveChanged && (await setActive.Handle(id, p.Active, ct)) is { IsFailure: true } activeResult)
        {
            return activeResult.Error;
        }

        return null;
    }

    private async Task SetPositionAsync(Guid publicId, string? position, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.PublicId == publicId, ct);
        user.Position = position;
        await db.SaveChangesAsync(ct);
    }
}
