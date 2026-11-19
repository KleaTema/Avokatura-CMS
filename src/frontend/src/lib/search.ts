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
