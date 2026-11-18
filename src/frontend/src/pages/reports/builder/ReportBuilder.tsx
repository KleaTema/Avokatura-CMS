import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Menu,
  MenuItem,
  Stack,
  TableCell,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { Columns3, Download, Link2, Play, Save } from 'lucide-react'
import { useMemo, useState } from 'react'
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
  filtersParam,
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
import { problemMessage } from '../../../lib/problem'
import { tokens } from '../../../theme/tokens'
import { isNumeric, reportCell } from '../reportCells'
import { DrillDrawer, StatisticsBody, type OpenCell } from '../StatisticsReport'
import { ColumnChooser } from './ColumnChooser'

const t = sq.reports.builder
const PAGE_SIZE = 25

/** The applied state of the builder lives in the URL — a shareable link: ?f= filters, ?c= columns, ?run=1 generated. */
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
  return { applied, columns, generated: params.get('run') === '1', savedId: params.get('saved'), savedName: params.get('name'), update }
}

/**
 * The report builder of one table (task 053h, decisions F, P6/P9): the table's default filters on top and
 * "+ Shto filtër" for every other column; a child filter's options follow its parent's choice (shkalla → gjykata →
 * gjyqtari, zyra → avokati, ministria → institucioni, kategoria → lloji) and the user's scope. Nothing runs before
 * "Gjenero" — the only gold button. Then the results (Tabela 1/3: the chosen columns, pages, the totals row; Tabela
 * 5/6: the statistics with their drill-down), "Kolonat", the exports (xlsx / csv / pdf), "Ruaj raportin" and the
 * shareable link.
 */
export function ReportBuilder({ table }: { table: BuilderTable }) {
  const url = useBuilderUrl()
  const statistics = isStatisticsTable(table)
  const [draft, setDraft] = useState<FilterValues>(() =>
    statistics && !url.applied.year ? { ...url.applied, year: todayInTirane().slice(0, 4) } : url.applied,
  )
  const [added, setAdded] = useState<string[]>(() => Object.keys(url.applied))
  const [columnsOpen, setColumnsOpen] = useState(false)
  const [saveOpen, setSaveOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  const meta = useBuilderFilters(table, draft)
  const filters = meta.data?.filters ?? []
  const fixed = meta.data?.fixed ?? {}
  const effective: FilterValues = { ...draft, ...fixed }
  const appliedParam = url.generated ? filtersParam({ ...url.applied, ...fixed }) : null
  const changed = url.generated && filtersParam(effective) !== filtersParam({ ...url.applied, ...fixed })

  const allColumns = meta.data?.columns ?? []
  const chosen = url.columns.length > 0 ? url.columns.filter((k) => allColumns.some((c) => c.key === k)) : allColumns.map((c) => c.key)

  const unused = added.some((k) => !hasValue(effective[k])) || changed

  const set = (filter: FilterDef, value: FilterValue) =>
    setDraft((current) => {
      const next = { ...current, [filter.key]: value }
      // a parent's new choice clears its children (their options change)
      for (const child of descendants(filters, filter.key)) delete next[child]
      return next
    })

  const remove = (filter: FilterDef) => {
    setAdded((current) => current.filter((k) => k !== filter.key))
    setDraft((current) => {
      const next = { ...current }
      delete next[filter.key]
      for (const child of descendants(filters, filter.key)) delete next[child]
      return next
    })
  }

  const generate = () => url.update({ f: filtersParam(effective), run: '1' })

  const copyLink = async () => {
    const link = new URL(window.location.href)
    link.searchParams.set('f', filtersParam(effective))
    link.searchParams.set('run', '1')
    await navigator.clipboard.writeText(link.toString())
    setToast(t.linkCopied)
  }

  return (
    <Stack spacing={2}>
      {/* task 053m: the shared FilterBar over the draft; the leave dialog while a filter is added but empty, or the
          draft differs from the generated report */}
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
        <Button variant="contained" color="secondary" startIcon={<Play size={18} aria-hidden />} onClick={generate} disabled={meta.isPending}>
          {t.generate}
        </Button>
        <Button variant="outlined" startIcon={<Columns3 size={18} aria-hidden />} onClick={() => setColumnsOpen(true)} disabled={allColumns.length === 0}>
          {t.columns}
        </Button>
        <ExportMenu table={table} filters={appliedParam} columns={chosen} />
        <Button variant="outlined" startIcon={<Save size={18} aria-hidden />} onClick={() => setSaveOpen(true)}>
          {t.save}
        </Button>
        <Button startIcon={<Link2 size={18} aria-hidden />} onClick={() => void copyLink()}>
          {t.shareLink}
        </Button>
      </Stack>

      {meta.isError && <Alert severity="error">{problemMessage(meta.error)}</Alert>}
      {changed && <Alert severity="info">{t.changedSinceGenerate}</Alert>}
      {!url.generated ? (
        <EmptyState title={t.notGenerated} />
      ) : statistics ? (
        <StatisticsResults table={table as 5 | 6} filters={appliedParam!} values={{ ...url.applied, ...fixed }} chosen={chosen} />
      ) : (
        <CaseResults table={table as 1 | 3} filters={appliedParam!} chosen={chosen} />
      )}

      {columnsOpen && (
        <ColumnChooser
          columns={allColumns}
          chosen={chosen}
          onClose={() => setColumnsOpen(false)}
          onApply={(keys) => {
            url.update({ c: keys.length === allColumns.length && keys.every((k, i) => k === allColumns[i].key) ? null : keys.join(',') })
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

/** Tabela 1 / 3: the chosen columns, keyset pages, the totals row ("Gjithsej") over every row of the filters. */
function CaseResults({ table, filters, chosen }: { table: 1 | 3; filters: string; chosen: string[] }) {
  const [stack, setStack] = useState<string[]>([])
  const [forFilters, setForFilters] = useState(filters)
  const [totals, setTotals] = useState<ReportTotals | null>(null)
  // new filters start again at the first page
  if (forFilters !== filters) {
    setForFilters(filters)
    setStack([])
    setTotals(null)
  }

  const cursor = stack[stack.length - 1]
  const rows = useBuilderRows(table, filters, cursor, PAGE_SIZE)
  const page = rows.data?.page
  // the totals come with the first page only; keep them while paging
  if (rows.data?.totals && rows.data.totals !== totals && !rows.isPlaceholderData) setTotals(rows.data.totals)

  const columns: ReportColumn[] = chosen
    .map((key) => page?.columns.find((c) => c.key === key))
    .filter((c): c is ReportColumn => c !== undefined)
  const currencyKey = table === 1 ? 'claimCurrency' : 'currency'

  if (rows.isError) {
    return <Alert severity="error">{rows.error instanceof ApiError && rows.error.status === 503 ? sq.reports.tooSlow : problemMessage(rows.error)}</Alert>
  }

  const items = (page?.items ?? []) as unknown as Record<string, unknown>[]
  return (
    <DataTable
      label={sq.reports.builder.tabs[`tabela${table}`]}
      loading={rows.isPending}
      minWidth={Math.max(900, columns.reduce((sum, c) => sum + Number(c.width) * 7, 0))}
      columns={columns.map((c, i) => ({ key: c.key, label: c.header, align: isNumeric(c) ? ('right' as const) : undefined, sticky: i === 0 }))}
      empty={page && items.length === 0 ? <EmptyState title={t.zeroNote} /> : undefined}
      footer={
        <PaginationFooter
          mode="keyset"
          paging={{
            page: stack.length + 1,
            size: PAGE_SIZE,
            cursor,
            next: (next: string) => setStack((s) => [...s, next]),
            previous: () => setStack((s) => s.slice(0, -1)),
            setSize: () => undefined,
          }}
          count={items.length}
          total={Number(page?.total ?? 0)}
          nextCursor={page?.nextCursor}
          busy={rows.isFetching}
          stale={rows.isPlaceholderData}
        />
      }
    >
      {items.map((row) => (
        <TableRow key={String(row.casePublicId)}>
          {columns.map((c, i) => (
            <TableCell
              key={c.key}
              align={isNumeric(c) ? 'right' : undefined}
              sx={{ ...(i === 0 ? { ...stickyCellSx, bgcolor: 'background.paper' } : {}), whiteSpace: Number(c.kind) === 1 ? 'normal' : 'nowrap', verticalAlign: 'top' }}
            >
              {reportCell(c, row, currencyKey)}
            </TableCell>
          ))}
        </TableRow>
      ))}
      {totals && items.length > 0 && (
        <TableRow sx={{ bgcolor: tokens.color.tableHead }}>
          {columns.map((c, i) => {
            const sums = totals.sums.filter((s) => s.key === c.key)
            return (
              <TableCell
                key={c.key}
                align={isNumeric(c) ? 'right' : undefined}
                sx={{ fontWeight: 700, whiteSpace: 'nowrap', ...(i === 0 ? { ...stickyCellSx, bgcolor: tokens.color.tableHead } : {}) }}
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
}

/** Tabela 5 / 6: the statistics of the applied filters, the chosen measures only, each number opening its cases. */
function StatisticsResults({ table, filters, values, chosen }: { table: 5 | 6; filters: string; values: FilterValues; chosen: string[] }) {
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

  if (stats.isError) return <Alert severity="error">{problemMessage(stats.error)}</Alert>
  if (!stats.data) return <EmptyState title={sq.errors.loading} />
  const s = { ...stats.data, measures: stats.data.measures.filter((m) => chosen.includes(m.code)) }
  const total = stats.data.courts.find((c) => c.courtId === null || c.courtId === undefined)
  const zero = !total || Object.values(total.values).every((v) => Number(v) === 0)

  return (
    <Stack spacing={2}>
      {text('lawyer') && <Alert severity="info">{t.lawyerNote}</Alert>}
      {zero && <Alert severity="info">{t.zeroNote}</Alert>}
      <StatisticsBody s={s} year={Number(text('year'))} onOpen={setOpen} />
      <DrillDrawer filters={drillFilters} open={open} onClose={() => setOpen(null)} />
    </Stack>
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
