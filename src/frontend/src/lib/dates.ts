import dayjs from 'dayjs'
import 'dayjs/locale/sq'

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

/**
 * A calendar date from the API (DateOnly, "yyyy-MM-dd") → "dd.MM.yyyy". Pure text: no Date object and no time zone,
 * so the day can never shift. Use it for every DateOnly field; empty for anything else.
 */
export function formatDate(dateOnly: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(dateOnly ?? '')
  return m ? `${m[3]}.${m[2]}.${m[1]}` : ''
}

/** A calendar date ("yyyy-MM-dd") plus n days, computed on the calendar only (no time zone). */
export function addDays(dateOnly: string, days: number): string {
  const [y, m, d] = dateOnly.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d + days))
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`
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

const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1).toLowerCase()

/**
 * "E premte, 30 Tetor 2026": a long date in Albanian, the Tirane calendar day (page headers). Names come from the
 * bundled dayjs "sq" locale, not Intl: browsers' locale data often lacks Albanian and falls back to English.
 */
export function formatLongDate(when: Date = new Date()): string {
  const day = dayjs(todayInTirane(when)).locale('sq')
  return `${capitalize(day.format('dddd'))}, ${day.format('D')} ${capitalize(day.format('MMMM'))} ${day.format('YYYY')}`
}

/** The Tirane calendar date (dd.MM.yyyy) of a UTC instant (an ISO date-time, e.g. closedAt) — not for DateOnly fields. */
export const formatTiraneDate = (utcIso: string | null | undefined): string => formatDateTime(utcIso).slice(0, 10)

/** The Tirane calendar date ("yyyy-MM-dd") of a UTC instant, or null. */
export function tiraneDateOf(utcIso: string | null | undefined): string | null {
  const shown = formatTiraneDate(utcIso)
  return shown ? `${shown.slice(6, 10)}-${shown.slice(3, 5)}-${shown.slice(0, 2)}` : null
}

/** Formats a UTC ISO string as dd.MM.yyyy HH:mm in Europe/Tirane. */
export function formatDateTime(utcIso: string | null | undefined): string {
  if (!utcIso) return ''
  const date = new Date(utcIso)
  if (Number.isNaN(date.getTime())) return ''
  const parts = Object.fromEntries(formatter.formatToParts(date).map((p) => [p.type, p.value]))
  return `${parts.day}.${parts.month}.${parts.year} ${parts.hour}:${parts.minute}`
}
