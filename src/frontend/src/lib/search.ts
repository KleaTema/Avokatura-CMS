/** Accent- and case-insensitive form for client-side search: "Shëndetësisë" → "shendetesise". */
export const normalize = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** True when q is empty or appears in any of the fields (accent- and case-insensitive). */
export function matches(q: string, ...fields: (string | null | undefined)[]) {
  const needle = normalize(q.trim())
  return needle === '' || fields.some((f) => f != null && normalize(f).includes(needle))
}

/** Server-side search starts at 2 characters; shorter input means "no filter". */
export const MIN_SERVER_QUERY = 2

export const serverQuery = (q: string) => {
  const trimmed = q.trim()
  return trimmed.length >= MIN_SERVER_QUERY ? trimmed : ''
}
