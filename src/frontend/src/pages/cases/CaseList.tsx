import {
  Alert,
  Badge,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
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
import { AppDatePicker } from '../../components/AppDatePicker'
import { CourtPicker } from '../../components/CourtPicker'
import { useCaseList, useCaseTabs, type CaseListFilters, type CaseListItem } from '../../features/cases/api'
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
type Param = 'tab' | 'q' | 'status' | 'court' | 'category' | 'priority' | 'suspended' | 'from' | 'to'
const FILTER_PARAMS: Param[] = ['q', 'status', 'court', 'category', 'priority', 'suspended', 'from', 'to']

/** Cases waiting for a lawyer have no category, priority or suspension yet: those filters and columns are hidden. */
const TABS_WITHOUT_CLASSIFICATION = new Set(['registered', 'unassigned'])

/** A select whose "" option ("Të hapura", "Të gjitha") is shown as a value, not as an empty field. */
const SHOW_EMPTY_OPTION = { select: { displayEmpty: true }, inputLabel: { shrink: true } } as const

const HIDE_ON_PHONE ={ display: { xs: 'none', md: 'table-cell' } } as const
const TRUNCATE = { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } as const

interface Column {
  key: 'code' | 'act' | 'court' | 'lawyer' | 'hearing' | 'priority' | 'status' | 'suspended'
  label: string
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
  { key: 'hearing', label: t.columns.nextHearing, width: { xs: 96, md: 150 }, phone: true },
  { key: 'priority', label: t.columns.priority, width: { xs: 0, md: 120 }, phone: false, classification: true },
  { key: 'status', label: t.columns.status, width: { xs: 96, md: 120 }, phone: true },
  { key: 'suspended', label: '', width: { xs: 28, md: 44 }, phone: true, classification: true },
]

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
    // TODO(task 023): lawyer filter (lawyerId = user public id) with the lawyer picker.
  }
  const list = useCaseList(filters, tab !== '')
  const items = list.data?.pages.flatMap((p) => p.items) ?? []
  const columns = COLUMNS.filter((c) => classification || !c.classification)

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
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}
      >
        <Typography variant="h4" component="h1">
          {sq.cases.title}
        </Typography>
        <Stack direction="row" spacing={2} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
          {permissions.includes('case.create') && (
            <Button variant="contained" component={Link} to="/cases/register">
              {sq.cases.registerNew}
            </Button>
          )}
          {permissions.includes('case.link_previous') && (
            <Button variant="outlined" component={Link} to="/cases/continue">
              {sq.cases.continueExisting}
            </Button>
          )}
        </Stack>
      </Stack>

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
        <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1, alignItems: 'center' }} aria-label={t.activeFilters}>
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
                      sx={{ width: c.width, fontWeight: 600, ...CELL_PADDING, ...TRUNCATE, ...(c.phone ? {} : HIDE_ON_PHONE) }}
                    >
                      {c.label}
                    </TableCell>
                  ))}
                </TableRow>
              </TableHead>
              <TableBody component="div">
                {items.map((item) => (
                  <CaseRow key={item.publicId} item={item} columns={columns} />
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
    </Stack>
  )
}

/** One case as a real link: Tab reaches it, Enter opens it, Ctrl/middle-click opens a new tab. */
function CaseRow({ item, columns }: { item: CaseListItem; columns: Column[] }) {
  const today = todayInTirane()
  const hearingDay = tiraneDateOf(item.nextHearingAt)
  const when = hearingDay === today ? t.today : hearingDay === addDays(today, 1) ? t.tomorrow : null
  const hearing = item.nextHearingAt ? formatDateTime(item.nextHearingAt) : t.none
  const status = sq.cases.status[item.status] ?? item.status
  const priority = sq.cases.priority[item.priority] ?? item.priority

  const cell: Record<Column['key'], ReactNode> = {
    code: (
      <>
        <Box sx={{ fontFamily: 'monospace', fontWeight: 600, ...TRUNCATE }}>{item.caseCode}</Box>
        <Box sx={{ display: { xs: 'block', md: 'none' }, color: 'text.secondary', fontSize: '0.8125rem' }}>
          {item.courtName}
        </Box>
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
    hearing: (
      <Box sx={when ? { fontWeight: 700, color: hearingDay === today ? 'error.main' : 'warning.dark' } : undefined}>
        {when ? `${when} ${hearing.slice(11)}` : hearing}
      </Box>
    ),
    priority: (
      <Chip
        size="small"
        label={priority}
        color={item.priority === 'HIGH' ? 'error' : item.priority === 'MEDIUM' ? 'warning' : 'default'}
        variant={item.priority === 'NONE' ? 'outlined' : 'filled'}
      />
    ),
    status: (
      <Chip
        size="small"
        label={status}
        color={item.status === 'ACTIVE' ? 'success' : item.status === 'REGISTERED' ? 'info' : 'default'}
      />
    ),
    suspended: item.isSuspended ? (
      <Tooltip title={t.suspendedTooltip}>
        <Box component="span" role="img" aria-label={t.suspendedTooltip} sx={{ fontSize: 18 }}>
          ⏸
        </Box>
      </Tooltip>
    ) : null,
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
      {columns.map((c) => (
        <TableCell key={c.key} component="div" sx={{ ...CELL_PADDING, ...(c.phone ? {} : HIDE_ON_PHONE), overflow: 'hidden' }}>
          {cell[c.key]}
        </TableCell>
      ))}
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
