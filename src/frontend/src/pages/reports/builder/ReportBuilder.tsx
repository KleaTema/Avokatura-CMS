import {
  Alert,
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  LinearProgress,
  Menu,
  MenuItem,
  Stack,
  TableCell,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { Columns3, Download, Link2, Printer, Save } from 'lucide-react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError } from '../../../api/client'
import { FilterBar } from '../../../components/filters/FilterBar'
import { hasValue, type FilterDef } from '../../../components/filters/filterTypes'
import { UnusedFilterGuard } from '../../../components/filters/UnusedFilterGuard'
import { DataTable, PaginationFooter } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import { stickyCellSx } from '../../../components/ui/tableStyles'
import type { ReportColumn, StatisticsFilters } from '../../../features/reports/api'
import {
  cleanValues,
  descendants,
  exportUrl,
  fetchAllBuilderRows,
  filtersParam,
  MAX_PRINT_ROWS,
  isStatisticsTable,
  parseValues,
  useBuilderFilters,
  useBuilderRows,
  useBuilderStatistics,
  useSaveReport,
  type BuilderTable,
  type FilterValue,
  type FilterValues,
  type ReportTotals,
} from '../../../features/reports/builder'
import { sq } from '../../../i18n/sq'
import { todayInTirane } from '../../../lib/dates'
import { downloadFile } from '../../../lib/download'
import { formatDecimal, formatMoney } from '../../../lib/money'
import { formatCount } from '../../../lib/numbers'
import { useKeysetPaging } from '../../../lib/paging'
import { problemMessage } from '../../../lib/problem'
import { useDebounced } from '../../../lib/useDebounced'
import { usePrintWhenReady } from '../../../lib/usePrintWhenReady'
import { isNumeric, reportCell } from '../reportCells'
import { PrintPortal, PrintTitle } from '../ReportPrint'
import { DrillDrawer, StatisticsBody, type OpenCell } from '../StatisticsReport'
import { ColumnChooser } from './ColumnChooser'

const t = sq.reports.builder
/** Task 053o: the table follows the filters this long after the last change (and a newer change aborts the older request). */
export const FILTER_DEBOUNCE_MS = 400

/** The applied state of the builder lives in the URL — a shareable link: ?f= filters, ?c= columns. */
function useBuilderUrl() {
  const [params, setParams] = useSearchParams()
  const applied = useMemo(() => {
    try {
      return parseValues(JSON.parse(params.get('f') ?? '{}'))
    } catch {
      return {}
    }
  }, [params])
  const columns = (params.get('c') ?? '').split(',').filter(Boolean)
  const update = (changes: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [key, value] of Object.entries(changes)) {
          if (value === null || value === '') next.delete(key)
          else next.set(key, value)
        }
        return next
      },
      { replace: true },
    )
  return { applied, hasFilters: params.has('f'), columns, savedId: params.get('saved'), savedName: params.get('name'), update }
}

/**
 * The report builder of one table (tasks 053h, 053o): the table's default filters on top and "+ Shto filtër" for every
 * other column; a child filter's options follow its parent's choice and the user's scope.
 * <ul>
 * <li>On entering, every filter is at "Të gjitha" and NO table is shown ("Zgjidhni të paktën një filtër…").</li>
 * <li>As soon as the user changes any filter the table appears, then follows every further change — 400 ms after the
 * last one, a newer change aborting the request still running; the previous rows stay with a thin progress bar.</li>
 * <li>Opened from a saved report or a shared link (?f=), it shows the table at once.</li>
 * </ul>
 * Then "Kolonat" (the client's columns by default), the exports, "Printo" (only the table), "Ruaj raportin", the link.
 */
export function ReportBuilder({ table }: { table: BuilderTable }) {
  const url = useBuilderUrl()
  const statistics = isStatisticsTable(table)
  const [draft, setDraft] = useState<FilterValues>(() =>
    statistics && !url.applied.year ? { ...url.applied, year: todayInTirane().slice(0, 4) } : url.applied,
  )
  // a saved report / shared link starts shown; a fresh tab waits for the first change
  const [touched, setTouched] = useState(url.hasFilters)
  const [added, setAdded] = useState<string[]>(() => Object.keys(url.applied))
  const [columnsOpen, setColumnsOpen] = useState(false)
  const [saveOpen, setSaveOpen] = useState(false)
  const [printing, setPrinting] = useState<PrintChoice | null>(null)
  const [printBusy, setPrintBusy] = useState(false)
  const [printError, setPrintError] = useState<string | null>(null)
  const [printDialog, setPrintDialog] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  const meta = useBuilderFilters(table, draft)
  const filters = meta.data?.filters ?? []
  const fixed = meta.data?.fixed ?? {}
  const effective: FilterValues = { ...draft, ...fixed }
  const debounced = useDebounced(filtersParam(effective), FILTER_DEBOUNCE_MS)
  const appliedParam = touched ? debounced : null
  // the applied filters go to the URL (a link to share, Back keeps them)
  useEffect(() => {
    if (appliedParam !== null && appliedParam !== filtersParam(url.applied)) url.update({ f: appliedParam })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only when the applied filters change
  }, [appliedParam])

  const allColumns = meta.data?.columns ?? []
  const defaults = allColumns.filter((c) => c.defaultVisible !== false).map((c) => c.key)
  const chosen = url.columns.length > 0 ? url.columns.filter((k) => allColumns.some((c) => c.key === k)) : defaults

  const unused = added.some((k) => !hasValue(effective[k]))

  const set = (filter: FilterDef, value: FilterValue) => {
    setTouched(true)
    setDraft((current) => {
      const next = { ...current, [filter.key]: value }
      // a parent's new choice clears its children (their options change)
      for (const child of descendants(filters, filter.key)) delete next[child]
      return next
    })
  }

  const remove = (filter: FilterDef) => {
    setTouched(true)
    setAdded((current) => current.filter((k) => k !== filter.key))
    setDraft((current) => {
      const next = { ...current }
      delete next[filter.key]
      for (const child of descendants(filters, filter.key)) delete next[child]
      return next
    })
  }

  const copyLink = async () => {
    const link = new URL(window.location.href)
    link.searchParams.set('f', filtersParam(effective))
    await navigator.clipboard.writeText(link.toString())
    setToast(t.linkCopied)
  }

  // task 053o: Tabela 1 / 2 print EVERY row of the filters (loaded first), not the page on screen
  const print = async () => {
    if (statistics) {
      setPrintDialog(true)
      return
    }
    if (appliedParam === null) return
    setPrintBusy(true)
    setPrintError(null)
    try {
      const all = await fetchAllBuilderRows(table as 1 | 3, appliedParam)
      if (all.kind === 'tooMany') setPrintError(t.printTooMany(formatCount(MAX_PRINT_ROWS)))
      else setPrinting({ results: false, inHand: false, caseRows: { items: all.items, totals: all.totals } })
    } catch (e) {
      setPrintError(problemMessage(e))
    } finally {
      setPrintBusy(false)
    }
  }
  const clearPrinting = useCallback(() => setPrinting(null), [])
  usePrintWhenReady(printing, clearPrinting)
  const printTitle = t.tableNames[table]

  return (
    <Stack spacing={2}>
      {/* task 053m: the shared FilterBar; the leave dialog while a filter is added but still empty */}
      <FilterBar
        filters={filters}
        values={effective}
        added={added}
        onChange={set}
        onAdd={(key) => setAdded((current) => [...current.filter((k) => k !== key), key])}
        onRemove={remove}
      />
      <UnusedFilterGuard unused={unused} />

      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', displayPrint: 'none' }}>
        {!statistics && (
          <Button variant="outlined" startIcon={<Columns3 size={18} aria-hidden />} onClick={() => setColumnsOpen(true)} disabled={allColumns.length === 0}>
            {t.columns}
          </Button>
        )}
        <ExportMenu table={table} filters={appliedParam} columns={statistics ? [] : chosen} />
        <Button
          variant="outlined"
          startIcon={<Printer size={18} aria-hidden />}
          onClick={() => void print()}
          disabled={appliedParam === null || printBusy}
        >
          {printBusy ? t.printLoading : t.print}
        </Button>
        <Button variant="outlined" startIcon={<Save size={18} aria-hidden />} onClick={() => setSaveOpen(true)}>
          {t.save}
        </Button>
        <Button startIcon={<Link2 size={18} aria-hidden />} onClick={() => void copyLink()}>
          {t.shareLink}
        </Button>
      </Stack>

      {printError && (
        <Alert severity="warning" onClose={() => setPrintError(null)}>
          {printError}
        </Alert>
      )}
      {meta.isError && <Alert severity="error">{problemMessage(meta.error)}</Alert>}
      {appliedParam === null ? (
        <EmptyState title={t.notGenerated} />
      ) : statistics ? (
        <StatisticsResults
          table={table as 5 | 6}
          filters={appliedParam}
          values={effective}
          printing={printing}
          printTitle={printTitle}
        />
      ) : (
        <CaseResults table={table as 1 | 3} filters={appliedParam} chosen={chosen} printRows={printing?.caseRows ?? null} printTitle={printTitle} />
      )}

      {printDialog && (
        <StatisticsPrintDialog
          onClose={() => setPrintDialog(false)}
          onPrint={(choice) => {
            setPrintDialog(false)
            setPrinting(choice)
          }}
        />
      )}
      {columnsOpen && (
        <ColumnChooser
          columns={allColumns}
          chosen={chosen}
          onClose={() => setColumnsOpen(false)}
          onApply={(keys) => {
            url.update({ c: keys.length === defaults.length && keys.every((k, i) => k === defaults[i]) ? null : keys.join(',') })
            setColumnsOpen(false)
          }}
        />
      )}
      {saveOpen && (
        <SaveDialog
          table={table}
          values={effective}
          columns={url.columns}
          savedId={url.savedId}
          savedName={url.savedName}
          onClose={() => setSaveOpen(false)}
          onSaved={(id, name) => {
            url.update({ saved: id, name })
            setSaveOpen(false)
            setToast(t.saved(name))
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

/** Every row of Tabela 1 / 2 under the filters, loaded for the printout. */
interface CasePrintRows {
  items: Record<string, unknown>[]
  totals: ReportTotals | null
}

/** What "Printo" prints: the statistics' sections chosen in the print dialog; for Tabela 1 / 2 all of its rows. */
export interface PrintChoice {
  results: boolean
  inHand: boolean
  caseRows?: CasePrintRows
}

/**
 * Tabela 1 / 2 (API 1 / 3): the chosen columns (the client's by default; a group's header merged over its columns),
 * keyset pages through the shared footer (task 053o: size 10 / 25 / 50 in the URL, back to the first page on a new size
 * or new filters; Para / Pas move the cursor), the totals row ("Gjithsej") over every row of the filters. While the
 * next rows load the previous ones stay, with a thin progress bar; an error shows above them.
 */
function CaseResults({
  table,
  filters,
  chosen,
  printRows,
  printTitle,
}: {
  table: 1 | 3
  filters: string
  chosen: string[]
  printRows: CasePrintRows | null
  printTitle: string
}) {
  const paging = useKeysetPaging()
  const [totals, setTotals] = useState<ReportTotals | null>(null)
  const [forFilters, setForFilters] = useState(filters)
  // new filters: the totals of the old ones go (the page goes back to 1 by itself: the URL's filters changed)
  if (forFilters !== filters) {
    setForFilters(filters)
    setTotals(null)
  }

  const rows = useBuilderRows(table, filters, paging.cursor, paging.size)
  const page = rows.data?.page
  // the totals come with the first page only; keep them while paging
  if (rows.data?.totals && rows.data.totals !== totals && !rows.isPlaceholderData) setTotals(rows.data.totals)

  const columns: ReportColumn[] = chosen
    .map((key) => page?.columns.find((c) => c.key === key))
    .filter((c): c is ReportColumn => c !== undefined)
  const items = (page?.items ?? []) as unknown as Record<string, unknown>[]
  const error = rows.isError
    ? rows.error instanceof ApiError && rows.error.status === 503
      ? sq.reports.tooSlow
      : problemMessage(rows.error)
    : null

  // on screen: the current page; on paper: every row of the filters (loaded by "Printo")
  const tableOf = (print: boolean, items: Record<string, unknown>[], totals: ReportTotals | null) => (
    <DataTable
      label={t.tableNames[table]}
      loading={rows.isPending}
      minWidth={print ? undefined : Math.max(900, columns.reduce((sum, c) => sum + Number(c.width) * 7, 0))}
      columns={columns.map((c, i) => ({
        key: c.key,
        label: c.header,
        align: isNumeric(c) ? ('right' as const) : undefined,
        sticky: i === 0 && !print,
        group: c.group ?? undefined,
      }))}
      empty={page && items.length === 0 ? <EmptyState title={t.zeroNote} /> : undefined}
      footer={
        print ? undefined : (
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={items.length}
            total={Number(page?.total ?? 0)}
            nextCursor={page?.nextCursor}
            busy={rows.isFetching}
            stale={rows.isPlaceholderData}
          />
        )
      }
    >
      {items.map((row) => (
        <TableRow key={String(row.casePublicId)}>
          {columns.map((c, i) => (
            <TableCell
              key={c.key}
              align={isNumeric(c) ? 'right' : undefined}
              sx={{ ...(i === 0 && !print ? { ...stickyCellSx, bgcolor: 'inherit' } : {}), whiteSpace: Number(c.kind) === 1 ? 'normal' : 'nowrap', verticalAlign: 'top' }}
            >
              {reportCell(c, row, c.currencyKey ?? undefined)}
            </TableCell>
          ))}
        </TableRow>
      ))}
      {totals && items.length > 0 && (
        // the totals row: bold, with a strong top line (the same in print)
        <TableRow sx={{ '& td': { borderTop: 2, borderTopColor: 'text.primary', fontWeight: 700 } }}>
          {columns.map((c, i) => {
            const sums = totals.sums.filter((s) => s.key === c.key)
            return (
              <TableCell
                key={c.key}
                align={isNumeric(c) ? 'right' : undefined}
                sx={{ whiteSpace: 'nowrap', ...(i === 0 && !print ? { ...stickyCellSx, bgcolor: 'inherit' } : {}) }}
              >
                {i === 0
                  ? t.totalRows(formatCount(Number(totals.rows)))
                  : sums.map((s) => (
                      <Box key={s.currency ?? 'm2'}>
                        {s.currency ? formatMoney(Number(s.value).toFixed(2), s.currency) : `${formatDecimal(Number(s.value).toFixed(2))} m²`}
                      </Box>
                    ))}
              </TableCell>
            )
          })}
        </TableRow>
      )}
    </DataTable>
  )

  return (
    <Stack spacing={1}>
      {error && <Alert severity="error">{error}</Alert>}
      {/* the previous rows stay while the next ones load: a thin bar, no flicker */}
      <Box sx={{ height: 4 }}>{rows.isFetching && !rows.isPending && <LinearProgress aria-label={t.updating} />}</Box>
      {tableOf(false, items, totals)}
      {printRows && (
        <PrintPortal landscape={columns.length > 6}>
          <PrintTitle title={printTitle} />
          {tableOf(true, printRows.items, printRows.totals)}
        </PrintPortal>
      )}
    </Stack>
  )
}

/** Tabela 3 / 4 (API 5 / 6): the client's table of the applied filters, each number opening its cases. */
function StatisticsResults({
  table,
  filters,
  values,
  printing,
  printTitle,
}: {
  table: 5 | 6
  filters: string
  values: FilterValues
  printing: PrintChoice | null
  printTitle: string
}) {
  const stats = useBuilderStatistics(table, filters)
  const [open, setOpen] = useState<OpenCell | null>(null)
  const text = (key: string) => (typeof values[key] === 'string' ? (values[key] as string) : '')
  const drillFilters: StatisticsFilters = {
    year: text('year'),
    table: String(table),
    officeId: text('office'),
    lawyerId: text('lawyer'),
    institutionId: text('institution'),
    ministryId: text('institution') ? '' : text('ministry'),
    courtLevelCode: text('courtLevel'),
    filterCourtId: text('court'),
  }

  const error = stats.isError ? problemMessage(stats.error) : null
  if (!stats.data) return error ? <Alert severity="error">{error}</Alert> : <LinearProgress aria-label={sq.errors.loading} />
  const s = stats.data
  const total = s.courts.find((c) => c.courtId === null || c.courtId === undefined)
  const zero = !total || Object.values(total.values).every((v) => Number(v) === 0)

  return (
    <Stack spacing={2}>
      {error && <Alert severity="error">{error}</Alert>}
      <Box sx={{ height: 4 }}>{stats.isFetching && <LinearProgress aria-label={t.updating} />}</Box>
      {text('lawyer') && <Alert severity="info">{t.lawyerNote}</Alert>}
      {zero && <Alert severity="info">{t.zeroNote}</Alert>}
      <StatisticsBody s={s} year={Number(text('year'))} onOpen={setOpen} />
      <DrillDrawer filters={drillFilters} open={open} onClose={() => setOpen(null)} />
      {printing && (
        <PrintPortal landscape>
          <PrintTitle title={printTitle} />
          <StatisticsBody s={s} year={Number(text('year'))} onOpen={() => undefined} print sections={printing} />
        </PrintPortal>
      )}
    </Stack>
  )
}

/** "Printo" of the statistics: the table always; "Rezultati" and "Në dorë" only when chosen. */
function StatisticsPrintDialog({ onClose, onPrint }: { onClose: () => void; onPrint: (choice: PrintChoice) => void }) {
  const [choice, setChoice] = useState<PrintChoice>({ results: false, inHand: false })
  return (
    <Dialog open onClose={onClose} aria-labelledby="stats-print-title">
      <DialogTitle id="stats-print-title">{t.printTitle}</DialogTitle>
      <DialogContent>
        <Stack>
          <FormControlLabel control={<Checkbox checked disabled />} label={t.printTable} />
          <FormControlLabel
            control={<Checkbox checked={choice.results} onChange={(e) => setChoice({ ...choice, results: e.target.checked })} />}
            label={t.printResults}
          />
          <FormControlLabel
            control={<Checkbox checked={choice.inHand} onChange={(e) => setChoice({ ...choice, inHand: e.target.checked })} />}
            label={t.printInHand}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t.printCancel}</Button>
        <Button variant="contained" onClick={() => onPrint(choice)}>
          {t.print}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

/** "Eksporto" → xlsx / csv / pdf of the applied filters and the chosen columns (audited by the server). */
function ExportMenu({ table, filters, columns }: { table: BuilderTable; filters: string | null; columns: string[] }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const run = async (format: 'xlsx' | 'csv' | 'pdf') => {
    setAnchor(null)
    if (filters === null) return
    setBusy(true)
    setError(null)
    try {
      const result = await downloadFile(exportUrl(table, format, filters, columns), `raporti.${format}`, () => undefined, new AbortController().signal)
      setDone(sq.reports.exported(result.fileName))
    } catch (e) {
      setError(e instanceof ApiError ? problemMessage(e) : sq.reports.exportFailed)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Button variant="outlined" startIcon={<Download size={18} aria-hidden />} disabled={filters === null || busy} onClick={(e) => setAnchor(e.currentTarget)}>
        {t.exportAs}
      </Button>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {(['xlsx', 'csv', 'pdf'] as const).map((format) => (
          <MenuItem key={format} onClick={() => void run(format)}>
            {t.formats[format]}
          </MenuItem>
        ))}
      </Menu>
      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ width: '100%' }}>
          {error}
        </Alert>
      )}
      <SuccessToast message={done} onClose={() => setDone(null)} />
    </>
  )
}

/** "Ruaj raportin": a name; opened from a saved report, it may overwrite that one or save a new one. */
function SaveDialog({
  table,
  values,
  columns,
  savedId,
  savedName,
  onClose,
  onSaved,
}: {
  table: BuilderTable
  values: FilterValues
  columns: string[]
  savedId: string | null
  savedName: string | null
  onClose: () => void
  onSaved: (id: string, name: string) => void
}) {
  const [name, setName] = useState(savedName ?? '')
  const save = useSaveReport()
  const submit = async (id?: string) => {
    try {
      const saved = await save.mutateAsync({ id, name: name.trim(), table, filters: values, columns })
      onSaved(saved.publicId, saved.name)
    } catch {
      // the message shows below
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="save-title">
      <DialogTitle id="save-title">{t.saveTitle}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField autoFocus required label={t.saveName} value={name} onChange={(e) => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 120 } }} />
          {save.isError && <Alert severity="error">{problemMessage(save.error)}</Alert>}
          <Typography variant="body2" color="text.secondary">
            {t.filtersSummary(Object.keys(cleanValues(values)).length)}
          </Typography>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        {savedId && savedName && (
          <Button disabled={!name.trim() || save.isPending} onClick={() => void submit(savedId)}>
            {t.saveOverwrite(savedName)}
          </Button>
        )}
        <Button variant="contained" disabled={!name.trim() || save.isPending} onClick={() => void submit()}>
          {savedId ? t.saveAsNew : t.save}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
