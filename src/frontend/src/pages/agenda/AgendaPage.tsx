import type { DayHeaderContentArg, EventClickArg, EventContentArg, EventInput, EventMountArg } from '@fullcalendar/core'
import sqLocale from '@fullcalendar/core/locales/sq'
import dayGridPlugin from '@fullcalendar/daygrid'
import interactionPlugin from '@fullcalendar/interaction'
import listPlugin from '@fullcalendar/list'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  FormControlLabel,
  GlobalStyles,
  IconButton,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import dayjs from 'dayjs'
import { ChevronLeft, ChevronRight, Printer, TriangleAlert } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { OptionPicker } from '../../components/OptionPicker'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAgenda, type AgendaItem } from '../../features/agenda/api'
import { lawyerOptionsOf, useFilterOptions } from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { sq } from '../../i18n/sq'
import {
  AGENDA_VIEWS,
  CALENDAR_VIEW,
  isAgendaView,
  rangeOf,
  shift,
  titleOf,
  validDate,
  type AgendaView,
} from '../../lib/agendaRange'
import { formatDateTime, formatLongDate, tiraneWallClockIso, todayInTirane } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'
import { tokens, tones, type Tone } from '../../theme/tokens'

const t = sq.agenda

type Param = 'view' | 'date' | 'office' | 'lawyer' | 'cancelled'

const STATUS_TONE: Record<string, Tone> = { SCHEDULED: 'blue', HELD: 'green', POSTPONED: 'orange', CANCELLED: 'neutral' }

/** Visible hours: 07:00–19:00, widened when a hearing of the range falls outside them (never hidden). */
const DAY_START = 7
const DAY_END = 19
const hour = (h: number) => `${String(h).padStart(2, '0')}:00:00`
const TIME_24H = { hour: '2-digit', minute: '2-digit', hour12: false } as const

const PLUGINS = [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin]

/** "Now" for the calendar (today's column, the red line): the Tirane wall clock, asked by the calendar when it needs it. */
const tiraneNow = () => tiraneWallClockIso(new Date().toISOString())

const typeOf = (i: AgendaItem) => (i.otherTypeText ? `${i.hearingType.name}: ${i.otherTypeText}` : i.hearingType.name)
const hearingsPath = (i: AgendaItem) => `/cases/${i.casePublicId}/seancat`

/**
 * Agjenda (task 031): the hearings of the visible range from GET /api/agenda — the user's cases, the office's for the
 * Kryetar (lawyer filter), all for the Monitor (office + lawyer filters).
 *
 * Time zone: the calendar runs in UTC and every hearing is handed to it as its Tirane wall-clock time written as UTC
 * (lib/dates tiraneWallClockIso, DST-safe through Intl), so it shows Tirane times in any browser time zone without
 * the luxon plugin and its extra library. The visible range is computed here (lib/agendaRange) from the view and date
 * in the URL — Tirane calendar dates, exactly what the API takes.
 */
export function AgendaPage() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
  const theme = useTheme()
  const phone = useMediaQuery(theme.breakpoints.down('md')) // < 900 px
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const get = (name: Param) => params.get(name) ?? ''
  const set = (updates: Partial<Record<Param, string>>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [name, value] of Object.entries(updates)) {
          if (value) next.set(name, value)
          else next.delete(name)
        }
        return next
      },
      { replace: true },
    )

  const requestedView = params.get('view')
  const view: AgendaView = isAgendaView(requestedView) ? requestedView : phone ? 'lista' : 'java'
  const date = validDate(params.get('date')) ?? todayInTirane()
  const range = rangeOf(view, date)

  const canFilterOffice = permissions.includes('case.view_all')
  const canFilterLawyer = canFilterOffice || permissions.includes('case.view_office')
  const filterOptions = useFilterOptions(canFilterLawyer)
  const officeId = canFilterOffice ? get('office') : ''
  const lawyerId = canFilterLawyer ? get('lawyer') : ''
  const includeCancelled = get('cancelled') === '1'
  const officeOptions = (filterOptions.data?.offices ?? []).map((o) => ({ value: o.publicId, label: o.name }))
  const lawyerOptions = lawyerOptionsOf(filterOptions.data?.lawyers ?? [], officeId)
  const setOffice = (office: string) => {
    const stillThere = lawyerOptionsOf(filterOptions.data?.lawyers ?? [], office).some((x) => x.value === lawyerId)
    set({ office, lawyer: stillThere ? lawyerId : '' })
  }

  const agenda = useAgenda({ ...range, lawyerId, officeId, includeCancelled })
  const items = useMemo(() => agenda.data ?? [], [agenda.data])
  const byId = useMemo(() => new Map(items.map((i) => [i.hearingPublicId, i])), [items])
  const events: EventInput[] = useMemo(
    () =>
      items.map((i) => ({
        id: i.hearingPublicId,
        start: tiraneWallClockIso(i.scheduledAt),
        title: i.caseCode,
        url: hearingsPath(i),
      })),
    [items],
  )
  const hours = items.map((i) => new Date(tiraneWallClockIso(i.scheduledAt)).getUTCHours())
  const slotMin = hour(Math.min(DAY_START, ...hours))
  const slotMax = hour(Math.min(24, Math.max(DAY_END, ...hours.map((h) => h + 1))))

  // the calendar follows the URL (view, date); it is not remounted on every step. In a microtask: FullCalendar
  // renders its React content synchronously, which React refuses inside an effect (flushSync warning).
  const calendar = useRef<FullCalendar>(null)
  useEffect(() => {
    queueMicrotask(() => calendar.current?.getApi().changeView(CALENDAR_VIEW[view], date))
  }, [view, date])
  // hidden while printing, the calendar measures itself at zero size: measure again afterwards
  useEffect(() => {
    const remeasure = () => calendar.current?.getApi().updateSize()
    window.addEventListener('afterprint', remeasure)
    return () => window.removeEventListener('afterprint', remeasure)
  }, [])

  // stable callbacks: a new function on every render makes FullCalendar redraw its React content (flushSync warnings)
  const openHearing = useCallback(
    (arg: EventClickArg) => {
      const item = byId.get(arg.event.id)
      const plain = !(arg.jsEvent.ctrlKey || arg.jsEvent.metaKey || arg.jsEvent.shiftKey || arg.jsEvent.button === 1)
      if (item && plain) {
        arg.jsEvent.preventDefault() // a router navigation; Ctrl/middle-click still opens a new tab (it is a link)
        void navigate(hearingsPath(item))
      }
    },
    [byId, navigate],
  )
  const eventContent = useCallback(
    (arg: EventContentArg) => {
      const item = byId.get(arg.event.id)
      return item ? <AgendaEvent item={item} time={arg.timeText} list={arg.view.type === 'listWeek'} /> : null
    },
    [byId],
  )
  const eventDidMount = useCallback(
    (arg: EventMountArg) => {
      const item = byId.get(arg.event.id)
      if (!item) return
      // the link's whole name for keyboard and screen readers: the ⚠ tooltip itself is reached by the mouse only
      const label = [
        t.openCase(item.caseCode, formatDateTime(item.scheduledAt)),
        item.isOverdue ? t.overdue : null,
        item.conflict ? t.conflict(item.conflictWith.join(', ')) : null,
      ]
      arg.el.setAttribute('aria-label', label.filter((x) => x !== null).join('. '))
    },
    [byId],
  )

  const title = titleOf(view, date)

  return (
    <>
      {/* paper: A4 portrait; an open tooltip or menu is not printed */}
      <GlobalStyles
        styles={{
          '@page': { size: 'A4 portrait', margin: '15mm' },
          '@media print': { '.MuiPopper-root, .MuiTooltip-popper': { display: 'none !important' } },
        }}
      />
      <Box sx={{ displayPrint: 'none' }}>
        <PageHeader
          title={t.title}
          subtitle={t.subtitle}
          actions={
            <Button variant="outlined" startIcon={<Printer size={20} aria-hidden />} onClick={() => window.print()}>
              {t.print}
            </Button>
          }
        />

        <Stack spacing={2}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Stack spacing={2}>
              <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <IconButton aria-label={t.previous} onClick={() => set({ date: shift(view, date, -1) })}>
                  <ChevronLeft size={20} />
                </IconButton>
                <IconButton aria-label={t.next} onClick={() => set({ date: shift(view, date, 1) })}>
                  <ChevronRight size={20} />
                </IconButton>
                <Button variant="outlined" size="small" onClick={() => set({ date: '' })}>
                  {t.today}
                </Button>
                <Typography variant="h3" component="h2" aria-live="polite" sx={{ flex: 1, minWidth: 180, ml: 1 }}>
                  {title}
                </Typography>
                <ToggleButtonGroup
                  exclusive
                  size="small"
                  value={view}
                  aria-label={t.viewsLabel}
                  onChange={(_, next: AgendaView | null) => next && set({ view: next })}
                  sx={{ width: { xs: '100%', sm: 'auto' }, '& .MuiToggleButton-root': { flex: { xs: 1, sm: 'none' }, px: 2 } }}
                >
                  {AGENDA_VIEWS.map((v) => (
                    <ToggleButton key={v} value={v}>
                      {t.views[v]}
                    </ToggleButton>
                  ))}
                </ToggleButtonGroup>
              </Stack>

              <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ alignItems: { md: 'center' } }}>
                {canFilterOffice && (
                  <Box sx={{ width: { xs: '100%', md: 280 } }}>
                    <OptionPicker options={officeOptions} value={officeId} onChange={setOffice} label={t.office} size="small" />
                  </Box>
                )}
                {canFilterLawyer && (
                  <Box sx={{ width: { xs: '100%', md: 260 } }}>
                    <OptionPicker
                      options={lawyerOptions}
                      value={lawyerId}
                      onChange={(lawyer) => set({ lawyer })}
                      label={t.lawyer}
                      size="small"
                    />
                  </Box>
                )}
                <FormControlLabel
                  control={<Switch checked={includeCancelled} onChange={(e) => set({ cancelled: e.target.checked ? '1' : '' })} />}
                  label={t.showCancelled}
                />
              </Stack>
            </Stack>
          </Paper>

          {agenda.isError && <Alert severity="error">{problemMessage(agenda.error)}</Alert>}
          {agenda.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : items.length === 0 ? (
            <Paper variant="outlined">
              <EmptyState title={t.empty} />
            </Paper>
          ) : (
            <Paper variant="outlined" sx={{ p: { xs: 0, md: 2 }, overflow: 'hidden', ...calendarStyles }}>
              <FullCalendar
                ref={calendar}
                plugins={PLUGINS}
                locale={sqLocale}
                timeZone="UTC"
                now={tiraneNow}
                initialView={CALENDAR_VIEW[view]}
                initialDate={date}
                headerToolbar={false}
                height="auto"
                firstDay={1}
                allDaySlot={false}
                nowIndicator
                slotMinTime={slotMin}
                slotMaxTime={slotMax}
                slotLabelFormat={TIME_24H}
                eventTimeFormat={TIME_24H}
                dayMaxEvents={3}
                eventBackgroundColor="transparent"
                eventBorderColor="transparent"
                dayHeaderContent={dayHeader}
                listDayFormat={false}
                listDaySideFormat={false}
                events={events}
                eventClick={openHearing}
                eventContent={eventContent}
                eventDidMount={eventDidMount}
              />
            </Paper>
          )}
        </Stack>
      </Box>

      <PrintList title={title} items={items} />
    </>
  )
}

/** Column headers in Albanian from the bundled dayjs "sq" names (browsers' Intl often lacks Albanian). */
function dayHeader(arg: DayHeaderContentArg) {
  const day = dayjs(arg.date.toISOString().slice(0, 10)).locale('sq')
  const weekday = day.format('ddd')
  const name = weekday.charAt(0).toUpperCase() + weekday.slice(1)
  if (arg.view.type === 'dayGridMonth') return name
  if (arg.view.type === 'listWeek') return formatLongDate(new Date(`${arg.date.toISOString().slice(0, 10)}T12:00:00Z`))
  return `${name} ${day.format('D.M')}`
}

/** One hearing: time, case code, court, type; the colour by status, a red outline when overdue, ⚠ on a conflict. */
function AgendaEvent({ item, time, list }: { item: AgendaItem; time: string; list: boolean }) {
  const tone = tones[STATUS_TONE[item.status] ?? 'neutral']
  const cancelled = item.status === 'CANCELLED'
  return (
    <Box
      sx={{
        width: '100%',
        height: '100%',
        overflow: 'hidden',
        px: 0.75,
        py: 0.5,
        bgcolor: tone.bg,
        color: tone.fg,
        borderLeft: 3,
        borderLeftColor: tone.fg,
        borderRadius: `${tokens.radius.control}px`,
        outline: item.isOverdue ? 2 : 0,
        outlineColor: 'error.main',
        outlineOffset: -2,
        textDecoration: cancelled ? 'line-through' : 'none',
        lineHeight: 1.3,
        fontSize: '0.8125rem',
      }}
    >
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', minWidth: 0 }}>
        {!list && <Box component="span" sx={{ fontWeight: 600 }}>{time}</Box>}
        <Box component="span" sx={{ fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {item.caseCode}
        </Box>
        {item.conflict && (
          <Tooltip title={t.conflict(item.conflictWith.join(', '))}>
            <Box
              component="span"
              role="img"
              aria-label={t.conflict(item.conflictWith.join(', '))}
              sx={{ display: 'inline-flex', color: 'warning.dark', flexShrink: 0 }}
            >
              <TriangleAlert size={16} aria-hidden />
            </Box>
          </Tooltip>
        )}
      </Stack>
      <Box sx={{ color: 'text.secondary', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {item.courtName}
      </Box>
      <Box sx={{ color: 'text.secondary', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {typeOf(item)}
        {list && item.primaryLawyerName ? ` · ${item.primaryLawyerName}` : ''}
      </Box>
    </Box>
  )
}

/** Only on paper (A4 portrait): the range's hearings by day — date, time, court, case code, type, lawyer. */
function PrintList({ title, items }: { title: string; items: AgendaItem[] }) {
  const days = new Map<string, AgendaItem[]>()
  for (const item of items) {
    const day = formatDateTime(item.scheduledAt).slice(0, 10)
    days.set(day, [...(days.get(day) ?? []), item])
  }
  const c = t.columns
  return (
    <Box sx={{ display: 'none', displayPrint: 'block', color: 'common.black' }}>
      <Typography variant="h1" sx={{ fontSize: '1.4rem' }}>
        {t.title}
      </Typography>
      <Typography sx={{ mb: 2 }}>{title}</Typography>
      {items.length === 0 && <Typography>{t.empty}</Typography>}
      {[...days.entries()].map(([day, hearings]) => (
        <Box key={day} sx={{ mb: 2, breakInside: 'avoid' }}>
          <Typography variant="h3" component="h2" sx={{ fontSize: '1rem', mb: 0.5 }}>
            {formatLongDate(new Date(`${day.slice(6, 10)}-${day.slice(3, 5)}-${day.slice(0, 2)}T12:00:00Z`))}
          </Typography>
          <Table
            size="small"
            sx={{
              // the same column widths for every day's table
              tableLayout: 'fixed',
              '& td, & th': { px: 1, py: 0.5, borderColor: 'divider', fontSize: '0.8rem' },
              // time and case code never break ("2026-" / "000123/1")
              '& td:nth-of-type(1), & td:nth-of-type(3)': { whiteSpace: 'nowrap' },
            }}
          >
            <TableHead>
              <TableRow>
                <TableCell sx={{ width: '9%' }}>{c.time}</TableCell>
                <TableCell sx={{ width: '33%' }}>{c.court}</TableCell>
                <TableCell sx={{ width: '16%' }}>{c.caseCode}</TableCell>
                <TableCell sx={{ width: '23%' }}>{c.type}</TableCell>
                <TableCell sx={{ width: '19%' }}>{c.lawyer}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {hearings.map((i) => (
                <TableRow
                  key={i.hearingPublicId}
                  sx={i.status === 'CANCELLED' ? { '& td': { textDecoration: 'line-through' } } : undefined}
                >
                  <TableCell>{formatDateTime(i.scheduledAt).slice(11)}</TableCell>
                  <TableCell>{i.courtName}</TableCell>
                  <TableCell>{i.caseCode}</TableCell>
                  <TableCell>
                    {typeOf(i)}
                    {i.status === 'CANCELLED' ? ` (${t.cancelled})` : ''}
                  </TableCell>
                  <TableCell>{i.primaryLawyerName ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      ))}
    </Box>
  )
}

/** FullCalendar's own CSS, tuned to the theme: our event boxes, no default dot or fill, theme borders and fonts. */
const calendarStyles = {
  '& .fc': { fontFamily: tokens.font.family, fontSize: '0.875rem' },
  '& .fc-theme-standard td, & .fc-theme-standard th, & .fc-theme-standard .fc-scrollgrid': { borderColor: tokens.color.cardBorder },
  '& .fc-col-header-cell': { bgcolor: tokens.color.tableHead, py: 0.75, fontWeight: 600 },
  '& .fc-col-header-cell a, & .fc-daygrid-day-number': { color: tokens.color.textPrimary, textDecoration: 'none' },
  '& .fc-day-today': { bgcolor: `${tokens.color.tint.blue} !important` },
  '& .fc-timegrid-slot-label': { color: tokens.color.textSecondary },
  // half-hour rows tall enough for the three lines of a one-hour hearing (time + code, court, type)
  '& .fc-timegrid-slot': { height: '2.25rem' },
  '& .fc-event, & .fc-event:hover': { bgcolor: 'transparent', border: 0, boxShadow: 'none' },
  '& .fc-event:focus-visible': { outline: 2, outlineColor: tokens.color.primary, outlineOffset: 1 },
  '& .fc-daygrid-event': { whiteSpace: 'normal', mx: 0.5, mb: 0.5 },
  '& .fc-list-event-dot': { display: 'none' },
  '& .fc-list-event td': { borderColor: tokens.color.cardBorder },
  '& .fc-list-event:hover td': { bgcolor: tokens.color.pageBg },
  '& .fc-list-day-cushion': { bgcolor: tokens.color.tableHead, color: tokens.color.textPrimary },
  '& .fc-list-event-time': { color: tokens.color.textPrimary, fontWeight: 600, width: 64 },
  '& .fc-daygrid-more-link': { color: tokens.color.primary },
} as const
