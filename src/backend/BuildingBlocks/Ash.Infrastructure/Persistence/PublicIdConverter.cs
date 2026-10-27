using Ash.SharedKernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ash.Infrastructure.Persistence;

public sealed class PublicIdConverter : ValueConverter<Guid, byte[]>
{
    public PublicIdConverter()
        : base(id => PublicId.ToBytes(id), bytes => PublicId.FromBytes(bytes))
    {
    }
}
