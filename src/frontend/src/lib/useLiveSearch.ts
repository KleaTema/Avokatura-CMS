import { useDebounced } from './useDebounced'

/** Live search: the request follows the typing this long after the last keystroke. */
export const LIVE_SEARCH_DEBOUNCE_MS = 300

/**
 * The shortest term the server searches (TextSearch.MinTermLength): a 1-letter search scans the table (0.4–0.6 s on
 * 100k cases), so server lists ignore shorter terms. Lists filtered in the browser (matches()) keep 1 character.
 */
export const MIN_SERVER_TERM_LENGTH = 2

/** The text the server would search: its terms of at least 2 characters ('' when there is none). */
export function serverSearchText(raw: string): string {
  return raw
    .trim()
    .split(/\s+/)
    .filter((term) => term.length >= MIN_SERVER_TERM_LENGTH)
    .join(' ')
}

/**
 * Live search (search-as-you-type) — the ONE hook every server-searched list uses with SearchField:
 * <ul>
 * <li>the text to send, 300 ms after the last keystroke, from 2 characters: 1-character terms are dropped, so "a"
 * keeps the full list (as the server, TextSearch.MinTermLength);</li>
 * <li>cleared (X or an empty field, or only 1-character terms): '' at once — the full list comes back without waiting;</li>
 * <li>the value goes into the list's react-query key, so a newer text is a newer query: the older request is aborted
 * (the hooks pass react-query's `signal`) and its late answer can never replace the newer rows.</li>
 * </ul>
 * Client-side lists filter with `matches()` (lib/search: accent- and case-insensitive, from 1 character).
 */
export function useLiveSearch(raw: string, delayMs = LIVE_SEARCH_DEBOUNCE_MS): string {
  const text = serverSearchText(raw)
  const debounced = useDebounced(text, delayMs)
  return text === '' ? '' : debounced
}
