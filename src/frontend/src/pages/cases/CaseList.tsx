import {
  Alert,
  Badge,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  IconButton,
  Paper,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import dayjs from 'dayjs'
import { useId, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { CirclePause, UserPlus } from 'lucide-react'
import { AppDatePicker } from '../../components/AppDatePicker'
import { AssignLawyerDialog } from '../../components/AssignLawyerDialog'
import { InstitutionPicker } from '../../components/InstitutionPicker'
import { JudgePicker } from '../../components/JudgePicker'
import { OptionPicker } from '../../components/OptionPicker'
import { AppSelect } from '../../components/ui/AppSelect'
import { CaseCode } from '../../components/ui/CaseCode'
import { PaginationFooter } from '../../components/ui/DataTable'
import { PageHeader } from '../../components/ui/PageHeader'
import { StatusChip } from '../../components/ui/StatusChip'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { CourtPicker } from '../../components/CourtPicker'
import {
  useCaseList,
  lawyerOptionsOf,
  useCaseTabs,
  useFilterOptions,
  type CaseListFilters,
  type CaseListItem,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCaseCategories, useCourtLevels, useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { addDays, formatDate, formatDateTime, tiraneDateOf, todayInTirane } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { isoDateOf, type PickerValue } from '../../lib/pickerValues'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'

const t = sq.cases.list

/**
 * URL query parameters of the list (shareable; Back/Forward keep them). judgeName / institutionName only label the
 * chosen judge / institution (chips, the picker) without another request; they are not sent to the API.
 */
type Param =
  | 'tab'
  | 'q'
  | 'status'
  | 'level'
  | 'court'
  | 'judge'
  | 'judgeName'
  | 'category'
  | 'priority'
  | 'suspended'
  | 'from'
  | 'to'
  | 'regFrom'
  | 'regTo'
  | 'lawyer'
  | 'office'
  | 'institution'
  | 'institutionName'
  | 'filter'
const FILTER_PARAMS: Param[] = [
  'q',
  'filter',
  'status',
  'level',
  'court',
  'judge',
  'judgeName',
  'category',
  'priority',
  'suspended',
  'from',
  'to',
  'regFrom',
  'regTo',
  'lawyer',
  'office',
  'institution',
  'institutionName',
]

/**
 * Cases waiting for a lawyer have no lawyer, category, priority or suspension yet: those COLUMNS are hidden there.
 * The filters are not: the "Filtra" panel shows every filter on every tab.
 */
const TABS_WITHOUT_CLASSIFICATION = new Set(['registered', 'unassigned'])

const HIDE_ON_PHONE = { display: { xs: 'none', md: 'table-cell' } } as const
const TRUNCATE = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const
const PANEL_FIELD = { width: { xs: '100%', md: 240 } } as const

interface Column {
  key: 'code' | 'act' | 'court' | 'lawyer' | 'assign' | 'hearing' | 'priority' | 'status' | 'suspended'
  label: string
  /** shorter header on a phone */
  phoneLabel?: string
  /** responsive width; the court takes the rest */
  width?: { xs: string | number; md: string | number }
  phone: boolean
  classification?: boolean
}

// On a phone the court is shown under the code (its own column would be a few pixels wide).
const COLUMNS: Column[] = [
  { key: 'code', label: t.columns.code, width: { xs: 'auto', md: 150 }, phone: true },
  { key: 'act', label: t.columns.actNumber, width: { xs: 0, md: 130 }, phone: false },
  { key: 'court', label: t.columns.court, phone: false },
  { key: 'lawyer', label: t.columns.lawyer, width: { xs: 0, md: 170 }, phone: false },
  { key: 'hearing', label: t.columns.nextHearing, phoneLabel: t.columns.hearingShort, width: { xs: 92, md: 150 }, phone: true },
  { key: 'priority', label: t.columns.priority, width: { xs: 0, md: 120 }, phone: false, classification: true },
  { key: 'status', label: t.columns.status, width: { xs: 112, md: 120 }, phone: true },
  { key: 'suspended', label: '', width: { xs: 28, md: 44 }, phone: true, classification: true },
]

/** "Pa avokat" for a user who may assign: the (always empty) lawyer column becomes the assign button. */
const ASSIGN_COLUMN: Column = {
  key: 'assign',
  label: sq.common.actions,
  phoneLabel: '', // too narrow for a header on a phone; the icon button has its own label
  width: { xs: 48, md: 170 },
  phone: true,
}

/** Less padding on a phone, where every pixel of the row counts. */
const CELL_PADDING = { px: { xs: 1, md: 2 } } as const

const STATUS_OPTIONS = [
  { value: 'OPEN', label: t.open },
  { value: 'CLOSED', label: t.closed },
  { value: 'ALL', label: t.allStatuses },
]

/**
 * History state of a case page opened from this list: how many history entries back the list is (1 from the row;
 * the case page adds one per tab or level link), so its back link returns to the same list page and filters.
 */
export interface FromCaseList {
  listDepth: number
}

/**
 * The case list (task 022): tabs from the API with counts, filters in the URL, keyset paging (10 rows by default,
 * "‹ Para" / "Pas ›", the page in the URL). Rows are real links, so Ctrl/middle-click opens a case in a new tab and
 * Back returns to the same list and page.
 */
export function CaseListPage() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
  const theme = useTheme()
  const narrow = useMediaQuery(theme.breakpoints.down('md'))
  const [params, setParams] = useSearchParams()
  const get = (name: Param) => params.get(name) ?? ''
  // any filter change starts again at page 1
  const set = (updates: Partial<Record<Param, string>>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [name, value] of Object.entries(updates)) {
          if (value) next.set(name, value)
          else next.delete(name)
        }
        next.delete('page')
        return next
      },
      { replace: true },
    )

  const tabs = useCaseTabs()
  const courts = useCourts()
  const levels = useCourtLevels()
  const categories = useCaseCategories()
  const [panelOpen, setPanelOpen] = useState(false)
  const panelId = useId()
  const [assigning, setAssigning] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const userTabs = tabs.data?.tabs ?? []
  const requested = get('tab')
  const tab = userTabs.some((x) => x.tab === requested) ? requested : (tabs.data?.defaultTab ?? '')
  const classification = !TABS_WITHOUT_CLASSIFICATION.has(tab)
  // the Kryetar filters by the lawyers of the office; the Monitor by office and lawyer (task 031)
  const canFilterOffice = permissions.includes('case.view_all')
  const canFilterLawyer = permissions.includes('case.view_office') || canFilterOffice
  const filterOptions = useFilterOptions(canFilterLawyer)

  const search = get('q')
  const q = useDebounced(serverQuery(search), 300)
  const filters: CaseListFilters = {
    tab,
    q,
    status: get('status'),
    courtLevel: get('level'),
    courtId: get('court'),
    judgeId: get('judge'),
    categoryCode: get('category'),
    priority: get('priority'),
    suspended: get('suspended'),
    hearingFrom: get('from'),
    hearingTo: get('to'),
    registeredFrom: get('regFrom'),
    registeredTo: get('regTo'),
    lawyerId: canFilterLawyer ? get('lawyer') : '',
    officeId: canFilterOffice ? get('office') : '',
    institutionId: get('institution'),
    filter: get('filter'),
  }
  const paging = useKeysetPaging()
  const list = useCaseList(filters, { cursor: paging.cursor, size: paging.size }, tab !== '')
  const items = list.data?.items ?? []
  // the button only for who may assign; the API checks each case again
  const canAssign = tab === 'unassigned' && permissions.includes('case.assign_lawyer')
  const columns = COLUMNS.filter((c) => classification || !c.classification).map((c) =>
    canAssign && c.key === 'lawyer' ? ASSIGN_COLUMN : c,
  )
  const lawyerOptions = lawyerOptionsOf(filterOptions.data?.lawyers ?? [], filters.officeId).map((x) =>
    x.value === me.data?.publicId ? { ...x, label: `${x.label} ${sq.cases.assign.self}` } : x,
  )
  const officeOptions = (filterOptions.data?.offices ?? []).map((o) => ({ value: o.publicId, label: o.name }))
  // a chosen lawyer of another office goes when the office changes
  const setOffice = (office: string) => {
    const lawyers = lawyerOptionsOf(filterOptions.data?.lawyers ?? [], office)
    set({ office, lawyer: lawyers.some((x) => x.value === filters.lawyerId) ? filters.lawyerId : '' })
  }
  const levelOptions = (levels.data ?? []).map((l) => ({ value: l.code, label: l.name }))
  // a chosen court of another level goes when the level changes
  const setLevel = (level: string) => {
    const court = courts.data?.find((c) => String(c.id) === filters.courtId)
    set({ level, court: court && level && court.courtLevelCode !== level ? '' : filters.courtId })
  }
  const courtNameOf = (courtId: string | null) => courts.data?.find((c) => String(c.id) === courtId)?.name

  // ---------------------------------------------------------------- active filters (chips + the "Filtra" badge)
  const courtName = courtNameOf(filters.courtId)
  const categoryName = categories.data?.find((c) => c.code === filters.categoryCode)?.name
  const range = (from: string, to: string) => `${formatDate(from) || '…'} – ${formatDate(to) || '…'}`
  const chips: { key: string; label: string; clear: Partial<Record<Param, string>>; inPanel: boolean }[] = []
  if (search.trim()) chips.push({ key: 'q', label: `${t.searchChip}: „${search.trim()}“`, clear: { q: '' }, inPanel: false })
  // a dashboard card's filter (task 046): always visible, removable like any other
  if (filters.filter) {
    const name = t.specialFilters[filters.filter] ?? filters.filter
    chips.push({ key: 'filter', label: `${t.special}: ${name}`, clear: { filter: '' }, inPanel: false })
  }
  if (filters.status) {
    const name = STATUS_OPTIONS.find((o) => o.value === filters.status)?.label ?? filters.status
    chips.push({ key: 'status', label: `${t.status}: ${name}`, clear: { status: '' }, inPanel: narrow })
  }
  if (filters.courtLevel) {
    const name = levelOptions.find((o) => o.value === filters.courtLevel)?.label ?? filters.courtLevel
    chips.push({ key: 'level', label: `${t.courtLevel}: ${name}`, clear: { level: '' }, inPanel: true })
  }
  if (filters.courtId) {
    chips.push({ key: 'court', label: `${t.court}: ${courtName ?? filters.courtId}`, clear: { court: '' }, inPanel: narrow })
  }
  if (filters.judgeId) {
    chips.push({ key: 'judge', label: `${t.judge}: ${get('judgeName') || '…'}`, clear: { judge: '', judgeName: '' }, inPanel: true })
  }
  if (filters.officeId) {
    const officeName = officeOptions.find((x) => x.value === filters.officeId)?.label
    chips.push({ key: 'office', label: `${t.office}: ${officeName ?? '…'}`, clear: { office: '', lawyer: '' }, inPanel: true })
  }
  if (filters.lawyerId) {
    const lawyerName = lawyerOptions.find((x) => x.value === filters.lawyerId)?.label
    chips.push({ key: 'lawyer', label: `${t.lawyer}: ${lawyerName ?? '…'}`, clear: { lawyer: '' }, inPanel: true })
  }
  if (filters.institutionId) {
    chips.push({
      key: 'institution',
      label: `${t.institution}: ${get('institutionName') || '…'}`,
      clear: { institution: '', institutionName: '' },
      inPanel: true,
    })
  }
  if (filters.categoryCode) {
    chips.push({
      key: 'category',
      label: `${t.category}: ${categoryName ?? filters.categoryCode}`,
      clear: { category: '' },
      inPanel: true,
    })
  }
  if (filters.priority) {
    chips.push({
      key: 'priority',
      label: `${t.priority}: ${sq.cases.priority[filters.priority] ?? filters.priority}`,
      clear: { priority: '' },
      inPanel: true,
    })
  }
  if (filters.suspended) {
    chips.push({
      key: 'suspended',
      label: `${t.suspended}: ${filters.suspended === 'true' ? t.yes : t.no}`,
      clear: { suspended: '' },
      inPanel: true,
    })
  }
  if (filters.hearingFrom || filters.hearingTo) {
    chips.push({
      key: 'hearing',
      label: `${t.hearingRange}: ${range(filters.hearingFrom, filters.hearingTo)}`,
      clear: { from: '', to: '' },
      inPanel: true,
    })
  }
  if (filters.registeredFrom || filters.registeredTo) {
    chips.push({
      key: 'registered',
      label: `${t.registeredRange}: ${range(filters.registeredFrom, filters.registeredTo)}`,
      clear: { regFrom: '', regTo: '' },
      inPanel: true,
    })
  }
  const panelCount = chips.filter((c) => c.inPanel).length
  const anyFilter = chips.length > 0
  const clearFilters = () => set(Object.fromEntries(FILTER_PARAMS.map((name) => [name, ''])))

  // ---------------------------------------------------------------- filter controls
  // not chosen: open cases — or every status while searching (the API's rule; a closed case must be findable)
  const statusField = (
    <AppSelect
      required
      hideRequiredMark
      size="small"
      label={t.status}
      value={filters.status || (search.trim() ? 'ALL' : 'OPEN')}
      onChange={(status) => set({ status })}
      options={STATUS_OPTIONS}
      sx={{ width: { xs: '100%', md: 160 } }}
    />
  )
  const courtField = (
    <Box sx={{ width: { xs: '100%', md: 300 } }}>
      <CourtPicker
        filter
        courts={courts.data ?? []}
        levels={levels.data ?? []}
        levelCode={filters.courtLevel}
        value={filters.courtId}
        onChange={(court) => set({ court: court ? String(court.id) : '' })}
        label={t.court}
        size="small"
      />
    </Box>
  )

  return (
    <Stack spacing={2}>
      <PageHeader
        title={sq.cases.title}
        actions={
          <>
            {permissions.includes('case.link_previous') && (
              <Button variant="outlined" component={Link} to="/cases/continue">
                {sq.cases.continueExisting}
              </Button>
            )}
            {permissions.includes('case.create') && (
              <Button variant="contained" color="secondary" component={Link} to="/cases/register">
                {sq.cases.registerNew}
              </Button>
            )}
          </>
        }
      />

      {tabs.isError && <Alert severity="error">{problemMessage(tabs.error)}</Alert>}
      {userTabs.length > 0 && (
        <Tabs
          value={tab}
          onChange={(_, value: string) => set({ tab: value })}
          variant="scrollable"
          allowScrollButtonsMobile
          aria-label={t.tabsLabel}
          sx={{ borderBottom: 1, borderColor: 'divider' }}
        >
          {userTabs.map((x) => (
            <Tab key={x.tab} value={x.tab} label={`${t.tabs[x.tab] ?? x.tab} (${x.count})`} />
          ))}
        </Tabs>
      )}

      {/* row 1: search grows; status and court beside it (on a phone they move into the panel); the panel button */}
      <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: { xs: 'wrap', md: 'nowrap' } }}>
        <TextField
          size="small"
          label={t.search}
          value={search}
          onChange={(e) => set({ q: e.target.value })}
          slotProps={{ htmlInput: { maxLength: 100 } }}
          sx={{ flex: 1, minWidth: { xs: '100%', md: 240 } }}
        />
        {!narrow && statusField}
        {!narrow && courtField}
        <Badge badgeContent={panelCount} color="primary">
          <Button
            variant={panelOpen ? 'contained' : 'outlined'}
            onClick={() => setPanelOpen((o) => !o)}
            aria-expanded={panelOpen}
            aria-controls={panelId}
          >
            {t.moreFilters}
          </Button>
        </Badge>
      </Stack>

      {/* every filter, on every tab */}
      <Collapse in={panelOpen} id={panelId}>
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: { md: 'flex-end' } }}>
            {narrow && statusField}
            <AppSelect
              filter
              size="small"
              label={t.courtLevel}
              value={filters.courtLevel}
              onChange={setLevel}
              options={levelOptions}
              sx={PANEL_FIELD}
            />
            {narrow && courtField}
            <Box sx={{ width: { xs: '100%', md: 280 } }}>
              <JudgePicker
                filter
                size="small"
                label={t.judge}
                courtId={filters.courtId ? Number(filters.courtId) : null}
                showAll={!filters.courtId}
                courtName={courtNameOf}
                value={filters.judgeId ? { id: filters.judgeId, name: get('judgeName'), courtId: null } : null}
                onChange={(judge) => set({ judge: judge?.id ?? '', judgeName: judge?.name ?? '' })}
              />
            </Box>
            {canFilterOffice && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker filter options={officeOptions} value={filters.officeId} onChange={setOffice} label={t.office} size="small" />
              </Box>
            )}
            {canFilterLawyer && (
              <Box sx={PANEL_FIELD}>
                <OptionPicker
                  filter
                  options={lawyerOptions}
                  value={filters.lawyerId}
                  onChange={(lawyer) => set({ lawyer })}
                  label={t.lawyer}
                  size="small"
                />
              </Box>
            )}
            <Box sx={{ width: { xs: '100%', md: 300 } }}>
              <InstitutionPicker
                filter
                size="small"
                label={t.institution}
                value={filters.institutionId ? { publicId: filters.institutionId, name: get('institutionName') } : null}
                onChange={(institution) =>
                  set({ institution: institution?.publicId ?? '', institutionName: institution?.name ?? '' })
                }
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
            <RangeFilter
              legend={t.hearingRange}
              from={filters.hearingFrom}
              to={filters.hearingTo}
              onFrom={(from) => set({ from })}
              onTo={(to) => set({ to })}
            />
            <RangeFilter
              legend={t.registeredRange}
              from={filters.registeredFrom}
              to={filters.registeredTo}
              onFrom={(regFrom) => set({ regFrom })}
              onTo={(regTo) => set({ regTo })}
            />
          </Stack>
        </Paper>
      </Collapse>

      {anyFilter && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }} aria-label={t.activeFilters}>
          {chips.map((chip) => (
            <Chip key={chip.key} label={chip.label} onDelete={() => set(chip.clear)} />
          ))}
          <Button size="small" onClick={clearFilters}>
            {t.clearFilters}
          </Button>
        </Stack>
      )}

      {list.isError && <Alert severity="error">{problemMessage(list.error)}</Alert>}
      {(tabs.isPending || (tab !== '' && list.isPending)) && <CircularProgress aria-label={sq.errors.loading} />}

      {list.isSuccess && items.length === 0 && (
        <Box sx={{ py: 2 }}>
          {search.trim() !== '' ? (
            <Typography>
              {sq.search.noResults(search.trim())}{' '}
              <Button size="small" onClick={() => set({ q: '' })}>
                {t.clearSearch}
              </Button>
            </Typography>
          ) : anyFilter ? (
            <Typography>
              {t.emptyFilters}{' '}
              <Button size="small" onClick={clearFilters}>
                {t.clearFilters}
              </Button>
            </Typography>
          ) : (
            <Typography color="text.secondary">{t.emptyTab}</Typography>
          )}
        </Box>
      )}

      {items.length > 0 && (
        <Box>
          <Paper variant="outlined">
            {/* div-based table: every body row is a real <a>, which a <tr> cannot be */}
            <Table component="div" size="small" sx={{ tableLayout: 'fixed', width: '100%' }} aria-label={t.tabs[tab] ?? sq.cases.title}>
              <TableHead component="div">
                <TableRow component="div">
                  {columns.map((c) => (
                    <TableCell
                      key={c.key}
                      component="div"
                      sx={{ width: c.width, ...CELL_PADDING, ...TRUNCATE, ...(c.phone ? {} : HIDE_ON_PHONE) }}
                    >
                      {c.phoneLabel !== undefined ? (
                        <>
                          <Box component="span" sx={{ display: { xs: 'none', md: 'inline' } }}>
                            {c.label}
                          </Box>
                          <Box component="span" sx={{ display: { xs: 'inline', md: 'none' } }}>
                            {c.phoneLabel}
                          </Box>
                        </>
                      ) : (
                        c.label
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              </TableHead>
              <TableBody component="div">
                {items.map((item) => (
                  <CaseRow
                    key={item.publicId}
                    item={item}
                    columns={columns}
                    onAssign={canAssign ? () => setAssigning(item.publicId) : undefined}
                  />
                ))}
              </TableBody>
            </Table>
          </Paper>
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={items.length}
            total={Number(list.data?.total ?? 0)}
            nextCursor={list.data?.nextCursor}
            busy={list.isFetching}
            stale={list.isPlaceholderData}
          />
        </Box>
      )}

      {assigning && (
        <AssignLawyerDialog
          casePublicId={assigning}
          onClose={() => setAssigning(null)}
          onAssigned={(name) => {
            setAssigning(null)
            setToast(sq.cases.assign.success(name))
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

const FROM_LIST: FromCaseList = { listDepth: 1 }

/**
 * One case as a real link: Tab reaches it, Enter opens it, Ctrl/middle-click opens a new tab. With `onAssign` the row
 * holds a button, which cannot sit inside an <a>: then the case code is the link, stretched over the whole row
 * (::after), and the button lies above it.
 */
function CaseRow({ item, columns, onAssign }: { item: CaseListItem; columns: Column[]; onAssign?: () => void }) {
  const today = todayInTirane()
  const hearingDay = tiraneDateOf(item.nextHearingAt)
  const when = hearingDay === today ? t.today : hearingDay === addDays(today, 1) ? t.tomorrow : null
  const hearing = item.nextHearingAt ? formatDateTime(item.nextHearingAt) : t.none
  const code = <CaseCode code={item.caseCode} />
  const cell: Record<Column['key'], ReactNode> = {
    code: (
      <>
        {/* the row is the link: the code is styled as one, not a nested <a> */}
        <Box sx={TRUNCATE}>
          {onAssign ? (
            <Box
              component={Link}
              to={`/cases/${item.publicId}`}
              state={FROM_LIST}
              sx={{
                textDecoration: 'none',
                outline: 'none',
                '&::after': { content: '""', position: 'absolute', inset: 0 },
                '&:focus-visible::after': { outline: 2, outlineColor: 'primary.main', outlineOffset: -2 },
              }}
            >
              {code}
            </Box>
          ) : (
            code
          )}
        </Box>
        <Typography variant="caption" component="p" color="text.secondary" sx={{ display: { xs: 'block', md: 'none' } }}>
          {item.courtName}
        </Typography>
      </>
    ),
    act: <Box sx={TRUNCATE}>{item.actNumber}</Box>,
    court: (
      <Tooltip title={item.courtName} placement="top-start">
        <Box sx={TRUNCATE}>{item.courtName}</Box>
      </Tooltip>
    ),
    lawyer: (
      <Tooltip title={item.primaryLawyerName ?? ''} placement="top-start">
        <Box sx={TRUNCATE}>{item.primaryLawyerName ?? t.none}</Box>
      </Tooltip>
    ),
    // above the stretched link (position + z-index), so a click assigns instead of opening the case
    assign: onAssign ? (
      <Box sx={{ position: 'relative', zIndex: 1 }}>
        <Button
          variant="outlined"
          size="small"
          startIcon={<UserPlus size={18} aria-hidden />}
          onClick={onAssign}
          aria-label={sq.cases.assign.buttonFor(item.caseCode)}
          sx={{ display: { xs: 'none', md: 'inline-flex' } }}
        >
          {sq.cases.assign.button}
        </Button>
        <Tooltip title={sq.cases.assign.button}>
          <IconButton
            size="small"
            color="primary"
            onClick={onAssign}
            aria-label={sq.cases.assign.buttonFor(item.caseCode)}
            sx={{ display: { xs: 'inline-flex', md: 'none' } }}
          >
            <UserPlus size={20} aria-hidden />
          </IconButton>
        </Tooltip>
      </Box>
    ) : null,
    // passed without an outcome (task 030): red, with the word; today / tomorrow as tinted chips (red / orange)
    hearing: item.nextHearingIsOverdue ? (
      <Box component="span" sx={{ color: 'error.main', fontWeight: 600 }}>
        {`${hearing} (${sq.cases.hearings.overdueShort})`}
      </Box>
    ) : when ? (
      <Chip size="small" color={hearingDay === today ? 'error' : 'warning'} label={`${when} ${hearing.slice(11)}`} />
    ) : (
      hearing
    ),
    priority: <StatusChip kind="priority" value={item.priority} />,
    status: <StatusChip kind="status" value={item.status} />,
    suspended: item.isSuspended ? (
      <Tooltip title={t.suspendedTooltip}>
        <Box component="span" role="img" aria-label={t.suspendedTooltip} sx={{ display: 'inline-flex', color: 'warning.dark' }}>
          <CirclePause size={20} aria-hidden />
        </Box>
      </Tooltip>
    ) : null,
  }

  const cells = columns.map((c) => (
    <TableCell key={c.key} component="div" sx={{ ...CELL_PADDING, ...(c.phone ? {} : HIDE_ON_PHONE), overflow: 'hidden' }}>
      {cell[c.key]}
    </TableCell>
  ))

  if (onAssign) {
    return (
      <TableRow hover component="div" sx={{ position: 'relative', cursor: 'pointer' }}>
        {cells}
      </TableRow>
    )
  }

  return (
    <TableRow
      hover
      component={Link}
      to={`/cases/${item.publicId}`}
      state={FROM_LIST}
      sx={{
        textDecoration: 'none',
        color: 'inherit',
        cursor: 'pointer',
        '&:focus-visible': { outline: 2, outlineColor: 'primary.main', outlineOffset: -2 },
      }}
    >
      {cells}
    </TableRow>
  )
}

/** A "nga – deri" pair of date filters under one legend (hearing dates, registration dates). */
function RangeFilter({
  legend,
  from,
  to,
  onFrom,
  onTo,
}: {
  legend: string
  from: string
  to: string
  onFrom: (isoDate: string) => void
  onTo: (isoDate: string) => void
}) {
  return (
    <Box component="fieldset" sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
      <Typography component="legend" variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {legend}
      </Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} useFlexGap sx={{ alignItems: { sm: 'center' } }}>
        {/* short labels; the legend names the pair (fieldset) */}
        <DateFilter label={t.from} value={from} maxDate={to} onChange={onFrom} />
        <Typography aria-hidden sx={{ display: { xs: 'none', sm: 'block' } }}>
          –
        </Typography>
        <DateFilter label={t.to} value={to} minDate={from} onChange={onTo} />
      </Stack>
    </Box>
  )
}

/**
 * A date filter kept in the URL as yyyy-MM-dd. Local picker state holds a half-typed date; only a complete date (or
 * clearing it) reaches the URL, and a change of the URL (e.g. "Pastro filtrat") resets the picker.
 */
function DateFilter({
  label,
  value,
  onChange,
  minDate,
  maxDate,
}: {
  label: string
  value: string
  onChange: (isoDate: string) => void
  minDate?: string
  maxDate?: string
}) {
  const [local, setLocal] = useState<PickerValue>(value ? dayjs(value) : null)
  const [fromUrl, setFromUrl] = useState(value)
  if (value !== fromUrl) {
    setFromUrl(value)
    setLocal(value ? dayjs(value) : null)
  }

  const change = (next: PickerValue) => {
    setLocal(next)
    if (next === null) onChange('')
    else if (isoDateOf(next)) onChange(isoDateOf(next) ?? '')
  }

  return (
    <Box sx={{ width: { xs: '100%', sm: 170 } }}>
      <AppDatePicker
        label={label}
        value={local}
        onChange={change}
        size="small"
        fullWidth
        minDate={minDate ? dayjs(minDate) : undefined}
        maxDate={maxDate ? dayjs(maxDate) : undefined}
      />
    </Box>
  )
}
