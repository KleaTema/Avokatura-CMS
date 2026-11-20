import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Link as MuiLink,
  Stack,
  TableCell,
  TableRow,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { FilterBar } from '../../components/filters/FilterBar'
import { descendantsOf, hasValue, type FilterDef, type FilterValue, type FilterValues } from '../../components/filters/filterTypes'
import { UnusedFilterGuard } from '../../components/filters/UnusedFilterGuard'
import { ADDED_PARAM } from '../../components/filters/useUrlFilters'
import { JudgePicker, type PickedJudge } from '../../components/JudgePicker'
import { OptionPicker } from '../../components/OptionPicker'
import { AppSelect } from '../../components/ui/AppSelect'
import { CaseCode } from '../../components/ui/CaseCode'
import { DataTable, PaginationFooter, type DataColumn } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { SearchField } from '../../components/ui/SearchField'
import { StatusChip } from '../../components/ui/StatusChip'
import { stickyCellSx } from '../../components/ui/tableStyles'
import { NotifyNotClosedDialog } from '../../components/registration/NotifyNotClosedDialog'
import { FormSection, RegistrationForm } from '../../components/registration/RegistrationForm'
import {
  REGISTRATION_STATUSES,
  useMoreFilters,
  useNextLevelPreview,
  useNotifyRecipients,
  useRegisterNextLevel,
  useRegistrationFilters,
  useRegistrationSearch,
  type MoreFilter,
  type NextLevelPreview,
  type RegistrationFilter,
  type RegistrationFilterName,
  type RegistrationFilterValues,
  type RegistrationSearchItem,
  type RegistrationStatus,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import {
  useCaseCategories,
  useCaseSubcategories,
  useCourtLevels,
  useCourts,
  useDecisionResults,
} from '../../features/reference/api'
import { cleanValues, filtersParam, parseValues } from '../../features/reports/builder'
import { sq } from '../../i18n/sq'
import { formatTiraneDate } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { problemMessage } from '../../lib/problem'
import { useDebounced } from '../../lib/useDebounced'
import { LIVE_SEARCH_DEBOUNCE_MS, serverSearchText } from '../../lib/useLiveSearch'

const t = sq.cases.continue

/**
 * Protokoll continues a closed case at the next level (tasks 021, 053j): a table of EVERY dossier of the country (the
 * Protokoll is a central role), then the summons form for X/n+1 (?case=<publicId>). Filters and paging live in the URL
 * (?actNumber=… opens it from the duplicate dialog of a new registration).
 */
export function ContinueCasePage() {
  const [params, setParams] = useSearchParams()
  const selected = params.get('case')
  const select = (publicId: string | null) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (publicId) next.set('case', publicId)
      else next.delete('case')
      return next
    })

  return selected ? <NextLevel previousCasePublicId={selected} onBack={() => select(null)} /> : <Search onContinue={select} />
}

// ---------------------------------------------------------------- the table

/** URL parameters that only label a chosen value (not sent to the API). */
const JUDGE_NAME = 'judgeName'
/** The "+ Shto filtër" values in the URL: the report builder's JSON object. */
const MORE = 'more'
const isStatus = (value: string): value is RegistrationStatus => REGISTRATION_STATUSES.some((s) => s === value)

const COLUMNS: DataColumn[] = [
  { key: 'code', label: t.columns.code, sticky: true },
  { key: 'level', label: t.columns.level },
  { key: 'court', label: t.columns.court },
  { key: 'judge', label: t.columns.judge },
  { key: 'actNumber', label: t.columns.actNumber },
  { key: 'plaintiffs', label: t.columns.plaintiffs },
  { key: 'defendants', label: t.columns.defendants },
  { key: 'object', label: t.columns.object, width: 280 },
  { key: 'category', label: t.columns.category },
  { key: 'result', label: t.columns.result },
  { key: 'decisionDate', label: t.columns.decisionDate },
  { key: 'office', label: t.columns.office },
  { key: 'status', label: t.columns.status },
  { key: 'action', label: t.columns.action },
]

/** A "+ Shto filtër" filter as the shared FilterBar draws it (never default, required or locked here). */
const asReportFilter = (f: MoreFilter): FilterDef => ({
  key: f.key,
  label: f.label,
  kind: f.kind,
  parent: f.parent,
  isDefault: false,
  required: false,
  locked: false,
  options: f.options,
})

function Search({ onContinue }: { onContinue: (publicId: string) => void }) {
  const me = useMe(true)
  const canCreate = me.data?.permissions.includes('case.create') ?? false
  const [params, setParams] = useSearchParams()
  const filters = useRegistrationFilters()
  const fields = filters.data ?? []
  const paging = useKeysetPaging()

  const set = (changes: Record<string, string>) => {
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value)
          else next.delete(key)
        }
        // other filters: back to the first page
        next.delete('page')
        return next
      },
      { replace: true },
    )
  }

  // every filter lives in the URL under its own name
  const raw: RegistrationFilterValues = Object.fromEntries(fields.map((f) => [f.name, params.get(f.name) ?? '']))
  // a filter's value goes, and so do the values that depend on it (shkalla → gjykata → gjyqtari, kategoria → lloji)
  const change = (name: string, value: string) => {
    const changes: Record<string, string> = { [name]: value }
    const clear = (parent: string) => {
      for (const f of fields) {
        if (f.dependsOn !== parent) continue
        changes[f.name] = ''
        if (f.input === 'Judge') changes[JUDGE_NAME] = ''
        clear(f.name)
      }
    }
    clear(name)
    set(changes)
  }

  const statusParam = params.get('status') ?? ''
  const status: RegistrationStatus = isStatus(statusParam) ? statusParam : 'CONTINUABLE'

  // "+ Shto filtër" (task 053m: the shared FilterBar): the report builder's registry, the values in ?more=, the added
  // ones in ?af= (shown while empty, kept by Back; the leave dialog asks before an unused one is lost)
  const moreValues = parseValues(safeJson(params.get(MORE)))
  const moreParam = Object.keys(cleanValues(moreValues)).length > 0 ? filtersParam(moreValues) : ''
  const more = useMoreFilters(moreParam)
  const moreFilters = (more.data ?? []).map(asReportFilter)
  const added = (params.get(ADDED_PARAM) ?? '').split(',').filter((k) => k !== '')
  const unused = added.some((k) => !hasValue(moreValues[k]))
  const setMore = (next: FilterValues, nextAdded: string[] = added) =>
    set({
      [MORE]: Object.keys(cleanValues(next)).length > 0 ? filtersParam(next) : '',
      [ADDED_PARAM]: nextAdded.join(','),
    })
  const changeMore = (filter: FilterDef, value: FilterValue) => {
    const next = { ...moreValues, [filter.key]: value }
    for (const child of descendantsOf(moreFilters, filter.key)) delete next[child]
    setMore(next)
  }
  const removeMore = (filter: FilterDef) => {
    const next = { ...moreValues }
    delete next[filter.key]
    for (const child of descendantsOf(moreFilters, filter.key)) delete next[child]
    setMore(next, added.filter((k) => k !== filter.key))
  }

  const clearAll = () =>
    set(Object.fromEntries([...fields.map((f) => [f.name, '']), [JUDGE_NAME, ''], [MORE, ''], [ADDED_PARAM, ''], ['status', '']]))

  // live search: texts from 2 characters (serverSearchText), 300 ms after the last keystroke, '' at once when cleared;
  // chosen values at once (debounced as a string: `raw` is new each render)
  const typed = JSON.parse(useDebounced(JSON.stringify(raw), LIVE_SEARCH_DEBOUNCE_MS)) as RegistrationFilterValues
  const values: RegistrationFilterValues = Object.fromEntries(
    fields.map((f) => {
      const key = f.name as RegistrationFilterName
      const now = serverSearchText(raw[key] ?? '')
      return [f.name, f.input === 'Text' ? (now === '' ? '' : serverSearchText(typed[key] ?? '')) : (raw[key] ?? '')]
    }),
  )
  const filtered = Object.values(values).some((v) => v) || moreParam !== '' || status !== 'CONTINUABLE'
  const results = useRegistrationSearch(fields, values, status, moreParam, { cursor: paging.cursor, size: paging.size })
  const [notifyFor, setNotifyFor] = useState<RegistrationSearchItem | null>(null)
  const items = results.data?.items ?? []

  return (
    <Stack spacing={3}>
      <PageHeader title={t.title} />
      {filters.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {filters.isError && <Alert severity="error">{problemMessage(filters.error)}</Alert>}

      {/* the client's filters on top, then the status, then "+ Shto filtër" */}
      <Box
        sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: 'repeat(4, 1fr)' } }}
        role="search"
      >
        {fields.map((f, i) => (
          <FilterField
            key={f.name}
            filter={f}
            value={raw[f.name as RegistrationFilterName] ?? ''}
            parentValue={f.dependsOn ? (raw[f.dependsOn as RegistrationFilterName] ?? '') : ''}
            judgeName={params.get(JUDGE_NAME) ?? ''}
            onChange={(value) => change(f.name, value)}
            onJudge={(judge) => set({ judgeId: judge?.id ?? '', [JUDGE_NAME]: judge?.name ?? '' })}
            autoFocus={i === 0}
            busy={results.isFetching && (raw[f.name as RegistrationFilterName] ?? '') !== ''}
          />
        ))}
        <AppSelect
          label={t.status}
          value={status}
          onChange={(value) => set({ status: value === 'CONTINUABLE' ? '' : value })}
          options={REGISTRATION_STATUSES.map((value) => ({ value, label: t.statuses[value] ?? value }))}
        />
      </Box>

      <FilterBar
        filters={moreFilters}
        values={moreValues}
        added={added}
        onChange={changeMore}
        onAdd={(key) => setMore(moreValues, [...added.filter((k) => k !== key), key])}
        onRemove={removeMore}
        after={
          filtered && (
            <Button onClick={clearAll} sx={{ mt: 0.25 }}>
              {t.clearFilters}
            </Button>
          )
        }
      />
      <UnusedFilterGuard unused={unused} />
      {results.isError && <Alert severity="error">{problemMessage(results.error)}</Alert>}

      <DataTable
        label={t.tableLabel}
        columns={COLUMNS}
        loading={results.isPending && fields.length > 0}
        minWidth={1600}
        empty={
          results.isSuccess && items.length === 0 ? (
            <EmptyState
              title={t.noResults}
              action={
                canCreate ? (
                  <MuiLink component={Link} to="/cases/register">
                    {t.registerNew}
                  </MuiLink>
                ) : undefined
              }
            />
          ) : undefined
        }
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={items.length}
            total={Number(results.data?.total ?? 0)}
            nextCursor={results.data?.nextCursor}
            busy={results.isFetching}
            stale={results.isPlaceholderData}
          />
        }
      >
        {items.map((item) => (
          <ResultRow
            key={item.casePublicId}
            item={item}
            onContinue={() => onContinue(item.casePublicId)}
            onNotify={() => setNotifyFor(item)}
          />
        ))}
      </DataTable>

      {notifyFor && (
        <NotifyNotClosedDialog
          previousCasePublicId={notifyFor.casePublicId}
          caseCode={notifyFor.caseCode}
          onClose={() => setNotifyFor(null)}
        />
      )}
    </Stack>
  )
}

/** The URL's ?more= as JSON (anything unreadable is no filter). */
function safeJson(text: string | null): unknown {
  if (!text) return null
  try {
    return JSON.parse(text) as unknown
  } catch {
    return null
  }
}

/**
 * One search field, by the control the server names for it (filter.input). A new filter of an existing input needs
 * only its label in sq.cases.continue.filters; a new input needs a case here (docs/architecture.md).
 */
function FilterField({
  filter,
  value,
  parentValue,
  judgeName,
  onChange,
  onJudge,
  autoFocus,
  busy,
}: {
  filter: RegistrationFilter
  value: string
  /** the value of filter.dependsOn (the level of a court, the court of a judge, the category of a subcategory) */
  parentValue: string
  judgeName: string
  onChange: (value: string) => void
  onJudge: (judge: PickedJudge | null) => void
  autoFocus: boolean
  /** a text field: the results are loading */
  busy: boolean
}) {
  const label = t.filters[filter.name] ?? filter.name
  const levels = useCourtLevels()
  const courts = useCourts()
  const categories = useCaseCategories()
  const subcategories = useCaseSubcategories()
  const results = useDecisionResults()
  const courtName = (id: string | null) => courts.data?.find((c) => String(c.id) === id)?.name

  switch (filter.input) {
    case 'CourtLevel':
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          options={(levels.data ?? []).map((l) => ({ value: l.code, label: l.name }))}
        />
      )
    case 'Court':
      return (
        <OptionPicker
          filter
          options={(courts.data ?? [])
            .filter((c) => !parentValue || c.courtLevelCode === parentValue)
            .map((c) => ({ value: String(c.id), label: c.name }))}
          value={value}
          onChange={onChange}
          label={label}
        />
      )
    case 'Judge':
      return (
        <JudgePicker
          filter
          courtId={parentValue ? Number(parentValue) : null}
          showAll={!parentValue}
          courtName={courtName}
          value={value ? { id: value, name: judgeName, courtId: parentValue || null } : null}
          onChange={onJudge}
          label={label}
        />
      )
    case 'Category':
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          options={(categories.data ?? []).map((c) => ({ value: c.code, label: c.name }))}
        />
      )
    case 'Subcategory': {
      const own = (subcategories.data ?? []).filter((s) => s.categoryCode === parentValue)
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          disabled={own.length === 0}
          options={own.map((s) => ({ value: s.code, label: s.name }))}
        />
      )
    }
    case 'DecisionResult':
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          options={(results.data ?? []).map((r) => ({ value: r.code, label: r.name }))}
        />
      )
    default:
      // live search: the shared SearchField (X clears, a spinner while the rows load)
      return <SearchField size="medium" label={label} value={value} onChange={onChange} autoFocus={autoFocus} busy={busy} />
  }
}

/** The server sends at most five names per side. */
const names = (list: string[]) => list.join(', ')

function ResultRow({
  item,
  onContinue,
  onNotify,
}: {
  item: RegistrationSearchItem
  onContinue: () => void
  onNotify: () => void
}) {
  // "E pambyllur në sistem": the level is still open — the lawyer has to record the decision first
  const notClosed = !item.canContinue && item.status !== 'CLOSED'
  const category = [item.categoryName, item.subcategoryName].filter(Boolean).join(' / ')
  return (
    <TableRow hover>
      <TableCell sx={{ ...stickyCellSx, whiteSpace: 'nowrap' }}>
        <CaseCode code={item.caseCode} />
      </TableCell>
      <TableCell>{item.courtLevelName}</TableCell>
      <TableCell>{item.courtName}</TableCell>
      <TableCell>{item.judgeName}</TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>{item.actNumber}</TableCell>
      <TableCell>{names(item.plaintiffs)}</TableCell>
      <TableCell>{names(item.defendants)}</TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {item.objectSnippet}
        </Typography>
      </TableCell>
      <TableCell>{category}</TableCell>
      <TableCell>
        {item.resultName}
        {item.isFinal && (
          <Typography variant="caption" color="text.secondary" component="div">
            {t.final}
          </Typography>
        )}
      </TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>{item.decisionDate ? formatTiraneDate(item.decisionDate) : ''}</TableCell>
      <TableCell>{item.officeName}</TableCell>
      <TableCell>
        <StatusChip kind="status" value={item.status} />
      </TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>
        {item.canContinue ? (
          <Button size="small" variant="outlined" onClick={onContinue} aria-label={t.continueLabel(item.caseCode)}>
            {t.continueButton}
          </Button>
        ) : (
          <Stack spacing={0.5} sx={{ alignItems: 'flex-start' }}>
            <Typography variant="body2" color="warning.main">
              {item.reason}
            </Typography>
            {notClosed && <NotifyButton caseId={item.casePublicId} onClick={onNotify} />}
          </Stack>
        )}
      </TableCell>
    </TableRow>
  )
}

/** "Njofto avokatin dhe Kryetarin" when the case has a primary lawyer, "Njofto Kryetarin" when not (the API tells). */
function NotifyButton({ caseId, onClick }: { caseId: string; onClick: () => void }) {
  const recipients = useNotifyRecipients(caseId)
  return (
    <Button size="small" onClick={onClick} disabled={!recipients.data}>
      {t.notify(recipients.data?.hasPrimaryLawyer ?? false)}
    </Button>
  )
}

// ---------------------------------------------------------------- next level

function NextLevel({ previousCasePublicId, onBack }: { previousCasePublicId: string; onBack: () => void }) {
  const preview = useNextLevelPreview(previousCasePublicId)
  const register = useRegisterNextLevel(previousCasePublicId)

  const back = (
    <Box>
      <Button onClick={onBack}>{t.back}</Button>
    </Box>
  )

  if (preview.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (preview.isError) {
    // 409: already continued, archived or not closed — the table shows the current state
    return (
      <Stack spacing={2}>
        <Alert severity="warning">{problemMessage(preview.error)}</Alert>
        {back}
      </Stack>
    )
  }

  return (
    <Stack spacing={2}>
      {back}
      <RegistrationForm
        mode="next"
        title={t.nextTitle(preview.data.caseCode)}
        above={<PreviewPanel preview={preview.data} />}
        send={(request) => register.mutateAsync(request)}
        anotherLabel={t.back}
        onAnother={onBack}
      />
    </Stack>
  )
}

function PreviewPanel({ preview }: { preview: NextLevelPreview }) {
  return (
    <FormSection title={t.previewTitle}>
      <Typography>
        <strong>{t.previous}:</strong> {preview.caseCode} · {preview.courtLevelName} · {preview.courtName}
      </Typography>
      <Box>
        <Typography variant="h5" component="h3">
          {t.object}
        </Typography>
        <Typography sx={{ whiteSpace: 'pre-wrap' }}>{preview.objectText}</Typography>
      </Box>
      <Box>
        <Typography variant="h5" component="h3">
          {t.parties}
        </Typography>
        {preview.parties.length === 0 ? (
          <Typography color="text.secondary">{t.noParties}</Typography>
        ) : (
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {preview.parties.map((p, i) => (
              <li key={`${p.displayName}-${i}`}>
                {p.role} — {p.displayName} ({sq.partyEditor.kinds[p.kind] ?? p.kind}
                {p.identifier ? `, ${sq.partyEditor.nipt} ${p.identifier}` : ''})
              </li>
            ))}
          </Box>
        )}
      </Box>
      {preview.institutionLinks.length > 0 && (
        <Box>
          <Typography variant="h5" component="h3">
            {t.links}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {preview.institutionLinks.map((l, i) => (
              <li key={`${l.institution}-${i}`}>
                {l.institution} ({t.modes[l.mode] ?? l.mode})
              </li>
            ))}
          </Box>
        </Box>
      )}
      <Typography variant="body2" color="text.secondary">
        {t.linksNote}
      </Typography>
    </FormSection>
  )
}
