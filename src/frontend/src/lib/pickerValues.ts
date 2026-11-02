import dayjs, { type Dayjs } from 'dayjs'
import { formatDateTime, tiraneLocalToUtcIso, todayInTirane } from './dates'

/** A picker value: null when empty; an invalid Dayjs while a typed date is incomplete or wrong. */
export type PickerValue = Dayjs | null

export const isPickerValue = (value: unknown): value is PickerValue => value === null || dayjs.isDayjs(value)

export const isFilled = (value: PickerValue): value is Dayjs => value !== null && value.isValid()

/** The calendar date as yyyy-MM-dd (what was picked or typed, no time zone involved). */
export const isoDateOf = (value: PickerValue): string | null => (isFilled(value) ? value.format('YYYY-MM-DD') : null)

/** The wall-clock time as HH:mm (24h). */
export const timeOf = (value: PickerValue): string | null => (isFilled(value) ? value.format('HH:mm') : null)

/** Today's date in Tirane as a picker value (the browser may be in another time zone). */
export const todayValue = (): Dayjs => dayjs(todayInTirane())

/**
 * The time picker builds its values on this day: a winter day without clock change, so a time typed on a
 * browser-side DST change day is never shifted. Only HH:mm of the value is used.
 */
export const TIME_REFERENCE = dayjs('2000-01-15T12:00')

/**
 * The other way: a UTC instant from the API → the date and time pickers' values (Tirane wall clock), e.g. to edit a
 * hearing. Both null for a missing or invalid value.
 */
export function pickerValuesOfUtc(utcIso: string | null | undefined): { date: PickerValue; time: PickerValue } {
  const shown = formatDateTime(utcIso) // dd.MM.yyyy HH:mm in Tirane
  if (!shown) return { date: null, time: null }
  const date = dayjs(`${shown.slice(6, 10)}-${shown.slice(3, 5)}-${shown.slice(0, 2)}`)
  const time = TIME_REFERENCE.hour(Number(shown.slice(11, 13))).minute(Number(shown.slice(14, 16))).second(0)
  return { date, time }
}

/** Date picker + time picker (Tirane wall clock) → UTC ISO, or null (empty, invalid, or skipped by the DST change). */
export function utcIsoOf(date: PickerValue, time: PickerValue): string | null {
  const d = isoDateOf(date)
  const t = timeOf(time)
  return d && t ? tiraneLocalToUtcIso(d, t) : null
}
