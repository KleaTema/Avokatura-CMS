import {
  Alert,
  Badge,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  IconButton,
  MenuItem,
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
import { useEffect, useId, useRef, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { CirclePause, UserPlus } from 'lucide-react'
import { AppDatePicker } from '../../components/AppDatePicker'
import { AssignLawyerDialog } from '../../components/AssignLawyerDialog'
import { OptionPicker } from '../../components/OptionPicker'
import { CaseCode } from '../../components/ui/CaseCode'
import { PageHeader } from '../../components/ui/PageHeader'
import { StatusChip } from '../../components/ui/StatusChip'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { CourtPicker } from '../../components/CourtPicker'
import {
  useCaseList,
  useCaseTabs,
  useOfficeLawyers,
  type CaseListFilters,
  type CaseListItem,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCaseCategories, useCourtLevels, useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { addDays, formatDate, formatDateTime, tiraneDateOf, todayInTirane } from '../../lib/dates'
import { isoDateOf, type PickerValue } from '../../lib/pickerValues'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'

const t = sq.cases.list

/** URL query parameters of the list (shareable; Back/Forward keep them). */
type Param = 'tab' | 'q' | 'status' | 'court' | 'category' | 'priority' | 'suspended' | 'from' | 'to' | 'lawyer'
const FILTER_PARAMS: Param[] = ['q', 'status', 'court', 'category', 'priority', 'suspended', 'from', 'to', 'lawyer']

/**
 * Cases waiting for a lawyer have no lawyer, category, priority or suspension yet: those filters and columns are
 * hidden.
 */
const TABS_WITHOUT_CLASSIFICATION = new Set(['registered', 'unassigned'])

/** A select whose "" option ("Të hapura", "Të gjitha") is shown as a value, not as an empty field. */
const SHOW_EMPTY_OPTION = { select: { displayEmpty: true }, inputLabel: { shrink: true } } as const

const HIDE_ON_PHONE ={ display: { xs: 'none', md: 'table-cell' } } as const
const TRUNCATE = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const

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

/**
 * The case list (task 022): tabs from the API with counts, filters in the URL, keyset paging with infinite scroll.
 * Rows are real links, so Ctrl/middle-click opens a case in a new tab and Back returns to the same list and position
 * (the pages stay cached; Layout restores the scroll).
 */
export function CaseListPage() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
  const theme = useTheme()
  const narrow = useMediaQuery(theme.breakpoints.down('md'))
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

  const tabs = useCaseTabs()
  const courts = useCourts()
  const levels = useCourtLevels()
  const categories = useCaseCategories()
  const [panelOpen, setPanelOpen] = useState(false)
  const panelId = useId()
  const canFilterLawyer = permissions.includes('case.view_office')
  const officeLawyers = useOfficeLawyers(canFilterLawyer)
  const [assigning, setAssigning] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const userTabs = tabs.data?.tabs ?? []
  const requested = get('tab')
  const tab = userTabs.some((x) => x.tab === requested) ? requested : (tabs.data?.defaultTab ?? '')
  const classification = !TABS_WITHOUT_CLASSIFICATION.has(tab)

  const search = get('q')
  const q = useDebounced(serverQuery(search), 300)
  const filters: CaseListFilters = {
    tab,
    q,
    status: get('status'),
    courtId: get('court'),
    // filters the tab does not show are not sent either
    categoryCode: classification ? get('category') : '',
    priority: classification ? get('priority') : '',
    suspended: classification ? get('suspended') : '',
    hearingFrom: get('from'),
    hearingTo: get('to'),
    lawyerId: classification && canFilterLawyer ? get('lawyer') : '',
  }
  const list = useCaseList(filters, tab !== '')
  const items = list.data?.pages.flatMap((p) => p.items) ?? []
  // the button only for who may assign; the API checks each case again
  const canAssign = tab === 'unassigned' && permissions.includes('case.assign_lawyer')
  const columns = COLUMNS.filter((c) => classification || !c.classification).map((c) =>
    canAssign && c.key === 'lawyer' ? ASSIGN_COLUMN : c,
  )
  const lawyerOptions = (officeLawyers.data ?? []).map((x) => ({
    value: x.publicId,
    label: x.isSelf ? `${x.fullName} ${sq.cases.assign.self}` : x.fullName,
  }))

  // ---------------------------------------------------------------- active filters (chips + the "Filtra" badge)
  const courtName = courts.data?.find((c) => String(c.id) === filters.courtId)?.name
  const categoryName = categories.data?.find((c) => c.code === filters.categoryCode)?.name
  const chips: { key: string; label: string; clear: Partial<Record<Param, string>>; inPanel: boolean }[] = []
  if (search.trim()) chips.push({ key: 'q', label: `${t.searchChip}: „${search.trim()}“`, clear: { q: '' }, inPanel: false })
  if (filters.status) {
    chips.push({ key: 'status', label: `${t.status}: ${t.closed}`, clear: { status: '' }, inPanel: narrow })
  }
  if (filters.courtId) {
    chips.push({ key: 'court', label: `${t.court}: ${courtName ?? filters.courtId}`, clear: { court: '' }, inPanel: narrow })
  }
  if (filters.lawyerId) {
    const lawyerName = lawyerOptions.find((x) => x.value === filters.lawyerId)?.label
    chips.push({ key: 'lawyer', label: `${t.lawyer}: ${lawyerName ?? '…'}`, clear: { lawyer: '' }, inPanel: true })
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
      label: `${t.hearingRange}: ${formatDate(filters.hearingFrom) || '…'} – ${formatDate(filters.hearingTo) || '…'}`,
      clear: { from: '', to: '' },
      inPanel: true,
    })
  }
  const panelCount = chips.filter((c) => c.inPanel).length
  const anyFilter = chips.length > 0
  const clearFilters = () => set(Object.fromEntries(FILTER_PARAMS.map((name) => [name, ''])))

  // infinite scroll: load the next page when the end of the list comes near
  const sentinel = useRef<HTMLDivElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = list
  useEffect(() => {
    const node = sentinel.current
    if (!node || !hasNextPage) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && !isFetchingNextPage) void fetchNextPage()
      },
      { rootMargin: '400px' },
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  // ---------------------------------------------------------------- filter controls
  const statusField = (
    <TextField
      select
      size="small"
      label={t.status}
      value={get('status')}
      onChange={(e) => set({ status: e.target.value })}
      slotProps={SHOW_EMPTY_OPTION}
      sx={{ width: { xs: '100%', md: 160 } }}
    >
      <MenuItem value="">{t.open}</MenuItem>
      <MenuItem value="CLOSED">{t.closed}</MenuItem>
    </TextField>
  )
  const courtField = (
    <Box sx={{ width: { xs: '100%', md: 300 } }}>
      <CourtPicker
        courts={courts.data ?? []}
        levels={levels.data ?? []}
        levelCode=""
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

      <Collapse in={panelOpen} id={panelId}>
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: { md: 'flex-end' } }}>
            {narrow && statusField}
            {narrow && courtField}
            {classification && canFilterLawyer && (
              <Box sx={{ width: { xs: '100%', md: 240 } }}>
                <OptionPicker
                  options={lawyerOptions}
                  value={filters.lawyerId}
                  onChange={(lawyer) => set({ lawyer })}
                  label={t.lawyer}
                  size="small"
                />
              </Box>
            )}
            {classification && (
              <>
                <PanelSelect label={t.category} value={filters.categoryCode} onChange={(category) => set({ category })}>
                  {(categories.data ?? []).map((c) => (
                    <MenuItem key={c.code} value={c.code}>
                      {c.name}
                    </MenuItem>
                  ))}
                </PanelSelect>
                <PanelSelect label={t.priority} value={filters.priority} onChange={(priority) => set({ priority })}>
                  {Object.entries(sq.cases.priority).map(([code, name]) => (
                    <MenuItem key={code} value={code}>
                      {name}
                    </MenuItem>
                  ))}
                </PanelSelect>
                <PanelSelect label={t.suspended} value={filters.suspended} onChange={(suspended) => set({ suspended })}>
                  <MenuItem value="true">{t.yes}</MenuItem>
                  <MenuItem value="false">{t.no}</MenuItem>
                </PanelSelect>
              </>
            )}
            <Box component="fieldset" sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
              <Typography component="legend" variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                {t.hearingRange}
              </Typography>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} useFlexGap sx={{ alignItems: { sm: 'center' } }}>
                <DateFilter label={t.from} value={filters.hearingFrom} maxDate={filters.hearingTo} onChange={(from) => set({ from })} />
                <Typography aria-hidden sx={{ display: { xs: 'none', sm: 'block' } }}>
                  –
                </Typography>
                <DateFilter label={t.to} value={filters.hearingTo} minDate={filters.hearingFrom} onChange={(to) => set({ to })} />
              </Stack>
            </Box>
          </Stack>
        </Paper>
      </Collapse>

      {anyFilter && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }} aria-label={t.activeFilters}>
          {chips.map((chip) => (
            <Chip
              key={chip.key}
              label={chip.label}
              onDelete={() => set(chip.clear)}
            />
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
          <Box ref={sentinel} sx={{ height: 1 }} />
          <Stack direction="row" spacing={2} sx={{ py: 2, alignItems: 'center' }}>
            {isFetchingNextPage && <Typography color="text.secondary">{t.loadingMore}</Typography>}
            {hasNextPage && !isFetchingNextPage && (
              <Button onClick={() => void fetchNextPage()}>{sq.common.loadMore}</Button>
            )}
            {!hasNextPage && <Typography color="text.secondary">{t.end(items.length)}</Typography>}
          </Stack>
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
    // today / tomorrow stand out as tinted chips (red / orange)
    hearing: when ? (
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

function PanelSelect({
  label,
  value,
  onChange,
  children,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  children: ReactNode
}) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      slotProps={SHOW_EMPTY_OPTION}
      sx={{ width: { xs: '100%', md: 170 } }}
    >
      <MenuItem value="">{t.any}</MenuItem>
      {children}
    </TextField>
  )
}

/**
 * A hearing-date filter kept in the URL as yyyy-MM-dd. Local picker state holds a half-typed date; only a complete
 * date (or clearing it) reaches the URL, and a change of the URL (e.g. "Pastro filtrat") resets the picker.
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
