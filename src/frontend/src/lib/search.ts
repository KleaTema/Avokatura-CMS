/**
 * Accent- and case-insensitive form for client-side search — the browser side of the server's utf8mb4_0900_ai_ci rule:
 * "Shëndetësisë" → "shendetesise", "Çela" → "cela".
 */
export const normalize = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/**
 * Live search on a list loaded whole (the same rule as the server's TextSearch): true when q is empty, or when every
 * whitespace-separated term of q appears (a substring, from 1 character) in one of the fields.
 */
export function matches(q: string, ...fields: (string | null | undefined)[]) {
  const terms = normalize(q.trim()).split(/\s+/).filter((x) => x !== '')
  if (terms.length === 0) return true
  const haystacks = fields.filter((f): f is string => f != null).map(normalize)
  return terms.every((term) => haystacks.some((h) => h.includes(term)))
}

/** A bounded lookup dropdown filters from the 2nd letter (task 054a): shorter text shows the whole list. */
export const PICKER_MIN_FILTER = 2

/**
 * The filter of a bounded lookup dropdown (institutions, courts, judges, simple lists — loaded whole, cached): the
 * full list until the text has 2 characters, then `matches()` (substring, case- and accent-insensitive).
 */
export function pickerMatches(q: string, ...fields: (string | null | undefined)[]) {
  return q.trim().length < PICKER_MIN_FILTER || matches(q, ...fields)
}
