namespace Ash.SharedKernel.Paging;

public sealed record KeysetPage<T>(IReadOnlyList<T> Items, string? NextCursor);
