import { Alert, Box, Button, Link, Paper, Skeleton, Stack, TableCell, TableRow, Tooltip, Typography } from '@mui/material'
import {
  TriangleAlert,
  CalendarClock,
  CalendarDays,
  CircleCheck,
  ClipboardList,
  FileWarning,
  FolderOpen,
  Gavel,
  Landmark,
  Share2,
  UserX,
  type LucideIcon,
} from 'lucide-react'
import { Link as RouterLink, useNavigate } from 'react-router'
import { Bar, BarChart, CartesianGrid, LabelList, Legend, ResponsiveContainer, Tooltip as ChartTooltip, XAxis, YAxis } from 'recharts'
import { num, type DashboardItem, type DashboardWidget } from '../../features/dashboard/api'
import { sq } from '../../i18n/sq'
import { isAppLink, listShapeOf, toneOf } from '../../lib/dashboard'
import { formatDateTime, formatRelative } from '../../lib/dates'
import { tokens } from '../../theme/tokens'
import { DataTable, type DataColumn } from '../ui/DataTable'
import { EmptyState } from '../ui/EmptyState'
import { KpiCard } from '../ui/KpiCard'
import { SectionCard } from '../ui/SectionCard'
import { StatusChip } from '../ui/StatusChip'

const t = sq.dashboard

/** The title of a widget: the app's own text for its code, the API's as a fallback. */
const titleOf = (w: DashboardWidget) => t.widgets[w.code] ?? w.title

const ICONS: Record<string, LucideIcon> = {
  registeredToday: ClipboardList,
  registeredWaiting: CalendarClock,
  unassigned: UserX,
  officeMissingData30: TriangleAlert,
  officeHearingOverdue: CalendarClock,
  officeDecisionPending: Gavel,
  mineOpen: FolderOpen,
  mineHearingOverdue: CalendarClock,
  mineMissingData30: TriangleAlert,
  mineDecisionPending: Gavel,
  institutionLinked: Landmark,
  institutionDelegated: Share2,
  institutionNoJurist: UserX,
  allHearingsThisWeek: CalendarDays,
  allMissingData6m: TriangleAlert,
  allDecisionPending: Gavel,
  adminFailedScans: FileWarning,
}

/** A widget that failed: a small inline error with "Provo përsëri" — never a broken page. */
export function WidgetError({ title, onRetry }: { title: string; onRetry: () => void }) {
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {title}
      </Typography>
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={onRetry}>
            {t.retry}
          </Button>
        }
      >
        {t.failed}
      </Alert>
    </Paper>
  )
}

/** A count card: tinted by meaning; the whole card opens the list it counts. */
export function CountCard({ widget, onRetry }: { widget: DashboardWidget; onRetry: () => void }) {
  const title = titleOf(widget)
  if (widget.error) return <WidgetError title={title} onRetry={onRetry} />
  const value = num(widget.value) ?? 0
  const secondary = num(widget.secondary)
  const card = (
    <KpiCard
      tone={toneOf(widget.code, value)}
      icon={ICONS[widget.code] ?? CircleCheck}
      title={title}
      value={value.toLocaleString('sq-AL')}
      note={widget.code === 'unassigned' && secondary !== null && value > 0 ? t.oldestWaiting(secondary) : undefined}
    />
  )
  if (!isAppLink(widget.linkPath)) return card
  return (
    <Box
      component={RouterLink}
      to={widget.linkPath}
      aria-label={t.openList(title, value.toLocaleString('sq-AL'))}
      sx={{
        display: 'block',
        textDecoration: 'none',
        color: 'inherit',
        borderRadius: `${tokens.radius.card}px`,
        '& > .MuiPaper-root': { transition: 'box-shadow .15s' },
        '&:hover > .MuiPaper-root, &:focus-visible > .MuiPaper-root': { boxShadow: 3 },
        '&:focus-visible': { outline: `2px solid ${tokens.color.primary}`, outlineOffset: 2 },
      }}
    >
      {card}
    </Box>
  )
}

export function CountCardSkeleton() {
  return (
    <Paper variant="outlined" sx={{ p: 2.5 }}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Skeleton variant="circular" width={48} height={48} />
        <Box sx={{ flex: 1 }}>
          <Skeleton variant="text" width="70%" />
          <Skeleton variant="text" width="40%" height={40} />
        </Box>
      </Stack>
    </Paper>
  )
}

// ---------------------------------------------------------------- lists

const COLUMNS: Record<ReturnType<typeof listShapeOf>, DataColumn[]> = {
  // the court under the case code: two columns fit a phone
  hearings: [
    { key: 'when', label: t.columns.when, width: 150 },
    { key: 'case', label: `${t.columns.case} · ${t.columns.court}` },
  ],
  registrations: [
    { key: 'case', label: t.columns.case },
    { key: 'registered', label: t.columns.registered, width: 170 },
    { key: 'state', label: t.columns.state, width: 140 },
  ],
  activity: [
    { key: 'what', label: t.columns.what },
    { key: 'case', label: t.columns.case, width: 180 },
    { key: 'when', label: t.columns.when, width: 150 },
  ],
  jobs: [
    { key: 'job', label: t.columns.job },
    { key: 'last', label: t.columns.lastSuccess, width: 170 },
    { key: 'state', label: t.columns.state, width: 140 },
  ],
  partitions: [
    { key: 'table', label: t.columns.table },
    { key: 'state', label: t.columns.state },
  ],
}

/** The case code as a blue link to the row's page (lists of cases and hearings). */
function CaseLink({ item }: { item: DashboardItem }) {
  const text = item.caseCode ?? item.label
  return isAppLink(item.linkPath) ? (
    <Link component={RouterLink} to={item.linkPath} underline="hover" sx={{ fontWeight: 600 }}>
      {text}
    </Link>
  ) : (
    <>{text}</>
  )
}

function ListRow({ shape, item }: { shape: ReturnType<typeof listShapeOf>; item: DashboardItem }) {
  const navigate = useNavigate()
  const link = isAppLink(item.linkPath) ? item.linkPath : null
  // the whole row opens it (the code in it is the keyboard / screen-reader link)
  const rowProps = link ? { hover: true, onClick: () => navigate(link), sx: { cursor: 'pointer' } } : {}
  switch (shape) {
    case 'hearings':
      return (
        <TableRow {...rowProps}>
          <TableCell sx={{ verticalAlign: 'top', whiteSpace: 'nowrap' }}>{formatDateTime(item.at)}</TableCell>
          <TableCell>
            <CaseLink item={item} />
            {item.courtName && (
              <Typography variant="caption" color="text.secondary" component="p">
                {item.courtName}
              </Typography>
            )}
          </TableCell>
        </TableRow>
      )
    case 'registrations':
      return (
        <TableRow {...rowProps}>
          <TableCell>
            <CaseLink item={item} />
          </TableCell>
          <TableCell>{formatDateTime(item.at)}</TableCell>
          <TableCell>{item.status && <StatusChip kind="status" value={item.status} />}</TableCell>
        </TableRow>
      )
    case 'activity':
      return (
        <TableRow {...rowProps}>
          <TableCell>{item.label}</TableCell>
          <TableCell>
            <CaseLink item={{ ...item, label: item.caseCode ?? '' }} />
          </TableCell>
          <TableCell>
            <Tooltip title={formatDateTime(item.at)}>
              <Typography variant="caption" color="text.secondary" component="span" tabIndex={0}>
                {formatRelative(item.at)}
              </Typography>
            </Tooltip>
          </TableCell>
        </TableRow>
      )
    case 'jobs': {
      const late = num(item.value) === 1
      return (
        <TableRow>
          <TableCell>
            {item.label}
            {item.detail && (
              <Typography variant="caption" color="error" component="p">
                {item.detail}
              </Typography>
            )}
          </TableCell>
          <TableCell>{item.at ? formatDateTime(item.at) : t.never}</TableCell>
          <TableCell>
            <Typography variant="body2" sx={{ color: late ? 'error.main' : 'success.dark', fontWeight: 600 }}>
              {late ? t.jobLate : t.jobOk}
            </Typography>
          </TableCell>
        </TableRow>
      )
    }
    case 'partitions':
      return (
        <TableRow>
          <TableCell>{item.label}</TableCell>
          <TableCell>
            <Typography variant="body2" sx={{ color: num(item.value) === 1 ? 'error.main' : 'success.dark', fontWeight: 600 }}>
              {t.partition[item.detail ?? ''] ?? item.detail}
            </Typography>
          </TableCell>
        </TableRow>
      )
  }
}

/** A list widget: up to 8 rows in a DataTable, "Shiko të gjitha →" to the full list. */
export function ListWidget({ widget, onRetry }: { widget: DashboardWidget; onRetry: () => void }) {
  const title = titleOf(widget)
  if (widget.error) return <WidgetError title={title} onRetry={onRetry} />
  const shape = listShapeOf(widget.code)
  const items = widget.items ?? []
  const value = num(widget.value)
  return (
    <SectionCard
      title={value === null ? title : `${title} (${value.toLocaleString('sq-AL')})`}
      viewAllTo={isAppLink(widget.linkPath) ? widget.linkPath : undefined}
      padded={false}
    >
      <DataTable label={title} columns={COLUMNS[shape]} empty={items.length === 0 && <EmptyState title={t.empty} />}>
        {items.map((item, i) => (
          <ListRow key={`${item.linkPath ?? item.label}-${i}`} shape={shape} item={item} />
        ))}
      </DataTable>
    </SectionCard>
  )
}

// ---------------------------------------------------------------- charts

interface Bar {
  label: string
  value: number
  value2: number
  link: string | null
  link2: string | null
}

/** Height per bar row: enough for the label and the value labels at the bar ends. */
const ROW = 36

/**
 * A chart widget: horizontal stacked bars, one row per lawyer / office, two series (blue, light blue — validated
 * for colour-blind readers; the light one needs its value printed, so every segment carries its number). Legend
 * always visible. The row label is a keyboard link to the list; each segment opens its own list; a hidden table
 * gives screen readers the same numbers.
 */
export function ChartWidget({ widget, onRetry }: { widget: DashboardWidget; onRetry: () => void }) {
  const navigate = useNavigate()
  const title = titleOf(widget)
  if (widget.error) return <WidgetError title={title} onRetry={onRetry} />
  const byOffice = widget.code === 'allByOffice'
  const [first, second] = byOffice ? [t.open, t.closed] : [t.primary, t.secondary]
  const bars: Bar[] = (widget.items ?? []).map((i) => ({
    label: i.label,
    value: num(i.value) ?? 0,
    value2: num(i.value2) ?? 0,
    link: isAppLink(i.linkPath) ? i.linkPath : null,
    link2: isAppLink(i.link2Path) ? i.link2Path : isAppLink(i.linkPath) ? i.linkPath : null,
  }))
  const go = (link: string | null) => link && navigate(link)
  const ink = tokens.color.textPrimary
  const muted = tokens.color.textSecondary

  return (
    <SectionCard title={title} viewAllTo={isAppLink(widget.linkPath) ? widget.linkPath : undefined}>
      {bars.length === 0 ? (
        <EmptyState title={t.empty} />
      ) : (
        <>
          <Box sx={{ width: '100%', height: bars.length * ROW + 70 }} aria-hidden>
            <ResponsiveContainer>
              <BarChart data={bars} layout="vertical" margin={{ top: 4, right: 32, bottom: 4, left: 4 }} barCategoryGap={6}>
                <CartesianGrid horizontal={false} stroke={tokens.color.cardBorder} />
                <XAxis type="number" allowDecimals={false} tick={{ fill: muted, fontSize: 12 }} stroke={tokens.color.cardBorder} />
                <YAxis
                  type="category"
                  dataKey="label"
                  width={150}
                  tick={{ fill: ink, fontSize: 13 }}
                  stroke={tokens.color.cardBorder}
                />
                <ChartTooltip cursor={{ fill: tokens.color.tint.blue }} />
                {/* series order (value, value2), not alphabetical by label */}
                <Legend
                  verticalAlign="top"
                  align="left"
                  iconType="square"
                  itemSorter="dataKey"
                  wrapperStyle={{ paddingBottom: 8 }}
                  // the label in the text colour; the square beside it carries the series colour
                  formatter={(label: string) => <span style={{ color: ink }}>{label}</span>}
                />
                <Bar
                  dataKey="value"
                  name={first}
                  stackId="s"
                  fill={tokens.color.primary}
                  stroke={tokens.color.white}
                  strokeWidth={2}
                  isAnimationActive={false}
                  cursor="pointer"
                  onClick={(d: { payload?: Bar }) => go(d.payload?.link ?? null)}
                >
                  <LabelList dataKey="value" position="insideLeft" fill={tokens.color.white} fontSize={12} formatter={(v: unknown) => (Number(v) > 0 ? String(v) : '')} />
                </Bar>
                <Bar
                  dataKey="value2"
                  name={second}
                  stackId="s"
                  fill={tokens.color.primaryLight}
                  stroke={tokens.color.white}
                  strokeWidth={2}
                  radius={[0, 4, 4, 0]}
                  isAnimationActive={false}
                  cursor="pointer"
                  onClick={(d: { payload?: Bar }) => go(d.payload?.link2 ?? null)}
                >
                  <LabelList dataKey="value2" position="right" fill={ink} fontSize={12} formatter={(v: unknown) => (Number(v) > 0 ? String(v) : '')} />
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </Box>
          {/* the same numbers as links: keyboard and screen readers (the chart itself is hidden from them) */}
          <Box component="ul" aria-label={t.chartTable} sx={{ listStyle: 'none', p: 0, m: 0, mt: 1, display: 'flex', flexWrap: 'wrap', gap: 1.5 }}>
            {bars.map((b) => (
              <Box component="li" key={b.label} sx={{ typography: 'caption', color: 'text.secondary' }}>
                {b.link ? (
                  <Link component={RouterLink} to={b.link} underline="hover">
                    {b.label}
                  </Link>
                ) : (
                  b.label
                )}
                {`: ${first.toLowerCase()} ${b.value}, `}
                {b.link2 && b.link2 !== b.link ? (
                  <Link component={RouterLink} to={b.link2} underline="hover">
                    {`${second.toLowerCase()} ${b.value2}`}
                  </Link>
                ) : (
                  `${second.toLowerCase()} ${b.value2}`
                )}
              </Box>
            ))}
          </Box>
        </>
      )}
    </SectionCard>
  )
}

export function SectionSkeleton() {
  return (
    <Paper variant="outlined" sx={{ p: 2.5 }}>
      <Skeleton variant="text" width="40%" height={32} />
      {Array.from({ length: 4 }, (_, i) => (
        <Skeleton key={i} variant="text" />
      ))}
    </Paper>
  )
}
