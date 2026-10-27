using Ash.SharedKernel.Ids;
using Shouldly;

namespace Ash.Tests.Unit;

public class PublicIdTests
{
    private static readonly Guid KnownGuid = Guid.Parse("6ccd780c-baba-1026-9564-5b8c656024db");
    private static readonly byte[] KnownBytes = Convert.FromHexString("1026BABA6CCD780C95645B8C656024DB");

    [Fact]
    public void ToBytes_MatchesMySqlUuidToBin()
    {
        PublicId.ToBytes(KnownGuid).ShouldBe(KnownBytes);
    }

    [Fact]
    public void FromBytes_MatchesMySqlBinToUuid()
    {
        PublicId.FromBytes(KnownBytes).ShouldBe(KnownGuid);
    }

    [Fact]
    public void RoundTrip_ReturnsSameGuid()
    {
        var id = Guid.NewGuid();
        PublicId.FromBytes(PublicId.ToBytes(id)).ShouldBe(id);
    }

    [Fact]
    public void FromBytes_WrongLength_Throws()
    {
        Should.Throw<ArgumentException>(() => PublicId.FromBytes(new byte[15]));
    }
}
