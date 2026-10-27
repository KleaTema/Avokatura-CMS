namespace Ash.SharedKernel.Ids;

/// <summary>
/// Converts between Guid and BINARY(16) using the MySQL UUID_TO_BIN(x, 1) / BIN_TO_UUID(x, 1) byte order.
/// </summary>
public static class PublicId
{
    // Canonical hex string positions (without dashes) of each stored byte:
    // time_hi(2) time_mid(2) time_low(4) clock_seq + node(8)
    public static byte[] ToBytes(Guid id)
    {
        var hex = id.ToString("N");
        var canonical = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            canonical[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        var bytes = new byte[16];
        bytes[0] = canonical[6];
        bytes[1] = canonical[7];
        bytes[2] = canonical[4];
        bytes[3] = canonical[5];
        bytes[4] = canonical[0];
        bytes[5] = canonical[1];
        bytes[6] = canonical[2];
        bytes[7] = canonical[3];
        Array.Copy(canonical, 8, bytes, 8, 8);
        return bytes;
    }

    public static Guid FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
        {
            throw new ArgumentException("A public id must be exactly 16 bytes.", nameof(bytes));
        }

        Span<byte> canonical = stackalloc byte[16];
        canonical[0] = bytes[4];
        canonical[1] = bytes[5];
        canonical[2] = bytes[6];
        canonical[3] = bytes[7];
        canonical[4] = bytes[2];
        canonical[5] = bytes[3];
        canonical[6] = bytes[0];
        canonical[7] = bytes[1];
        bytes[8..].CopyTo(canonical[8..]);

        return Guid.ParseExact(Convert.ToHexString(canonical), "N");
    }
}
