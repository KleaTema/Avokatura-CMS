namespace Ash.Modules.Reference.Infrastructure;

public sealed class CourtLevelRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public byte SortOrder { get; set; }
    public bool AllowsDelegation { get; set; }
    public string? AppealKind { get; set; }
}

public sealed class CourtRecord
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
    public byte CourtLevelId { get; set; }
    public string? City { get; set; }
    public bool IsForeign { get; set; }
    public bool IsActive { get; set; }
}

public sealed class JudgeRecord
{
    public uint Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public ushort? CourtId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PartyRoleRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ReportGroup { get; set; } = "";
    public byte SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SummonsTypeRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class HearingTypeRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOther { get; set; }
    public byte SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DocumentTypeRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsOther { get; set; }
    public byte SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DecisionResultRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string OutcomeGroup { get; set; } = "";
    public bool SendsBackForRetrial { get; set; }
    public bool IsOther { get; set; }
    public byte SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CaseCategoryRecord
{
    public byte Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDelegation { get; set; }
    public byte SortOrder { get; set; }
}

public sealed class CaseSubcategoryRecord
{
    public byte Id { get; set; }
    public byte CategoryId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public byte SortOrder { get; set; }
}

public sealed class AllowedFileTypeRecord
{
    public byte Id { get; set; }
    public string Extension { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string MagicHex { get; set; } = "";
    public ulong MaxSizeBytes { get; set; }
    public string AppliesTo { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class DossierCodeFormatRecord
{
    public byte Id { get; set; }
    public string Template { get; set; } = "";
    public byte RandomDigits { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
}
