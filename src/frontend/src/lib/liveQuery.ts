import { keepPreviousData } from '@tanstack/react-query'

/**
 * Live search — the query options of a searched server list: keyed by the filters (the search text among them), so a
 * newer text is a newer query; react-query's `signal` goes to the request, so the older one is aborted and its late
 * answer can only land in its own (abandoned) cache entry, never on the newer rows; the previous rows stay on screen
 * while the next ones load (`keepPreviousData`, `isPlaceholderData`). Spread into useQuery with the hook's own options.
 */
export function liveListQuery<TData, TKey extends readonly unknown[]>(
  queryKey: TKey,
  request: (signal: AbortSignal) => Promise<TData>,
) {
  return {
    queryKey,
    placeholderData: keepPreviousData,
    queryFn: ({ signal }: { signal: AbortSignal }) => request(signal),
  }
}
