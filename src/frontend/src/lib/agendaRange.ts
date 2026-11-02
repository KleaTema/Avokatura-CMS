import dayjs from 'dayjs'
import 'dayjs/locale/sq'
import { addDays } from './dates'

/** The agenda's views as the URL holds them (task 031). */
export const AGENDA_VIEWS = ['dita', 'java', 'muaji', 'lista'] as const
export type AgendaView = (typeof AGENDA_VIEWS)[number]

export const isAgendaView = (value: string | null): value is AgendaView => AGENDA_VIEWS.some((v) => v === value)

/** The FullCalendar view of each: the list shows one week, like the week view. */
export const CALENDAR_VIEW: Record<AgendaView, string> = {
  dita: 'timeGridDay',
  java: 'timeGridWeek',
  muaji: 'dayGridMonth',
  lista: 'listWeek',
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

/** A yyyy-MM-dd from the URL, or null when it is not a real calendar date. */
export function validDate(value: string | null): string | null {
  if (!value || !ISO_DATE.test(value)) return null
  const [y, m, d] = value.split('-').map(Number)
  const date = new Date(Date.UTC(y, m - 1, d))
  return date.getUTCMonth() === m - 1 && date.getUTCDate() === d ? value : null
}

/** The Monday of the week of a calendar date (weeks start on Monday). */
export function mondayOf(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay() // 0 = Sunday
  return addDays(date, -((weekday + 6) % 7))
}

const firstOfMonth = (date: string) => `${date.slice(0, 7)}-01`

/**
 * The calendar dates (both included) a view shows around `date` — what GET /api/agenda is asked for. The month view
 * shows six whole weeks from the Monday on or before the 1st (as FullCalendar's dayGridMonth does): 42 days.
 */
export function rangeOf(view: AgendaView, date: string): { from: string; to: string } {
  switch (view) {
    case 'dita':
      return { from: date, to: date }
    case 'muaji': {
      const from = mondayOf(firstOfMonth(date))
      return { from, to: addDays(from, 41) }
    }
    default: {
      const from = mondayOf(date)
      return { from, to: addDays(from, 6) }
    }
  }
}

/** The date one step back (-1) or forward (+1): a day, a week, or the 1st of the previous / next month. */
export function shift(view: AgendaView, date: string, step: -1 | 1): string {
  switch (view) {
    case 'dita':
      return addDays(date, step)
    case 'muaji':
      return dayjs(firstOfMonth(date)).add(step, 'month').format('YYYY-MM-DD')
    default:
      return addDays(date, 7 * step)
  }
}

/** As formatLongDate: "E hënë", "Nëntor". */
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1).toLowerCase()

/** "E hënë, 9 Nëntor 2026" · "9 – 15 Nëntor 2026" · "30 Nëntor – 6 Dhjetor 2026" · "Nëntor 2026" (dayjs "sq" names). */
export function titleOf(view: AgendaView, date: string): string {
  const d = (iso: string) => dayjs(iso).locale('sq')
  const month = (iso: string) => capitalize(d(iso).format('MMMM'))
  if (view === 'dita') return `${capitalize(d(date).format('dddd'))}, ${d(date).format('D')} ${month(date)} ${d(date).format('YYYY')}`
  if (view === 'muaji') return `${month(date)} ${d(date).format('YYYY')}`
  const { from, to } = rangeOf(view, date)
  const end = `${d(to).format('D')} ${month(to)} ${d(to).format('YYYY')}`
  if (from.slice(0, 7) === to.slice(0, 7)) return `${d(from).format('D')} – ${end}`
  if (from.slice(0, 4) === to.slice(0, 4)) return `${d(from).format('D')} ${month(from)} – ${end}`
  return `${d(from).format('D')} ${month(from)} ${d(from).format('YYYY')} – ${end}`
}
