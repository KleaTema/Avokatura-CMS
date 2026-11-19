import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
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
  Tooltip,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { CirclePause, UserPlus } from 'lucide-react'
import { AssignLawyerDialog } from '../../components/AssignLawyerDialog'
import { FilterBar } from '../../components/filters/FilterBar'
import { hasValue, rangeOf, textOf, yesNoOf } from '../../components/filters/filterTypes'
import { UnusedFilterGuard } from '../../components/filters/UnusedFilterGuard'
import { useUrlFilters } from '../../components/filters/useUrlFilters'
import { CaseCode } from '../../components/ui/CaseCode'
import { PaginationFooter } from '../../components/ui/DataTable'
import { PageHeader } from '../../components/ui/PageHeader'
import { StatusChip } from '../../components/ui/StatusChip'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { useCaseList, useCaseTabs, type CaseListFilters, type CaseListItem } from '../../features/cases/api'
import { useTableFilters } from '../../features/filters/api'
import { useMe } from '../../features/identity/api'
import { sq } from '../../i18n/sq'
import { addDays, formatDateTime, tiraneDateOf, todayInTirane } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { problemMessage } from '../../lib/problem'
import { useLiveSearch } from '../../lib/useLiveSearch'

const t = sq.cases.list

/** URL parameters of the list the page itself sets (the filters' own come from their metadata, task 053m). */
type Param = 'tab' | 'filter'

/**
 * Cases waiting for a lawyer have no lawyer, category, priority or suspension yet: those COLUMNS are hidden there.
 * Every filter stays available ("+ Shto filtër", task 053m).
 */
const TABS_WITHOUT_CLASSIFICATION = new Set(['registered', 'unassigned'])

const HIDE_ON_PHONE = { display: { xs: 'none', md: 'table-cell' } } as const
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
  const [assigning, setAssigning] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const userTabs = tabs.data?.tabs ?? []
  const requested = get('tab')
  const tab = userTabs.some((x) => x.tab === requested) ? requested : (tabs.data?.defaultTab ?? '')
  const classification = !TABS_WITHOUT_CLASSIFICATION.has(tab)

  // task 053m: the filters of the list — its metadata (one per column, options in the tab's scope), kept in the URL
  const meta = useTableFilters('cases', tab ? { tab } : {})
  const filterDefs = meta.data ?? []
  const url = useUrlFilters(filterDefs)
  const text = (key: string) => textOf(url.values, key)
  const range = (key: string, side: 'from' | 'to') => rangeOf(url.values, key, side)

  // live search: from 1 character, 300 ms after the last keystroke; '' at once when cleared
  const search = text('q')
  const q = useLiveSearch(search)
  const code = useLiveSearch(text('code'))
  const actNumber = useLiveSearch(text('actNumber'))
  const filters: CaseListFilters = {
    tab,
    q,
    status: text('status'),
    courtLevel: text('level'),
    courtId: text('court'),
    judgeId: text('judge'),
    categoryCode: text('category'),
    priority: text('priority'),
    suspended: yesNoOf(url.values, 'suspended'),
    hearingFrom: range('hearing', 'from'),
    hearingTo: range('hearing', 'to'),
    registeredFrom: range('registered', 'from'),
    registeredTo: range('registered', 'to'),
    lawyerId: text('lawyer'),
    officeId: text('office'),
    institutionId: text('institution'),
    filter: get('filter'),
    code,
    actNumber,
  }
  const paging = useKeysetPaging()
  const list = useCaseList(filters, { cursor: paging.cursor, size: paging.size }, tab !== '')
  const items = list.data?.items ?? []
  // the button only for who may assign; the API checks each case again
  const canAssign = tab === 'unassigned' && permissions.includes('case.assign_lawyer')
  const columns = COLUMNS.filter((c) => classification || !c.classification).map((c) =>
    canAssign && c.key === 'lawyer' ? ASSIGN_COLUMN : c,
  )
  // any filter in use (the search, a column filter or a dashboard card's filter)
  const anyFilter = filterDefs.some((f) => hasValue(url.values[f.key])) || filters.filter !== ''
  const clearFilters = () => {
    url.clear()
    set({ filter: '' })
  }

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

      {/* task 053m: the main filters (search, status, court), "+ Shto filtër" for every other column */}
      {meta.isError && <Alert severity="error">{problemMessage(meta.error)}</Alert>}
      <FilterBar
        filters={filterDefs}
        values={url.values}
        added={url.added}
        onChange={url.set}
        onAdd={url.add}
        onRemove={url.remove}
        busy={list.isFetching}
        after={
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', minHeight: 40 }}>
            {/* a dashboard card's filter (task 046): not a column — a chip, removable */}
            {filters.filter && (
              <Chip
                label={`${t.special}: ${t.specialFilters[filters.filter] ?? filters.filter}`}
                onDelete={() => set({ filter: '' })}
              />
            )}
            {anyFilter && (
              <Button size="small" onClick={clearFilters}>
                {t.clearFilters}
              </Button>
            )}
          </Stack>
        }
      />
      <UnusedFilterGuard unused={url.unused} />

      {list.isError && <Alert severity="error">{problemMessage(list.error)}</Alert>}
      {(tabs.isPending || (tab !== '' && list.isPending)) && <CircularProgress aria-label={sq.errors.loading} />}

      {list.isSuccess && items.length === 0 && (
        <Box sx={{ py: 2 }}>
          {search.trim() !== '' ? (
            <Typography>
              {sq.search.noResults(search.trim())}{' '}
              <Button size="small" onClick={() => url.setKey('q', '')}>
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

