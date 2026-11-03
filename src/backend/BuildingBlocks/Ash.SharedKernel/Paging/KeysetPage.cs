namespace Ash.SharedKernel.Paging;

/// <summary>
/// One page of a keyset-paged list. Total = how many rows match the same filters (a COUNT run in parallel with the
/// page query), so the UI can show "11–20 nga 45"; it may differ by a row or two from the pages under concurrent writes.
/// </summary>
public sealed record KeysetPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total);
