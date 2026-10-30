import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import dayjs from 'dayjs'
import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { AppDatePicker } from '../../components/AppDatePicker'
import { CourtPicker } from '../../components/CourtPicker'
import { useCaseList, useCaseTabs, type CaseListFilters, type CaseListItem } from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCaseCategories, useCourtLevels, useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { addDays, formatDateTime, tiraneDateOf, todayInTirane } from '../../lib/dates'
import { isoDateOf, type PickerValue } from '../../lib/pickerValues'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'

const t = sq.cases.list

/** URL query parameters of the list (shareable; Back/Forward keep them). */
const PARAMS = ['tab', 'q', 'status', 'court', 'category', 'priority', 'suspended', 'from', 'to'] as const
type Param = (typeof PARAMS)[number]
const FILTER_PARAMS: Param[] = ['q', 'status', 'court', 'category', 'priority', 'suspended', 'from', 'to']

// Columns: md and up all; on a phone only code, court, next hearing and status (+ the suspended mark).
const GRID = {
  display: 'grid',
  gridTemplateColumns: {
    xs: 'minmax(0, 1fr) minmax(0, 1fr)',
    md: '170px 120px minmax(0, 1.4fr) minmax(0, 1fr) 160px 120px 110px 28px',
  },
  columnGap: 2,
  rowGap: 0.5,
  alignItems: 'center',
  px: 2,
  py: 1.25,
} as const
const DESKTOP_ONLY = { display: { xs: 'none', md: 'block' } } as const

/**
 * The case list (task 022): tabs from the API with counts, filters in the URL, keyset paging with infinite scroll.
 * Rows are real links, so Ctrl/middle-click opens a case in a new tab and Back returns to the same list and position
 * (the pages stay cached; Layout restores the scroll).
 */
export function CaseListPage() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
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

  const userTabs = tabs.data?.tabs ?? []
  const requested = get('tab')
  const tab = userTabs.some((x) => x.tab === requested) ? requested : (tabs.data?.defaultTab ?? '')

  const search = get('q')
  const q = useDebounced(serverQuery(search), 300)
  const filters: CaseListFilters = {
    tab,
    q,
    status: get('status'),
    courtId: get('court'),
    categoryCode: get('category'),
    priority: get('priority'),
    suspended: get('suspended'),
    hearingFrom: get('from'),
    hearingTo: get('to'),
    // TODO(task 023): lawyer filter (lawyerId = user public id) with the lawyer picker.
  }
  const list = useCaseList(filters, tab !== '')
  const items = list.data?.pages.flatMap((p) => p.items) ?? []
  const anyFilter = FILTER_PARAMS.some((name) => get(name) !== '')
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
        <Stack direction="row" spacing={2}>
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
        >
          {userTabs.map((x) => (
            <Tab key={x.tab} value={x.tab} label={`${t.tabs[x.tab] ?? x.tab} (${x.count})`} />
          ))}
        </Tabs>
      )}

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ flexWrap: 'wrap', alignItems: { md: 'flex-start' } }}>
        <TextField
          label={t.search}
          value={search}
          onChange={(e) => set({ q: e.target.value })}
          slotProps={{ htmlInput: { maxLength: 100 } }}
          sx={{ flex: 2, minWidth: 260 }}
        />
        <TextField
          select
          label={t.status}
          value={get('status')}
          onChange={(e) => set({ status: e.target.value })}
          sx={{ minWidth: 150 }}
        >
          <MenuItem value="">{t.open}</MenuItem>
          <MenuItem value="CLOSED">{t.closed}</MenuItem>
        </TextField>
        <Box sx={{ flex: 1, minWidth: 240 }}>
          <CourtPicker
            courts={courts.data ?? []}
            levels={levels.data ?? []}
            levelCode=""
            value={get('court')}
            onChange={(court) => set({ court: court ? String(court.id) : '' })}
            label={t.court}
          />
        </Box>
        <TextField
          select
          label={t.category}
          value={get('category')}
          onChange={(e) => set({ category: e.target.value })}
          sx={{ minWidth: 150 }}
        >
          <MenuItem value="">{t.any}</MenuItem>
          {(categories.data ?? []).map((c) => (
            <MenuItem key={c.code} value={c.code}>
              {c.name}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          label={t.priority}
          value={get('priority')}
          onChange={(e) => set({ priority: e.target.value })}
          sx={{ minWidth: 150 }}
        >
          <MenuItem value="">{t.any}</MenuItem>
          {Object.entries(sq.cases.priority).map(([code, name]) => (
            <MenuItem key={code} value={code}>
              {name}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          label={t.suspended}
          value={get('suspended')}
          onChange={(e) => set({ suspended: e.target.value })}
          sx={{ minWidth: 130 }}
        >
          <MenuItem value="">{t.any}</MenuItem>
          <MenuItem value="true">{t.yes}</MenuItem>
          <MenuItem value="false">{t.no}</MenuItem>
        </TextField>
        <DateFilter
          label={t.hearingFrom}
          value={get('from')}
          maxDate={get('to')}
          onChange={(from) => set({ from })}
        />
        <DateFilter label={t.hearingTo} value={get('to')} minDate={get('from')} onChange={(to) => set({ to })} />
        {anyFilter && (
          <Button onClick={clearFilters} sx={{ alignSelf: 'center' }}>
            {t.clearFilters}
          </Button>
        )}
      </Stack>

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
          <Box sx={{ ...GRID, ...DESKTOP_ONLY, color: 'text.secondary', fontWeight: 600 }} aria-hidden>
            <Box>{t.columns.code}</Box>
            <Box>{t.columns.actNumber}</Box>
            <Box>{t.columns.court}</Box>
            <Box>{t.columns.lawyer}</Box>
            <Box>{t.columns.nextHearing}</Box>
            <Box>{t.columns.priority}</Box>
            <Box>{t.columns.status}</Box>
            <Box />
          </Box>
          <Box component="ul" aria-label={t.tabs[tab] ?? sq.cases.title} sx={{ listStyle: 'none', m: 0, p: 0 }}>
            {items.map((item) => (
              <CaseRow key={item.publicId} item={item} />
            ))}
          </Box>
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
function CaseRow({ item }: { item: CaseListItem }) {
  const today = todayInTirane()
  const hearingDay = tiraneDateOf(item.nextHearingAt)
  const when = hearingDay === today ? t.today : hearingDay === addDays(today, 1) ? t.tomorrow : null
  const hearing = item.nextHearingAt ? formatDateTime(item.nextHearingAt) : t.none
  const status = sq.cases.status[item.status] ?? item.status
  const priority = sq.cases.priority[item.priority] ?? item.priority
  const label = [
    item.caseCode,
    `${t.columns.actNumber} ${item.actNumber}`,
    item.courtName,
    item.primaryLawyerName ?? '',
    `${t.columns.nextHearing} ${when ? `${when}, ` : ''}${hearing}`,
    `${t.columns.priority} ${priority}`,
    status,
    item.isSuspended ? t.suspendedTooltip : '',
  ]
    .filter(Boolean)
    .join(', ')

  return (
    <Box component="li" sx={{ borderTop: 1, borderColor: 'divider' }}>
      <Box
        component={Link}
        to={`/cases/${item.publicId}`}
        aria-label={label}
        sx={{
          ...GRID,
          color: 'inherit',
          textDecoration: 'none',
          '&:hover': { bgcolor: 'action.hover' },
          '&:focus-visible': { outline: 2, outlineColor: 'primary.main', outlineOffset: -2 },
        }}
      >
        <Box sx={{ fontFamily: 'monospace', fontWeight: 600 }}>{item.caseCode}</Box>
        <Box sx={DESKTOP_ONLY}>{item.actNumber}</Box>
        <Box>{item.courtName}</Box>
        <Box sx={DESKTOP_ONLY}>{item.primaryLawyerName ?? t.none}</Box>
        <Box sx={when ? { fontWeight: 700, color: hearingDay === today ? 'error.main' : 'warning.dark' } : undefined}>
          {when ? `${when} ${hearing.slice(11)}` : hearing}
        </Box>
        <Box sx={DESKTOP_ONLY}>
          <Chip
            size="small"
            label={priority}
            color={item.priority === 'HIGH' ? 'error' : item.priority === 'MEDIUM' ? 'warning' : 'default'}
            variant={item.priority === 'NONE' ? 'outlined' : 'filled'}
          />
        </Box>
        <Box>
          <Chip
            size="small"
            label={status}
            color={item.status === 'ACTIVE' ? 'success' : item.status === 'REGISTERED' ? 'info' : 'default'}
          />
        </Box>
        <Box>
          {item.isSuspended && (
            <Tooltip title={t.suspendedTooltip}>
              <Box component="span" aria-hidden sx={{ fontSize: 18 }}>
                ⏸
              </Box>
            </Tooltip>
          )}
        </Box>
      </Box>
    </Box>
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
    <Box sx={{ minWidth: 170 }}>
      <AppDatePicker
        label={label}
        value={local}
        onChange={change}
        minDate={minDate ? dayjs(minDate) : undefined}
        maxDate={maxDate ? dayjs(maxDate) : undefined}
      />
    </Box>
  )
}
