/** A piece of text, marked when it is one of the searched words. */
export interface TextPart {
  text: string
  match: boolean
}

/** One character without accents, lower case ("Ç" → "c", "ë" → "e"): the server's ai_ci collation, one char at a time. */
const fold = (ch: string) => ch.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/**
 * Splits <paramref name="text"/> into parts, marking every occurrence of a word of <paramref name="q"/> (2+ letters or
 * digits) — accent- and case-insensitive, as the search matched it ("ceshtje" marks "çështje", "640" marks the end of
 * "2026-686640"). The parts joined give back the text unchanged.
 */
export function highlightParts(text: string, q: string): TextPart[] {
  const found: string[] = q.match(/[\p{L}\p{N}]+/gu) ?? []
  const words = found.filter((w) => w.length >= 2).map((w) => [...w].map(fold).join(''))
  if (!text || words.length === 0) return text ? [{ text, match: false }] : []

  // folded text, one entry per original character, so a match maps back to the original positions
  const chars = [...text]
  const folded = chars.map(fold)
  const marked = new Array<boolean>(chars.length).fill(false)
  const flat = folded.join('')
  // start offsets of each original character in the folded string
  const starts: number[] = []
  let offset = 0
  for (const f of folded) {
    starts.push(offset)
    offset += f.length
  }

  for (const word of words) {
    for (let at = flat.indexOf(word); at >= 0; at = flat.indexOf(word, at + 1)) {
      const end = at + word.length
      chars.forEach((_, i) => {
        if (starts[i] >= at && starts[i] < end) marked[i] = true
      })
    }
  }

  const parts: TextPart[] = []
  chars.forEach((ch, i) => {
    const last = parts[parts.length - 1]
    if (last && last.match === marked[i]) last.text += ch
    else parts.push({ text: ch, match: marked[i] })
  })
  return parts
}
