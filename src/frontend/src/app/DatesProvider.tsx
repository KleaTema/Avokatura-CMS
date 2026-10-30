import { LocalizationProvider } from '@mui/x-date-pickers'
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs'
import 'dayjs/locale/sq'
import type { ComponentProps, ReactNode } from 'react'
import { sq } from '../i18n/sq'

const p = sq.pickers

/** Albanian texts for the pickers; month and day names come from the dayjs "sq" locale. */
const localeText: ComponentProps<typeof LocalizationProvider>['localeText'] = {
  previousMonth: p.previousMonth,
  nextMonth: p.nextMonth,
  openPreviousView: p.openPreviousView,
  openNextView: p.openNextView,
  calendarViewSwitchingButtonAriaLabel: (view) => (view === 'year' ? p.yearViewOpen : p.calendarViewOpen),
  cancelButtonLabel: p.cancel,
  clearButtonLabel: p.clear,
  okButtonLabel: p.ok,
  todayButtonLabel: p.today,
  nextStepButtonLabel: p.next,
  datePickerToolbarTitle: p.datePickerTitle,
  timePickerToolbarTitle: p.timePickerTitle,
  dateTimePickerToolbarTitle: p.dateTimePickerTitle,
  selectViewText: (view) => p.selectView(p.views[view] ?? view),
  openDatePickerDialogue: (formatted) => p.chooseDate(formatted),
  openTimePickerDialogue: (formatted) => p.chooseTime(formatted),
  fieldClearLabel: p.clear,
  timeTableLabel: p.timeTable,
  dateTableLabel: p.dateTable,
  fieldYearPlaceholder: (params) => p.yearPlaceholder.repeat(params.digitAmount),
  fieldDayPlaceholder: () => p.dayPlaceholder,
  fieldHoursPlaceholder: () => p.hoursPlaceholder,
  fieldMinutesPlaceholder: () => p.minutesPlaceholder,
  year: p.views.year,
  month: p.views.month,
  day: p.views.day,
  hours: p.views.hours,
  minutes: p.views.minutes,
  seconds: p.views.seconds,
  empty: p.empty,
}

/** Date and time pickers: dayjs with the Albanian locale (weeks start on Monday). */
export function DatesProvider({ children }: { children: ReactNode }) {
  return (
    <LocalizationProvider dateAdapter={AdapterDayjs} adapterLocale="sq" localeText={localeText}>
      {children}
    </LocalizationProvider>
  )
}
