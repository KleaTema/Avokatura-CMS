import { Alert, Box, TableCell, TableRow, TextField } from '@mui/material'
import { ApiError } from '../../api/client'
import { CourtPicker } from '../../components/CourtPicker'
import { OptionPicker } from '../../components/OptionPicker'
import { RangeFilter } from '../../components/RangeFilter'
import { AppSelect } from '../../components/ui/AppSelect'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { stickyCellSx } from '../../components/ui/tableStyles'
import { EmptyState } from '../../components/ui/EmptyState'
import { lawyerOptionsOf, useFilterOptions } from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import {
  searchOf,
  useActiveReport,
  useClosedReport,
  type ActiveFilters,
  type ClosedFilters,
  type ReportColumn,
} from '../../features/reports/api'
import { ExportButton } from '../../features/reports/ExportButton'
import { useCaseCategories, useCourtLevels, useCourts, useDecisionResults } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatDate } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'
import { tokens } from '../../theme/tokens'
import { reportCell, isNumeric } from './reportCells'
import { ReportFilterPanel, type FilterChip } from './ReportFilterPanel'
import { PANEL_FIELD, useReportParams } from './reportParams'

const t = sq.reports

/** Which filters the user gets — from their permissions (/me) and the server's filter options, never a role name. */
function useFilterRights() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
  // as the case list: the Monitor / Admin by office and lawyer, the Kryetar by the lawyers of the office
  const canFilterOffice = permissions.includes('case.view_all')
  const canFilterLawyer = permissions.includes('case.view_office') || canFilterOffice
  const options = useFilterOptions(canFilterLawyer)
  return { canFilterOffice, canFilterLawyer, options: options.data }
}

/** A failed report: the query-limit message for a 503, otherwise the server's. */
const failure = (error: unknown) => (error instanceof ApiError && error.status === 503 ? t.tooSlow : problemMessage(error))

function ReportTable<T extends Record<string, unknown>>({
  label,
  columns,
  rows,
  loading,
  empty,
  rowKey,
  currencyKey,
  shade,
  footer,
}: {
  label: string
  columns: ReportColumn[]
  rows: T[]
  loading: boolean
  empty: string | null
  rowKey: (row: T) => string
  currencyKey: string
  /** rows of one dossier share a background (closed report); undefined = none */
  shade?: (row: T, index: number) => boolean
  footer: React.ReactNode
}) {
  return (
    <DataTable
      label={label}
      loading={loading}
      minWidth={Math.max(900, columns.reduce((sum, c) => sum + Number(c.width) * 7, 0))}
      columns={columns.map((c, i) => ({
        key: c.key,
        label: c.header,
        align: isNumeric(c) ? ('right' as const) : undefined,
        sticky: i === 0,
      }))}
      empty={empty ? <EmptyState title={empty} /> : undefined}
      footer={footer}
    >
      {rows.map((row, index) => {
        const shaded = shade?.(row, index) ?? false
        const bg = shaded ? tokens.color.tint.blue : 'background.paper'
        return (
          <TableRow key={rowKey(row)} sx={{ bgcolor: shaded ? tokens.color.tint.blue : undefined }}>
            {columns.map((c, i) => (
              <TableCell
                key={c.key}
                align={isNumeric(c) ? 'right' : undefined}
                sx={{
                  ...(i === 0 ? { ...stickyCellSx, bgcolor: bg } : {}),
                  whiteSpace: Number(c.kind) === 1 ? 'normal' : 'nowrap',
                  minWidth: Number(c.width) * 6,
                  verticalAlign: 'top',
                }}
              >
                {reportCell(c, row, currencyKey)}
              </TableCell>
            ))}
          </TableRow>
        )
      })}
    </DataTable>
  )
}

// ---------------------------------------------------------------------------------------------------------- active

const ACTIVE_PARAMS = ['office', 'lawyer', 'court', 'category', 'level', 'priority', 'suspended', 'q'] as const

/** "Çështjet aktive" (Tabela 1, task 042): every filter in the URL, 10 rows a page, the export beside the filters. */
export function ActiveReport() {
  const { get, set, clear } = useReportParams(ACTIVE_PARAMS)
  const { canFilterOffice, canFilterLawyer, options } = useFilterRights()
  const courts = useCourts()
  const levels = useCourtLevels()
  const categories = useCaseCategories()
  const search = get('q')
  const q = useDebounced(serverQuery(search), 300)
  const filters: ActiveFilters = {
    officeId: canFilterOffice ? get('office') : '',
    lawyerId: canFilterLawyer ? get('lawyer') : '',
    courtId: get('court'),
    categoryCode: get('category'),
    courtLevelCode: get('level'),
    priority: get('priority'),
    suspended: get('suspended'),
    q,
  }
  const paging = useKeysetPaging()
  const report = useActiveReport(filters, { cursor: paging.cursor, size: paging.size })
  const rows = report.data?.items ?? []
  const lawyerOptions = lawyerOptionsOf(options?.lawyers ?? [], filters.officeId)
  const officeOptions = (options?.offices ?? []).map((o) => ({ value: o.publicId, label: o.name }))
  const courtName = courts.data?.find((c) => String(c.id) === filters.courtId)?.name

  const chips: FilterChip[] = []
  const chip = (key: string, label: string, value: string | undefined, clearWith: Partial<Record<(typeof ACTIVE_PARAMS)[number], string>>) => {
    if (value) chips.push({ key, label: `${label}: ${value}`, onDelete: () => set(clearWith) })
  }
  chip('q', t.searchChip, search.trim() || undefined, { q: '' })
  chip('office', t.office, officeOptions.find((o) => o.value === filters.officeId)?.label, { office: '', lawyer: '' })
  chip('lawyer', t.lawyer, lawyerOptions.find((o) => o.value === filters.lawyerId)?.label, { lawyer: '' })
  chip('court', t.court, filters.courtId ? (courtName ?? filters.courtId) : undefined, { court: '' })
  chip('level', t.courtLevel, levels.data?.find((l) => l.code === filters.courtLevelCode)?.name, { level: '' })
  chip('category', t.category, categories.data?.find((c) => c.code === filters.categoryCode)?.name, { category: '' })
  chip('priority', t.priority, filters.priority ? (sq.cases.priority[filters.priority] ?? filters.priority) : undefined, { priority: '' })
  chip('suspended', t.suspended, filters.suspended ? (filters.suspended === 'true' ? t.yes : t.no) : undefined, { suspended: '' })

  return (
    <>
      <ReportFilterPanel
        chips={chips}
        onClear={clear}
        action={
          <ExportButton
            url={`/api/reports/active-cases/export?${searchOf(filters)}`}
            rows={report.data ? Number(report.data.total) : undefined}
            fallbackName="raporti-ceshtjet-aktive.xlsx"
          />
        }
        main={
          <TextField
            size="small"
            label={t.search}
            value={search}
            onChange={(e) => set({ q: e.target.value })}
            slotProps={{ htmlInput: { maxLength: 100 } }}
            sx={{ flex: 1, minWidth: { xs: '100%', md: 260 } }}
          />
        }
        panel={
          <>
            {canFilterOffice && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker filter options={officeOptions} value={filters.officeId} onChange={(office) => set({ office, lawyer: '' })} label={t.office} size="small" />
              </Box>
            )}
            {canFilterLawyer && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker filter options={lawyerOptions} value={filters.lawyerId} onChange={(lawyer) => set({ lawyer })} label={t.lawyer} size="small" />
              </Box>
            )}
            <AppSelect
              filter
              size="small"
              label={t.courtLevel}
              value={filters.courtLevelCode}
              onChange={(level) => set({ level })}
              options={(levels.data ?? []).map((l) => ({ value: l.code, label: l.name }))}
              sx={PANEL_FIELD}
            />
            <Box sx={{ width: { xs: '100%', md: 300 } }}>
              <CourtPicker
                filter
                courts={courts.data ?? []}
                levels={levels.data ?? []}
                levelCode={filters.courtLevelCode}
                value={filters.courtId}
                onChange={(court) => set({ court: court ? String(court.id) : '' })}
                label={t.court}
                size="small"
              />
            </Box>
            <AppSelect
              filter
              size="small"
              label={t.category}
              value={filters.categoryCode}
              onChange={(category) => set({ category })}
              options={(categories.data ?? []).map((c) => ({ value: c.code, label: c.name }))}
              sx={PANEL_FIELD}
            />
            <AppSelect
              filter
              size="small"
              label={t.priority}
              value={filters.priority}
              onChange={(priority) => set({ priority })}
              options={Object.entries(sq.cases.priority).map(([code, name]) => ({ value: code, label: name }))}
              sx={{ width: { xs: '100%', md: 170 } }}
            />
            <AppSelect
              filter
              size="small"
              label={t.suspended}
              value={filters.suspended}
              onChange={(suspended) => set({ suspended })}
              options={[
                { value: 'true', label: t.yes },
                { value: 'false', label: t.no },
              ]}
              sx={{ width: { xs: '100%', md: 170 } }}
            />
          </>
        }
      />
      {report.isError && <Alert severity="error">{failure(report.error)}</Alert>}
      <ReportTable
        label={t.activeTable}
        columns={report.data?.columns ?? []}
        rows={rows as unknown as Record<string, unknown>[]}
        loading={report.isPending}
        empty={report.isSuccess && rows.length === 0 ? (chips.length ? t.empty : t.emptyNoFilters) : null}
        rowKey={(r) => String(r.casePublicId)}
        currencyKey="claimCurrency"
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={rows.length}
            total={Number(report.data?.total ?? 0)}
            nextCursor={report.data?.nextCursor}
            busy={report.isFetching}
            stale={report.isPlaceholderData}
          />
        }
      />
    </>
  )
}

// ---------------------------------------------------------------------------------------------------------- closed

const CLOSED_PARAMS = ['closedFrom', 'closedTo', 'result', 'outcome', 'court', 'lawyer', 'office'] as const

/**
 * "Çështjet e mbyllura" (Tabela 3, task 043): one row per closed level, the levels of a dossier together; the rows of
 * one dossier share a shaded block (alternating per dossier), so /1 and /2 read as one dossier.
 */
export function ClosedReport() {
  const { get, set, clear } = useReportParams(CLOSED_PARAMS)
  const { canFilterOffice, canFilterLawyer, options } = useFilterRights()
  const courts = useCourts()
  const levels = useCourtLevels()
  const results = useDecisionResults()
  const filters: ClosedFilters = {
    closedFrom: get('closedFrom'),
    closedTo: get('closedTo'),
    resultCode: get('result'),
    outcomeGroup: get('outcome'),
    courtId: get('court'),
    lawyerId: canFilterLawyer ? get('lawyer') : '',
    officeId: canFilterOffice ? get('office') : '',
  }
  const paging = useKeysetPaging()
  const report = useClosedReport(filters, { cursor: paging.cursor, size: paging.size })
  const rows = report.data?.items ?? []
  const lawyerOptions = lawyerOptionsOf(options?.lawyers ?? [], filters.officeId)
  const officeOptions = (options?.offices ?? []).map((o) => ({ value: o.publicId, label: o.name }))

  // alternate the shade per dossier (consecutive rows of one dossier keep it)
  const shadeOf = new Map<string, boolean>()
  let shaded = false
  let previous: string | null = null
  for (const row of rows) {
    if (row.dossierPublicId !== previous) {
      shaded = previous === null ? false : !shaded
      previous = row.dossierPublicId
    }
    shadeOf.set(row.casePublicId, shaded)
  }

  const chips: FilterChip[] = []
  if (filters.closedFrom || filters.closedTo) {
    chips.push({
      key: 'closed',
      label: `${t.closedRange}: ${formatDate(filters.closedFrom) || '…'} – ${formatDate(filters.closedTo) || '…'}`,
      onDelete: () => set({ closedFrom: '', closedTo: '' }),
    })
  }
  const resultName = results.data?.find((r) => r.code === filters.resultCode)?.name
  if (filters.resultCode) chips.push({ key: 'result', label: `${t.result}: ${resultName ?? filters.resultCode}`, onDelete: () => set({ result: '' }) })
  if (filters.outcomeGroup) {
    chips.push({ key: 'outcome', label: `${t.outcomeGroup}: ${t.outcomeGroups[filters.outcomeGroup] ?? filters.outcomeGroup}`, onDelete: () => set({ outcome: '' }) })
  }
  const courtName = courts.data?.find((c) => String(c.id) === filters.courtId)?.name
  if (filters.courtId) chips.push({ key: 'court', label: `${t.court}: ${courtName ?? filters.courtId}`, onDelete: () => set({ court: '' }) })
  const officeName = officeOptions.find((o) => o.value === filters.officeId)?.label
  if (officeName) chips.push({ key: 'office', label: `${t.office}: ${officeName}`, onDelete: () => set({ office: '', lawyer: '' }) })
  const lawyerName = lawyerOptions.find((o) => o.value === filters.lawyerId)?.label
  if (lawyerName) chips.push({ key: 'lawyer', label: `${t.lawyer}: ${lawyerName}`, onDelete: () => set({ lawyer: '' }) })

  return (
    <>
      <ReportFilterPanel
        chips={chips}
        onClear={clear}
        action={
          <ExportButton
            url={`/api/reports/closed-cases/export?${searchOf(filters)}`}
            rows={report.data ? Number(report.data.total) : undefined}
            fallbackName="raporti-ceshtjet-mbyllura.xlsx"
          />
        }
        main={
          <RangeFilter
            legend={t.closedRange}
            fromLabel={t.closedFrom}
            toLabel={t.closedTo}
            from={filters.closedFrom}
            to={filters.closedTo}
            onFrom={(closedFrom) => set({ closedFrom })}
            onTo={(closedTo) => set({ closedTo })}
          />
        }
        panel={
          <>
            <AppSelect
              filter
              size="small"
              label={t.result}
              value={filters.resultCode}
              onChange={(result) => set({ result })}
              options={(results.data ?? []).map((r) => ({ value: r.code, label: r.name }))}
              sx={PANEL_FIELD}
            />
            <AppSelect
              filter
              size="small"
              label={t.outcomeGroup}
              value={filters.outcomeGroup}
              onChange={(outcome) => set({ outcome })}
              options={Object.entries(t.outcomeGroups).map(([value, label]) => ({ value, label }))}
              sx={PANEL_FIELD}
            />
            <Box sx={{ width: { xs: '100%', md: 300 } }}>
              <CourtPicker
                filter
                courts={courts.data ?? []}
                levels={levels.data ?? []}
                levelCode=""
                value={filters.courtId}
                onChange={(court) => set({ court: court ? String(court.id) : '' })}
                label={t.court}
                size="small"
              />
            </Box>
            {canFilterOffice && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker filter options={officeOptions} value={filters.officeId} onChange={(office) => set({ office, lawyer: '' })} label={t.office} size="small" />
              </Box>
            )}
            {canFilterLawyer && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker filter options={lawyerOptions} value={filters.lawyerId} onChange={(lawyer) => set({ lawyer })} label={t.lawyer} size="small" />
              </Box>
            )}
          </>
        }
      />
      {report.isError && <Alert severity="error">{failure(report.error)}</Alert>}
      <ReportTable
        label={t.closedTable}
        columns={report.data?.columns ?? []}
        rows={rows as unknown as Record<string, unknown>[]}
        loading={report.isPending}
        empty={report.isSuccess && rows.length === 0 ? (chips.length ? t.empty : t.emptyNoFilters) : null}
        rowKey={(r) => String(r.casePublicId)}
        currencyKey="currency"
        shade={(r) => shadeOf.get(String(r.casePublicId)) ?? false}
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={rows.length}
            total={Number(report.data?.total ?? 0)}
            nextCursor={report.data?.nextCursor}
            busy={report.isFetching}
            stale={report.isPlaceholderData}
          />
        }
      />
    </>
  )
}
