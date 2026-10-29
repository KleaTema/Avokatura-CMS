using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ash.Infrastructure.Persistence;

/// <summary>
/// DATE columns as DateOnly: the MySQL provider reads DATE as DateTime and cannot cast it to DateOnly,
/// so the value goes through DateTime (midnight, no time zone — a calendar date is never converted to UTC).
/// </summary>
public sealed class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyConverter()
        : base(d => d.ToDateTime(TimeOnly.MinValue), dt => DateOnly.FromDateTime(dt))
    {
    }
}

public sealed class NullableDateOnlyConverter : ValueConverter<DateOnly?, DateTime?>
{
    public NullableDateOnlyConverter()
        : base(d => d.HasValue ? d.Value.ToDateTime(TimeOnly.MinValue) : null,
               dt => dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null)
    {
    }
}
