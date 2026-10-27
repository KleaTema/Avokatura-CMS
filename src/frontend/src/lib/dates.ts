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

/** Formats a UTC ISO string as dd.MM.yyyy HH:mm in Europe/Tirane. */
export function formatDateTime(utcIso: string | null | undefined): string {
  if (!utcIso) return ''
  const date = new Date(utcIso)
  if (Number.isNaN(date.getTime())) return ''
  const parts = Object.fromEntries(formatter.formatToParts(date).map((p) => [p.type, p.value]))
  return `${parts.day}.${parts.month}.${parts.year} ${parts.hour}:${parts.minute}`
}
