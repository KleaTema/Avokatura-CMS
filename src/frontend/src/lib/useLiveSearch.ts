import { useDebounced } from './useDebounced'

/** Live search: the request follows the typing this long after the last keystroke. */
export const LIVE_SEARCH_DEBOUNCE_MS = 300

/**
 * Live search (search-as-you-type) — the ONE hook every searchable list uses with SearchField:
 * <ul>
 * <li>the text to send, trimmed, 300 ms after the last keystroke; from 1 character (no minimum);</li>
 * <li>cleared (X or an empty field): '' at once — the full list comes back without waiting;</li>
 * <li>the value goes into the list's react-query key, so a newer text is a newer query: the older request is aborted
 * (the hooks pass react-query's `signal`) and its late answer can never replace the newer rows.</li>
 * </ul>
 * Client-side lists filter with `matches()` (lib/search: accent- and case-insensitive, the same rule as the server).
 */
export function useLiveSearch(raw: string, delayMs = LIVE_SEARCH_DEBOUNCE_MS): string {
  const text = raw.trim()
  const debounced = useDebounced(text, delayMs)
  return text === '' ? '' : debounced
}
