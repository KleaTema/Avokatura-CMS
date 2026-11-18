import {
  Alert,
  Box,
  Chip,
  Drawer,
  IconButton,
  Link,
  Stack,
  TableCell,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { Lock, RefreshCw, TriangleAlert, X } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip as ChartTooltip, XAxis, YAxis } from 'recharts'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { stickyCellSx } from '../../components/ui/tableStyles'
import { EmptyState } from '../../components/ui/EmptyState'
import { SectionCard } from '../../components/ui/SectionCard'
import { StatusChip } from '../../components/ui/StatusChip'
import {
  decimalText,
  useDrillDown,
  type DrillCell,
  type StatisticsFilters,
  type StatisticsMeasure,
  type StatisticsResponse,
} from '../../features/reports/api'
import { sq } from '../../i18n/sq'
import { formatTiraneDate, todayInTirane } from '../../lib/dates'
import { formatDecimal } from '../../lib/money'
import { formatCount } from '../../lib/numbers'
import { problemMessage } from '../../lib/problem'
import { tokens } from '../../theme/tokens'

const t = sq.reports

/** A column header: the measure's name from the API, its definition (the API's, never written here) in a tooltip. */
function MeasureHeader({ measure }: { measure: StatisticsMeasure }) {
  return (
    <Tooltip title={measure.definition} placement="top" arrow>
      <Box component="span" tabIndex={0} sx={{ textDecoration: 'underline dotted', textUnderlineOffset: 3, cursor: 'help' }}>
        {measure.name}
      </Box>
    </Tooltip>
  )
}

/** A number of the table: 0 is plain text, anything above is a button that opens its cases. */
function NumberCell({ value, onOpen, label }: { value: number; onOpen: () => void; label: string }) {
  if (value <= 0) return <>{formatCount(value)}</>
  return (
    <Link component="button" type="button" onClick={onOpen} aria-label={label} sx={{ fontWeight: 'inherit', fontVariantNumeric: 'tabular-nums' }}>
      {formatCount(value)}
    </Link>
  )
}

/** The cells drilled into: what to show in the drawer's title and what to ask the API. */
export interface OpenCell {
  cell: DrillCell
  measureName: string
  where: string
  count: number
}

/** The badge of the year: frozen on a date, not frozen yet (the API's warning), or the current year. */
function YearBadge({ s, year }: { s: StatisticsResponse; year: number }) {
  if (s.frozen && s.frozenAt) return <Chip icon={<Lock size={16} />} color="primary" variant="outlined" label={t.frozen(formatTiraneDate(s.frozenAt))} />
  if (s.warning) return <Chip icon={<TriangleAlert size={16} />} color="warning" variant="outlined" label={t.notFrozen} />
  return year === Number(todayInTirane().slice(0, 4)) ? <Chip icon={<RefreshCw size={16} />} variant="outlined" label={t.currentYear} /> : null
}

export function StatisticsBody({ s, year, onOpen }: { s: StatisticsResponse; year: number; onOpen: (cell: OpenCell) => void }) {
  const currencies = [...new Set(s.money.map((m) => m.currency).filter((c): c is string => !!c))].sort()
  const moneyOf = (courtId: number | null | undefined, currency: string | null) =>
    s.money.find((m) => (m.courtId ?? null) === (courtId ?? null) && (m.currency ?? null) === currency)
  const m2Of = (courtId: number | null | undefined, field: 'preventedM2' | 'causedM2') =>
    s.money.filter((m) => (m.courtId ?? null) === (courtId ?? null)).reduce((sum, m) => sum + Number(m[field]), 0)
  const area = (n: number) => (n ? `${formatDecimal(n.toFixed(2))}` : '')
  const amount = (v: number | string | undefined) => {
    const text = decimalText(v)
    return text && Number(text) !== 0 ? formatDecimal(text) : ''
  }

  const columns = [
    { key: 'court', label: sq.reports.court, sticky: true },
    ...s.measures.map((m) => ({ key: m.code, label: <MeasureHeader measure={m} />, align: 'right' as const })),
    ...currencies.flatMap((c) => [
      { key: `p:${c}`, label: <MeasureHeader measure={{ ...s.damageMeasure, name: t.damagePrevented(c) }} />, align: 'right' as const },
      { key: `c:${c}`, label: <MeasureHeader measure={{ ...s.damageMeasure, name: t.damageCaused(c) }} />, align: 'right' as const },
    ]),
    { key: 'pm2', label: <MeasureHeader measure={{ ...s.damageMeasure, name: t.damagePreventedM2 }} />, align: 'right' as const },
    { key: 'cm2', label: <MeasureHeader measure={{ ...s.damageMeasure, name: t.damageCausedM2 }} />, align: 'right' as const },
  ]

  const row = (court: (typeof s.courts)[number], total: boolean): ReactNode => {
    const courtId = court.courtId === null || court.courtId === undefined ? undefined : Number(court.courtId)
    const where = total ? t.totalRow : court.court
    return (
      <TableRow key={courtId ?? 'total'} hover={!total}>
        <TableCell component="th" scope="row" sx={{ ...stickyCellSx, fontWeight: total ? 700 : 500, whiteSpace: 'nowrap' }}>
          {total ? t.totalRow : court.court}
        </TableCell>
        {s.measures.map((m) => {
          const value = Number(court.values[m.code] ?? 0)
          return (
            <TableCell key={m.code} align="right" sx={{ fontWeight: total ? 700 : undefined }}>
              <NumberCell
                value={value}
                label={t.openNumber(formatCount(value), m.name, where)}
                onOpen={() => onOpen({ cell: { measure: m.code, courtId }, measureName: m.name, where, count: value })}
              />
            </TableCell>
          )
        })}
        {currencies.flatMap((c) => [
          <TableCell key={`p:${c}`} align="right">{amount(moneyOf(courtId, c)?.preventedAmount)}</TableCell>,
          <TableCell key={`c:${c}`} align="right">{amount(moneyOf(courtId, c)?.causedAmount)}</TableCell>,
        ])}
        <TableCell align="right">{area(m2Of(courtId, 'preventedM2'))}</TableCell>
        <TableCell align="right">{area(m2Of(courtId, 'causedM2'))}</TableCell>
      </TableRow>
    )
  }

  const courts = s.courts.filter((c) => c.courtId !== null && c.courtId !== undefined)
  const total = s.courts.find((c) => c.courtId === null || c.courtId === undefined)

  return (
    <Stack spacing={2.5}>
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
        <Typography variant="h2" component="h2">
          {t.statsTable(Number(s.table), year)}
        </Typography>
        <YearBadge s={s} year={year} />
      </Stack>

      <DataTable
        label={t.statsTable(Number(s.table), year)}
        columns={columns}
        minWidth={1400}
        stickyHeader
        maxHeight="70vh"
        empty={courts.length === 0 ? <EmptyState title={t.empty} /> : undefined}
        summary={total ? row(total, true) : undefined}
      >
        {courts.map((c) => row(c, false))}
      </DataTable>

      <Box sx={{ display: 'grid', gap: 2.5, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, alignItems: 'start' }}>
        <SectionCard title={t.results} padded={false}>
          <DataTable
            label={t.results}
            columns={[
              { key: 'r', label: t.result },
              { key: 'a', label: t.resultAsh, align: 'right' },
              { key: 'd', label: t.resultDelegated, align: 'right' },
            ]}
            empty={s.results.length === 0 ? <EmptyState title={t.noResults} /> : undefined}
          >
            {s.results.map((r) => (
              <TableRow key={r.resultCode}>
                <TableCell>{r.result}</TableCell>
                {(['perfunduar_ash', 'perfunduar_deleguar'] as const).map((code) => {
                  const value = Number(code === 'perfunduar_ash' ? r.ash : r.deleguar)
                  const name = s.measures.find((m) => m.code === code)?.name ?? code
                  return (
                    <TableCell key={code} align="right">
                      <NumberCell
                        value={value}
                        label={t.openNumber(formatCount(value), name, r.result)}
                        onOpen={() => onOpen({ cell: { measure: code, resultCode: r.resultCode }, measureName: name, where: r.result, count: value })}
                      />
                    </TableCell>
                  )
                })}
              </TableRow>
            ))}
          </DataTable>
        </SectionCard>

        <SectionCard title={<MeasureHeader measure={{ ...s.inHandMeasure, name: t.inHand }} />} padded={false}>
          {s.inHand.length === 0 ? (
            <Box sx={{ p: 2 }}>
              <EmptyState title={t.noInHand} />
            </Box>
          ) : (
            <>
              <Box sx={{ width: '100%', height: s.inHand.length * 34 + 40, px: 2, pt: 2 }} aria-hidden className="no-print">
                <ResponsiveContainer>
                  <BarChart data={s.inHand.map((l) => ({ name: l.lawyer, value: Number(l.count) }))} layout="vertical" margin={{ left: 8, right: 24 }}>
                    <CartesianGrid horizontal={false} stroke={tokens.color.cardBorder} />
                    <XAxis type="number" allowDecimals={false} stroke={tokens.color.cardBorder} tick={{ fontSize: 12 }} />
                    <YAxis type="category" dataKey="name" width={150} stroke={tokens.color.cardBorder} tick={{ fontSize: 13 }} />
                    <ChartTooltip cursor={{ fill: tokens.color.tint.blue }} />
                    <Bar dataKey="value" name={t.inHandChart} fill={tokens.color.primary} radius={[0, 4, 4, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              </Box>
              <DataTable
                label={t.inHand}
                columns={[
                  { key: 'l', label: t.lawyer },
                  { key: 'c', label: s.inHandMeasure.name, align: 'right' },
                ]}
              >
                {s.inHand.map((l) => (
                  <TableRow key={l.lawyerId}>
                    <TableCell>{l.lawyer}</TableCell>
                    <TableCell align="right">
                      <NumberCell
                        value={Number(l.count)}
                        label={t.openNumber(formatCount(Number(l.count)), s.inHandMeasure.name, l.lawyer)}
                        onOpen={() =>
                          onOpen({
                            cell: { measure: 'ne_dore', rowLawyerId: l.lawyerId },
                            measureName: s.inHandMeasure.name,
                            where: l.lawyer,
                            count: Number(l.count),
                          })
                        }
                      />
                    </TableCell>
                  </TableRow>
                ))}
              </DataTable>
            </>
          )}
        </SectionCard>
      </Box>
    </Stack>
  )
}

/** The cases behind one number: title "{measure} — {court}", keyset pages of 10, each case a link. */
export function DrillDrawer({ filters, open, onClose }: { filters: StatisticsFilters; open: OpenCell | null; onClose: () => void }) {
  const [stack, setStack] = useState<string[]>([])
  const [forCell, setForCell] = useState<OpenCell | null>(null)
  // a new cell starts at its first page
  if (open !== forCell) {
    setForCell(open)
    setStack([])
  }

  const cursor = stack[stack.length - 1]
  const drill = useDrillDown(filters, open?.cell ?? null, cursor)
  const items = drill.data?.items ?? []
  const c = t.drillColumns
  const page = stack.length + 1

  return (
    <Drawer anchor="right" open={open !== null} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', md: 760 } } } }}>
      {open && (
        <Stack spacing={2} sx={{ p: 2.5 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
            <Box sx={{ flex: 1 }}>
              <Typography variant="h2" component="h2">
                {t.drillTitle(open.measureName, open.where)}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {t.drillCount(Number(drill.data?.total ?? open.count))}
              </Typography>
            </Box>
            <IconButton onClick={onClose} aria-label={t.drillClose}>
              <X size={22} aria-hidden />
            </IconButton>
          </Stack>
          {drill.isError && <Alert severity="error">{problemMessage(drill.error)}</Alert>}
          <DataTable
            label={t.drillTitle(open.measureName, open.where)}
            loading={drill.isPending}
            columns={[
              { key: 'code', label: c.code },
              { key: 'court', label: c.court },
              { key: 'lawyer', label: c.lawyer },
              { key: 'status', label: c.status },
              { key: 'registered', label: c.registered },
              { key: 'closed', label: c.closed },
            ]}
            footer={
              <PaginationFooter
                mode="keyset"
                paging={{
                  page,
                  size: 10,
                  cursor,
                  next: (nextCursor: string) => setStack((s) => [...s, nextCursor]),
                  previous: () => setStack((s) => s.slice(0, -1)),
                  setSize: () => undefined,
                }}
                count={items.length}
                total={Number(drill.data?.total ?? 0)}
                nextCursor={drill.data?.nextCursor}
                busy={drill.isFetching}
                stale={drill.isPlaceholderData}
              />
            }
          >
            {items.map((r) => (
              <TableRow key={r.casePublicId}>
                <TableCell>
                  {/* task 053h: a link only when the user can open the case; otherwise the code alone */}
                  {r.canOpen ? (
                    <Link component={RouterLink} to={`/cases/${r.casePublicId}`} sx={{ fontFamily: tokens.font.mono, fontWeight: 600 }}>
                      {r.caseCode}
                    </Link>
                  ) : (
                    <Box component="span" sx={{ fontFamily: tokens.font.mono, fontWeight: 600 }}>
                      {r.caseCode}
                    </Box>
                  )}
                </TableCell>
                <TableCell>{r.court}</TableCell>
                <TableCell>{r.lawyer ?? ''}</TableCell>
                <TableCell>
                  <StatusChip kind="status" value={r.status} />
                </TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatTiraneDate(r.registeredAt)}</TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{r.closedAt ? formatTiraneDate(r.closedAt) : ''}</TableCell>
              </TableRow>
            ))}
          </DataTable>
        </Stack>
      )}
    </Drawer>
  )
}

