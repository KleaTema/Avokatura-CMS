const TIME_ZONE = 'Europe/Tirane'

const formatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

const MINUTE = 60_000
const DAY = 24 * 60 * MINUTE

const tiraneParts = (utcMs: number) =>
  Object.fromEntries(formatter.formatToParts(new Date(utcMs)).map((p) => [p.type, p.value]))

/** The Tirane wall-clock time of a UTC instant, as if it were UTC (milliseconds, whole minutes). */
function tiraneWallMs(utcMs: number): number {
  const p = tiraneParts(utcMs)
  return Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour, +p.minute)
}

const offsetMinutesAt = (utcMs: number) => Math.round((tiraneWallMs(utcMs) - utcMs) / MINUTE)

const pad = (n: number) => String(n).padStart(2, '0')

/** Today's calendar date in Tirane as yyyy-MM-dd. */
export function todayInTirane(now: Date = new Date()): string {
  const p = tiraneParts(now.getTime())
  return `${p.year}-${p.month}-${p.day}`
}

/** yyyy-MM-dd → dd.MM.yyyy (empty for anything else). */
export function isoDateToDisplay(iso: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? '')
  return m ? `${m[3]}.${m[2]}.${m[1]}` : ''
}

/** dd.MM.yyyy (d.M.yyyy accepted) → yyyy-MM-dd, or null when it is not a real calendar date. */
export function parseDisplayDate(text: string): string | null {
  const m = /^(\d{1,2})\.(\d{1,2})\.(\d{4})$/.exec(text.trim())
  if (!m) return null
  const [day, month, year] = [+m[1], +m[2], +m[3]]
  const date = new Date(Date.UTC(year, month - 1, day))
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null
  return `${year}-${pad(month)}-${pad(day)}`
}

/**
 * A wall-clock time in Tirane (date yyyy-MM-dd, time HH:mm, 24h) → UTC ISO string ("…Z"), correct across DST:
 * - a time skipped when clocks go forward (last Sunday of March, 02:00–02:59) does not exist → null;
 * - a time repeated when clocks go back (last Sunday of October, 02:00–02:59) → the first one (summer time).
 */
export function tiraneLocalToUtcIso(date: string, time: string): string | null {
  const d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date)
  const t = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(time)
  if (!d || !t || parseDisplayDate(`${d[3]}.${d[2]}.${d[1]}`) === null) return null
  const wall = Date.UTC(+d[1], +d[2] - 1, +d[3], +t[1], +t[2])
  // the offsets in force around that day; at most two (standard and summer time)
  const offsets = new Set([offsetMinutesAt(wall - DAY), offsetMinutesAt(wall), offsetMinutesAt(wall + DAY)])
  const candidates = [...offsets]
    .map((offset) => wall - offset * MINUTE)
    .filter((utc) => tiraneWallMs(utc) === wall)
    .sort((a, b) => a - b)
  return candidates.length > 0 ? new Date(candidates[0]).toISOString().replace('.000Z', 'Z') : null
}

/** Formats a UTC ISO string as the Tirane calendar date dd.MM.yyyy. */
export const formatDate = (utcIso: string | null | undefined): string => formatDateTime(utcIso).slice(0, 10)

/** Formats a UTC ISO string as dd.MM.yyyy HH:mm in Europe/Tirane. */
export function formatDateTime(utcIso: string | null | undefined): string {
  if (!utcIso) return ''
  const date = new Date(utcIso)
  if (Number.isNaN(date.getTime())) return ''
  const parts = Object.fromEntries(formatter.formatToParts(date).map((p) => [p.type, p.value]))
  return `${parts.day}.${parts.month}.${parts.year} ${parts.hour}:${parts.minute}`
}
