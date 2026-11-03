using Ash.SharedKernel.Time;

namespace Ash.Infrastructure.Storage;

/// <summary>Object keys: "yyyy/MM/&lt;uuid&gt;" (UTC). Never derived from a user's file name.</summary>
public static class StorageKeys
{
    public static string New(IClock clock)
    {
        var now = clock.UtcNow;
        return $"{now:yyyy}/{now:MM}/{Guid.NewGuid():D}";
    }
}
