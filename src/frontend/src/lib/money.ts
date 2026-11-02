/**
 * Money and m² as decimal STRINGS end to end ("1250000.50", what the API sends and accepts). Never parsed into a JS
 * number: a double cannot hold DECIMAL(18,2) exactly (9999999999999999.99 would become 10000000000000000). Every
 * function here works on the digits as text.
 *
 * Albanian notation: "." groups thousands, "," is the decimal comma — 1.250.000,50.
 */

/** DECIMAL(18,2): 16 digits before the decimal comma. */
export const MONEY_INTEGER_DIGITS = 16
/** DECIMAL(14,2): 12 digits before the decimal comma (m²). */
export const AREA_INTEGER_DIGITS = 12

const CANONICAL = /^(\d+)(?:\.(\d{1,2}))?$/

const group = (digits: string) => digits.replace(/\B(?=(\d{3})+(?!\d))/g, '.')

const trimZeros = (digits: string) => digits.replace(/^0+(?=\d)/, '')

/** "1250000.5" → "1.250.000,50"; text that is not a canonical decimal is returned unchanged. */
export function formatDecimal(value: string): string {
  const match = CANONICAL.exec(value.trim())
  if (!match) return value
  const [, int, frac = ''] = match
  return `${group(trimZeros(int))},${frac.padEnd(2, '0')}`
}

/** "1250000.50", "EUR" → "1.250.000,50 EUR" (ALL when no currency is given). */
export function formatMoney(value: string, currency: string | null | undefined): string {
  return `${formatDecimal(value)} ${currency || 'ALL'}`
}

/**
 * What the user typed in Albanian notation → the API's string with two decimals: "1.250.000,50" → "1250000.50",
 * "1250000" → "1250000.00". Dots and spaces are grouping and are ignored; one decimal comma with at most 2 digits.
 * "" for an empty field; null when it is not a valid non-negative amount (letters, a sign, a second comma,
 * 3 decimals, too many digits).
 */
export function parseAlbanianMoneyInput(text: string, integerDigits = MONEY_INTEGER_DIGITS): string | null {
  const compact = text.replace(/[\s.]/g, '')
  if (compact === '') return ''
  const match = /^(\d+)(?:,(\d{0,2}))?$/.exec(compact)
  if (!match) return null
  const int = trimZeros(match[1])
  if (int.length > integerDigits) return null
  return `${int}.${(match[2] ?? '').padEnd(2, '0')}`
}

/** The API's value as the input shows it: "1250000.50" → "1.250.000,50"; null/empty → "". */
export function toAlbanianInput(value: string | null | undefined): string {
  return value ? formatDecimal(value) : ''
}

/**
 * Live formatting while typing: keeps only digits and the first comma, regroups the thousands, at most 2 decimals.
 * "1250000,5" → "1.250.000,5". Returns the new text and where the caret goes (after the same number of digits as
 * before), so typing in the middle does not jump to the end.
 */
export function formatWhileTyping(text: string, caret: number): { text: string; caret: number } {
  const digitsBefore = text.slice(0, caret).replace(/[^\d,]/g, '').length
  let seenComma = false
  let cleaned = ''
  for (const ch of text) {
    if (/\d/.test(ch)) cleaned += ch
    else if (ch === ',' && !seenComma) {
      seenComma = true
      cleaned += ch
    }
  }
  const [intPart, fracPart] = cleaned.split(',')
  const formatted = seenComma ? `${group(intPart)},${(fracPart ?? '').slice(0, 2)}` : group(intPart)

  // the caret after the same count of digits/comma as before the edit
  let count = 0
  let position = 0
  while (position < formatted.length && count < digitsBefore) {
    if (/[\d,]/.test(formatted[position])) count++
    position++
  }
  return { text: formatted, caret: position }
}
