import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import dayjs from 'dayjs'
import { useState } from 'react'
import { AppDatePicker } from '../../components/AppDatePicker'
import { RangeGroup } from '../../components/filters/RangeGroup'
import { OptionPicker } from '../../components/OptionPicker'
import { fetchAgenda, type AgendaItem, type AgendaParams } from '../../features/agenda/api'
import { sq } from '../../i18n/sq'
import { PRINT_PRESETS, presetRange, printRangeError } from '../../lib/agendaRange'
import { formatDate, formatDateTime, formatLongDate, todayInTirane } from '../../lib/dates'
import { isoDateOf } from '../../lib/pickerValues'
import { problemMessage } from '../../lib/problem'

const t = sq.agenda
const d = t.printDialog

interface Option {
  value: string
  label: string
}

/** What is printed: the period and its hearings. */
export interface AgendaPrintout {
  params: AgendaParams
  items: AgendaItem[]
}

const typeOf = (i: AgendaItem) => (i.otherTypeText ? `${i.hearingType.name}: ${i.otherTypeText}` : i.hearingType.name)

/**
 * Task 053n — "Printo": the period (dates, or Sot / Kjo javë / Java tjetër / Ky muaj; at most 3 months), the office and
 * the lawyer for who may filter by them (the page's current ones to start with) and "Përfshi të anuluarat". It loads
 * EVERY hearing of that period from the API — not what the calendar shows — then the browser prints them.
 */
export function AgendaPrintDialog({
  initial,
  canFilterOffice,
  canFilterLawyer,
  officeOptions,
  lawyerOptionsFor,
  onClose,
  onLoaded,
}: {
  initial: AgendaParams
  canFilterOffice: boolean
  canFilterLawyer: boolean
  officeOptions: Option[]
  lawyerOptionsFor: (officeId: string) => Option[]
  onClose: () => void
  onLoaded: (printout: AgendaPrintout) => void
}) {
  const [from, setFrom] = useState<string | null>(initial.from)
  const [to, setTo] = useState<string | null>(initial.to)
  const [officeId, setOfficeId] = useState(initial.officeId)
  const [lawyerId, setLawyerId] = useState(initial.lawyerId)
  const [includeCancelled, setIncludeCancelled] = useState(initial.includeCancelled)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const rangeError = printRangeError(from, to)
  const lawyerOptions = lawyerOptionsFor(officeId)

  const print = async () => {
    if (rangeError || !from || !to) return
    setBusy(true)
    setError(null)
    try {
      const params: AgendaParams = { from, to, officeId, lawyerId, includeCancelled }
      const items = await fetchAgenda(params)
      onLoaded({ params, items })
    } catch (e) {
      setError(problemMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="agenda-print-title">
      <DialogTitle id="agenda-print-title">{d.title}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }} role="group" aria-label={d.presetsLabel}>
            {PRINT_PRESETS.map((p) => (
              <Chip
                key={p}
                label={d.presets[p]}
                onClick={() => {
                  const range = presetRange(p, todayInTirane())
                  setFrom(range.from)
                  setTo(range.to)
                }}
              />
            ))}
          </Stack>
          {/* task 053o: one label on the border, "Nga" and "Deri" inside */}
          <RangeGroup label={d.period} width={{ xs: '100%' }}>
            <AppDatePicker label={sq.filters.from} value={from ? dayjs(from) : null} onChange={(v) => setFrom(isoDateOf(v))} fullWidth />
            <AppDatePicker
              label={sq.filters.to}
              value={to ? dayjs(to) : null}
              onChange={(v) => setTo(isoDateOf(v))}
              fullWidth
              error={rangeError === 'order' || rangeError === 'tooLong'}
              helperText={rangeError && rangeError !== 'missing' ? d.errors[rangeError] : undefined}
            />
          </RangeGroup>
          {canFilterOffice && (
            <OptionPicker
              filter
              options={officeOptions}
              value={officeId}
              onChange={(office) => {
                setOfficeId(office)
                if (!lawyerOptionsFor(office).some((o) => o.value === lawyerId)) setLawyerId('')
              }}
              label={t.office}
            />
          )}
          {canFilterLawyer && (
            <OptionPicker filter options={lawyerOptions} value={lawyerId} onChange={setLawyerId} label={t.lawyer} />
          )}
          <FormControlLabel
            control={<Switch checked={includeCancelled} onChange={(e) => setIncludeCancelled(e.target.checked)} />}
            label={d.includeCancelled}
          />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{d.cancel}</Button>
        <Button variant="contained" onClick={() => void print()} disabled={busy || rangeError !== null}>
          {busy ? d.loading : d.print}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

/**
 * Only on paper (A4 portrait): one title line (with the period) and the hearings by day — time, court, case code,
 * type, lawyer — nothing else (no filters, no "printed by", no page numbers). A day's table header repeats on a new page.
 */
export function AgendaPrintoutView({ printout }: { printout: AgendaPrintout }) {
  const c = t.columns
  const days = new Map<string, AgendaItem[]>()
  for (const item of printout.items) {
    const day = formatDateTime(item.scheduledAt).slice(0, 10)
    days.set(day, [...(days.get(day) ?? []), item])
  }

  return (
    <Box sx={{ display: 'none', displayPrint: 'block', color: 'common.black' }} data-testid="agenda-printout">
      <style>{'@page { size: A4 portrait; margin: 15mm; }'}</style>
      <Typography variant="h1" sx={{ fontSize: '1.4rem', mb: 2 }}>
        {t.printout.title(formatDate(printout.params.from), formatDate(printout.params.to))}
      </Typography>
      {printout.items.length === 0 && <Typography>{t.empty}</Typography>}
      {[...days.entries()].map(([day, hearings]) => (
        <Box key={day} sx={{ mb: 2 }}>
          <Typography variant="h3" component="h2" sx={{ fontSize: '1rem', mb: 0.5, breakAfter: 'avoid' }}>
            {formatLongDate(new Date(`${day.slice(6, 10)}-${day.slice(3, 5)}-${day.slice(0, 2)}T12:00:00Z`))}
          </Typography>
          <Table
            size="small"
            sx={{
              tableLayout: 'fixed',
              '& td, & th': { px: 1, py: 0.5, borderColor: 'divider', fontSize: '0.8rem' },
              '& td:nth-of-type(1), & td:nth-of-type(3)': { whiteSpace: 'nowrap' },
              '& tr': { breakInside: 'avoid' },
            }}
          >
            <TableHead sx={{ display: 'table-header-group' }}>
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
