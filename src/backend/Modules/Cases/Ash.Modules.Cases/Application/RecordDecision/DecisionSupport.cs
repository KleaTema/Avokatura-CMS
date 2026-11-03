using System.Text.RegularExpressions;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Application.RecordDecision;

/// <summary>A decision request checked and parsed (money as decimals, the result resolved).</summary>
internal sealed record DecisionValues(
    DecisionResultDto Result,
    string? ResultOtherText,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    string? Dispositive,
    string Currency,
    decimal? WonAmount,
    decimal? WonAreaM2,
    decimal? LostAmount,
    decimal? LostAreaM2,
    decimal? PreventedDamageAmount,
    decimal? PreventedDamageAreaM2,
    decimal? CausedDamageAmount,
    decimal? CausedDamageAreaM2,
    decimal? CourtCostsAmount,
    bool DecisionReceived,
    Guid? DecisionDocumentPublicId,
    bool IsAppealed,
    bool IsRecursed,
    bool IsFinal);

/// <summary>
/// What POST and PUT /decision share (tasks 036/037): the validation (court level's appeal kind, dates, money), the
/// decision document check, the API view and the history snapshot. Albanian messages under camelCase field names.
/// </summary>
internal sealed partial class DecisionSupport(IReferenceData reference, IDocumentService documents, IClock clock)
{
    public const string Permission = "decision.record";
    public const string DecisionDocumentType = "VENDIM";
    public const string DefaultCurrency = "ALL";
    public const int MaxOtherTextLength = 300;
    public const int MaxNumberLength = 50;
    public const int MaxDispositiveLength = 8000;

    /// <summary>court_levels.appeal_kind: which checkbox the level has.</summary>
    public const string Ankim = "ANKIM";
    public const string Rekurs = "REKURS";

    public const string FinalWithAppealMessage = "Një vendim në formë të prerë nuk mund të jetë i ankimuar/rekursuar.";

    private const string MoneyMessage =
        "Shuma duhet të jetë një numër jo negativ me deri në 2 shifra pas pikës (p.sh. 1250000.50) dhe deri në 16 shifra para saj.";
    private const string AreaMessage =
        "Sipërfaqja duhet të jetë një numër jo negativ me deri në 2 shifra pas pikës dhe deri në 12 shifra para saj.";

    /// <summary>
    /// The request checked against the case's level: the result (active; "Tjetër" needs its text), number, date (not
    /// after today in Tirane, not before the act date), dispositive, money ≥ 0 as strings, one currency, the appeal
    /// checkboxes by the level's appeal kind, final vs appealed/recursed, the document when received, the rowVersion.
    /// </summary>
    public async Task<Result<DecisionValues>> ValidateAsync(DecisionRequest req, Case c, CancellationToken ct)
    {
        var errors = new FieldErrors();

        DecisionResultDto? result = null;
        if (string.IsNullOrWhiteSpace(req.ResultCode))
        {
            errors.Add("resultCode", "Zgjidhni rezultatin e çështjes.");
        }
        else
        {
            result = await reference.GetResultByCode(req.ResultCode.Trim(), ct);
            if (result is not { IsActive: true })
            {
                errors.Add("resultCode", "Rezultati nuk ekziston ose nuk është aktiv.");
                result = null;
            }
        }

        var otherText = Clean(req.ResultOtherText);
        if (result is { IsOther: true } && otherText is null)
        {
            errors.Add("resultOtherText", "Përshkruani rezultatin.");
        }
        else if (otherText is { Length: > MaxOtherTextLength })
        {
            errors.Add("resultOtherText", $"Përshkrimi i rezultatit mund të ketë deri në {MaxOtherTextLength} karaktere.");
        }

        var number = Clean(req.DecisionNumber);
        if (number is { Length: > MaxNumberLength })
        {
            errors.Add("decisionNumber", $"Numri i vendimit mund të ketë deri në {MaxNumberLength} karaktere.");
        }

        if (req.DecisionDate is { } date)
        {
            if (date > TiraneTime.Today(clock))
            {
                errors.Add("decisionDate", "Data e vendimit nuk mund të jetë në të ardhmen.");
            }
            else if (c.ActDate is { } actDate && date < actDate)
            {
                errors.Add("decisionDate", "Data e vendimit nuk mund të jetë para datës së aktit.");
            }
        }

        var dispositive = Clean(req.Dispositive);
        if (dispositive is { Length: > MaxDispositiveLength })
        {
            errors.Add("dispositive", $"Dispozitivi mund të ketë deri në {MaxDispositiveLength} karaktere.");
        }

        var currency = Clean(req.Currency) ?? DefaultCurrency;
        if (!CurrencyCode().IsMatch(currency))
        {
            errors.Add("currency", "Monedha duhet të jetë kodi ISO me 3 shkronja, p.sh. ALL ose EUR.");
        }

        decimal? Money(string? text, string field) => Parse(text, field, DecimalText.MoneyIntegerDigits, MoneyMessage, errors);
        decimal? Area(string? text, string field) => Parse(text, field, DecimalText.AreaIntegerDigits, AreaMessage, errors);
        var won = Money(req.WonAmount, "wonAmount");
        var wonArea = Area(req.WonAreaM2, "wonAreaM2");
        var lost = Money(req.LostAmount, "lostAmount");
        var lostArea = Area(req.LostAreaM2, "lostAreaM2");
        var prevented = Money(req.PreventedDamageAmount, "preventedDamageAmount");
        var preventedArea = Area(req.PreventedDamageAreaM2, "preventedDamageAreaM2");
        var caused = Money(req.CausedDamageAmount, "causedDamageAmount");
        var causedArea = Area(req.CausedDamageAreaM2, "causedDamageAreaM2");
        var costs = Money(req.CourtCostsAmount, "courtCostsAmount");

        // the checkbox the level has (court_levels.appeal_kind); "Formë e prerë" on every level
        var appealKind = (await reference.GetCourtLevel(c.CourtLevelId, ct))?.AppealKind;
        var appealed = req.IsAppealed ?? false;
        var recursed = req.IsRecursed ?? false;
        var final = req.IsFinal ?? false;
        if (appealed && appealKind != Ankim)
        {
            errors.Add("isAppealed", "Ankimi nuk vlen për këtë shkallë gjykimi.");
        }

        if (recursed && appealKind != Rekurs)
        {
            errors.Add("isRecursed", "Rekursi nuk vlen për këtë shkallë gjykimi.");
        }

        // before the database's ck_cd_final_vs_appeal: the user gets the reason, not a 500
        if (final && (appealed || recursed))
        {
            errors.Add("isFinal", FinalWithAppealMessage);
        }

        var received = req.DecisionReceived ?? false;
        if (received && req.DecisionDocumentPublicId is null)
        {
            errors.Add("decisionDocumentPublicId", "Bashkëngjitni vendimin e arsyetuar (dokument i llojit Vendim).");
        }

        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni. Rifreskoni faqen.");
        }

        if (errors.Any)
        {
            return errors.ToError(final && (appealed || recursed) ? FinalWithAppealMessage : "Të dhënat e vendimit nuk janë të vlefshme.");
        }

        return new DecisionValues(result!, result!.IsOther ? otherText : null, number, req.DecisionDate, dispositive, currency,
            won, wonArea, lost, lostArea, prevented, preventedArea, caused, causedArea, costs,
            received, req.DecisionDocumentPublicId, appealed, recursed, final);
    }

    /// <summary>
    /// The document to link: an existing, not deleted, CLEAN document of type VENDIM in the case's dossier. Its internal
    /// id, or the 400 under decisionDocumentPublicId.
    /// </summary>
    public async Task<Result<long>> DocumentAsync(Guid publicId, Case c, CancellationToken ct)
    {
        var document = await documents.GetAsync(publicId, ct);
        string? problem = document switch
        {
            null => "Dokumenti nuk u gjet në këtë dosje.",
            { IsDeleted: true } => "Dokumenti nuk u gjet në këtë dosje.",
            _ when document.DossierId != (long)c.DossierId => "Dokumenti nuk u gjet në këtë dosje.",
            _ when document.DocumentTypeCode != DecisionDocumentType => "Dokumenti duhet të jetë i llojit Vendim.",
            _ when document.ScanStatus != "CLEAN" => "Dokumenti nuk është kontrolluar ende ose u refuzua nga kontrolli antivirus.",
            _ => null
        };
        if (problem is null)
        {
            return document!.Id;
        }

        var errors = new FieldErrors();
        errors.Add("decisionDocumentPublicId", problem);
        return errors.ToError("Të dhënat e vendimit nuk janë të vlefshme.");
    }

    /// <summary>The decision as GET /api/cases/{id} and both decision endpoints return it.</summary>
    public async Task<CaseDecisionSummary> ViewAsync(CaseDecision d, CancellationToken ct)
    {
        var result = (await reference.ListDecisionResultsAsync(ct)).FirstOrDefault(r => r.Id == d.ResultId);
        var document = d.DecisionDocumentId is { } documentId ? await documents.GetByIdAsync((long)documentId, ct) : null;
        return new CaseDecisionSummary(
            new CodeName(result?.Code ?? string.Empty, result?.Name ?? string.Empty),
            d.ResultOtherText, d.DecisionNumber, d.DecisionDate, d.DecisionReceived, d.IsAppealed, d.IsRecursed, d.IsFinal,
            d.Dispositive, d.Currency,
            DecimalText.Format(d.WonAmount), DecimalText.Format(d.WonAreaM2),
            DecimalText.Format(d.LostAmount), DecimalText.Format(d.LostAreaM2),
            DecimalText.Format(d.PreventedDamageAmount), DecimalText.Format(d.PreventedDamageAreaM2),
            DecimalText.Format(d.CausedDamageAmount), DecimalText.Format(d.CausedDamageAreaM2),
            DecimalText.Format(d.CourtCostsAmount),
            d.DecisionReceivedAt,
            document is null ? null : new DecisionDocumentView(document.PublicId, document.Title),
            result?.SendsBackForRetrial ?? false,
            d.RecordedAt,
            d.RowVersion);
    }

    /// <summary>The values a decision row gets from the request (everything but the document link and the stamps).</summary>
    public static void Apply(CaseDecision d, DecisionValues v)
    {
        d.ResultId = v.Result.Id;
        d.ResultOtherText = v.ResultOtherText;
        d.DecisionNumber = v.DecisionNumber;
        d.DecisionDate = v.DecisionDate;
        d.Dispositive = v.Dispositive;
        d.Currency = v.Currency;
        d.WonAmount = v.WonAmount;
        d.WonAreaM2 = v.WonAreaM2;
        d.LostAmount = v.LostAmount;
        d.LostAreaM2 = v.LostAreaM2;
        d.PreventedDamageAmount = v.PreventedDamageAmount;
        d.PreventedDamageAreaM2 = v.PreventedDamageAreaM2;
        d.CausedDamageAmount = v.CausedDamageAmount;
        d.CausedDamageAreaM2 = v.CausedDamageAreaM2;
        d.CourtCostsAmount = v.CourtCostsAmount;
        d.IsAppealed = v.IsAppealed;
        d.IsRecursed = v.IsRecursed;
        d.IsFinal = v.IsFinal;
    }

    /// <summary>
    /// The decision's fields for the history (money as strings, the result by name, the document by title) — the
    /// changed ones are compared key by key (DECISION_UPDATED keeps only those).
    /// </summary>
    public async Task<Dictionary<string, object?>> SnapshotAsync(CaseDecision d, CancellationToken ct)
    {
        var result = (await reference.ListDecisionResultsAsync(ct)).FirstOrDefault(r => r.Id == d.ResultId);
        var document = d.DecisionDocumentId is { } documentId ? await documents.GetByIdAsync((long)documentId, ct) : null;
        return new Dictionary<string, object?>
        {
            ["result"] = result?.Name,
            ["resultOtherText"] = d.ResultOtherText,
            ["decisionNumber"] = d.DecisionNumber,
            ["decisionDate"] = d.DecisionDate?.ToString("yyyy-MM-dd"),
            ["dispositive"] = d.Dispositive,
            ["currency"] = d.Currency,
            ["wonAmount"] = DecimalText.Format(d.WonAmount),
            ["wonAreaM2"] = DecimalText.Format(d.WonAreaM2),
            ["lostAmount"] = DecimalText.Format(d.LostAmount),
            ["lostAreaM2"] = DecimalText.Format(d.LostAreaM2),
            ["preventedDamageAmount"] = DecimalText.Format(d.PreventedDamageAmount),
            ["preventedDamageAreaM2"] = DecimalText.Format(d.PreventedDamageAreaM2),
            ["causedDamageAmount"] = DecimalText.Format(d.CausedDamageAmount),
            ["causedDamageAreaM2"] = DecimalText.Format(d.CausedDamageAreaM2),
            ["courtCostsAmount"] = DecimalText.Format(d.CourtCostsAmount),
            ["decisionReceived"] = d.DecisionReceived,
            ["decisionDocument"] = document?.Title,
            ["isAppealed"] = d.IsAppealed,
            ["isRecursed"] = d.IsRecursed,
            ["isFinal"] = d.IsFinal,
        };
    }

    public static IReadOnlyList<string> Suggestions(DecisionResultDto result, bool suggestArchive)
    {
        var suggestions = new List<string>();
        if (result.SendsBackForRetrial)
        {
            suggestions.Add(DecisionSuggestions.RetrialExpected);
        }

        if (suggestArchive)
        {
            suggestions.Add(DecisionSuggestions.ArchiveSuggested);
        }

        return suggestions;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static decimal? Parse(string? text, string field, int integerDigits, string message, FieldErrors errors)
    {
        if (Clean(text) is not { } value)
        {
            return null;
        }

        if (DecimalText.TryParse(value, integerDigits, out var parsed))
        {
            return parsed;
        }

        errors.Add(field, message);
        return null;
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();
}
