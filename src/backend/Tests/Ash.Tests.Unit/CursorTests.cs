using Ash.SharedKernel.Paging;
using Shouldly;

namespace Ash.Tests.Unit;

public class CursorTests
{
    [Theory]
    [InlineData("2025-01-02T10:00:00Z", 42L)]
    [InlineData("a|b|c", 7L)]
    [InlineData("shqip ë ç", 1L)]
    [InlineData("", 9L)]
    public void EncodeDecode_RoundTrips(string sortKey, long id)
    {
        var cursor = Cursor.Encode(sortKey, id);

        Cursor.TryDecode(cursor, out var decodedKey, out var decodedId).ShouldBeTrue();
        decodedKey.ShouldBe(sortKey);
        decodedId.ShouldBe(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!not-base64!!!")]
    public void TryDecode_Invalid_ReturnsFalse(string? cursor)
    {
        Cursor.TryDecode(cursor, out _, out _).ShouldBeFalse();
    }
}
