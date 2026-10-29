export const BYTES_PER_MB = 1048576
export const MAX_SIZE_MB = 100

const mbFormat = new Intl.NumberFormat('sq-AL', { maximumFractionDigits: 2 })

/** Bytes shown as MB with up to two decimals, e.g. 52428800 → "50". */
export const formatMegabytes = (bytes: number) => mbFormat.format(bytes / BYTES_PER_MB)

/** Accepts "1.5" or "1,5". NaN when the text is not a number. */
export const parseMegabytes = (text: string) => Number(text.trim().replace(',', '.'))

export const megabytesToBytes = (mb: number) => Math.round(mb * BYTES_PER_MB)
