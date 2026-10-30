import dayjs, { type Dayjs } from 'dayjs'
import { tiraneLocalToUtcIso, todayInTirane } from './dates'

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

/** Date picker + time picker (Tirane wall clock) → UTC ISO, or null (empty, invalid, or skipped by the DST change). */
export function utcIsoOf(date: PickerValue, time: PickerValue): string | null {
  const d = isoDateOf(date)
  const t = timeOf(time)
  return d && t ? tiraneLocalToUtcIso(d, t) : null
}
