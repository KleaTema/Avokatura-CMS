import {
  Alert,
  Box,
  Chip,
  Drawer,
  IconButton,
  Link,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableFooter,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { Info, Lock, RefreshCw, TriangleAlert, X } from 'lucide-react'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip as ChartTooltip, XAxis, YAxis } from 'recharts'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { stickyCellSx } from '../../components/ui/tableStyles'
import { EmptyState } from '../../components/ui/EmptyState'
import { SectionCard } from '../../components/ui/SectionCard'
import { StatusChip } from '../../components/ui/StatusChip'
import {
  useDrillDown,
  type DrillCell,
  type StatisticsFilters,
  type StatisticsGrid,
  type StatisticsLeaf,
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

/**
 * A header's definition (the API's, never written here): task 053o — a small info icon beside the name shows it in a
 * tooltip (no dotted underline); not printed.
 */
function Definition({ text, name }: { text: string | null | undefined; name: string }) {
  if (!text) return null
  return (
    <Tooltip title={text} placement="top" arrow>
      <Box
        component="span"
        tabIndex={0}
        role="img"
        aria-label={t.definitionOf(name)}
        className="no-print"
        sx={{ display: 'inline-flex', verticalAlign: 'middle', ml: 0.5, cursor: 'help', opacity: 0.85, '@media print': { display: 'none' } }}
      >
        <Info size={14} aria-hidden />
      </Box>
    </Tooltip>
  )
}

function MeasureHeader({ measure }: { measure: StatisticsMeasure }) {
  return (
    <>
      {measure.name}
      <Definition text={measure.definition} name={measure.name} />
    </>
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

/**
 * Tabela 3 / 4 (tasks 044, 053o): the client's table from the API's grid — the header rows with their merges (groups
 * centered over their columns), the letter and formula rows, a row per court and the TOTALI row (bold); damages per
 * currency, never summed across currencies. Every count opens its cases. Then "Rezultati" and "Në dorë". `print`: no
 * links, no chart; `sections`: which of the two extra sections go on paper (the print dialog).
 */
export function StatisticsBody({
  s,
  year,
  onOpen,
  print = false,
  sections,
}: {
  s: StatisticsResponse
  year: number
  onOpen: (cell: OpenCell) => void
  print?: boolean
  sections?: { results: boolean; inHand: boolean }
}) {
  const showResults = !print || (sections?.results ?? false)
  const showInHand = !print || (sections?.inHand ?? false)
  const title = s.grid.title

  return (
    <Stack spacing={2.5}>
      {!print && (
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
          <Typography variant="h2" component="h2">
            {title}
          </Typography>
          <YearBadge s={s} year={year} />
        </Stack>
      )}

      <StatisticsGridTable grid={s.grid} onOpen={onOpen} print={print} />

      {(showResults || showInHand) && (
      <Box sx={{ display: 'grid', gap: 2.5, gridTemplateColumns: { xs: '1fr', lg: print ? '1fr' : '1fr 1fr' }, alignItems: 'start' }}>
        {showResults && (
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
        )}

        {showInHand && (
        <SectionCard title={<MeasureHeader measure={{ ...s.inHandMeasure, name: t.inHand }} />} padded={false}>
          {s.inHand.length === 0 ? (
            <Box sx={{ p: 2 }}>
              <EmptyState title={t.noInHand} />
            </Box>
          ) : (
            <>
              {!print && (
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
              )}
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
        )}
      </Box>
      )}
    </Stack>
  )
}

const LEAF_COUNT = 'count'

/** A grid cell's number: counts as a whole number, m² / money with 2 decimals (money with its ISO code). */
function gridValue(leaf: StatisticsLeaf, value: number): string {
  if (leaf.kind === LEAF_COUNT) return formatCount(value)
  if (!value) return ''
  const text = formatDecimal(value.toFixed(2))
  return leaf.currency ? `${text} ${leaf.currency}` : text
}

/**
 * The client's table as the API's grid draws it: each header cell at its place with its colSpan / rowSpan (groups
 * centered), a leaf's definition behind its info icon, the letter row and the formula row, then the rows — the TOTALI
 * row bold with a strong top line, kept at the bottom while scrolling. A count opens its cases (not on paper).
 */
function StatisticsGridTable({ grid, onOpen, print }: { grid: StatisticsGrid; onOpen: (cell: OpenCell) => void; print: boolean }) {
  const leaves = grid.columns
  const courts = grid.rows.filter((r) => r.courtId !== null && r.courtId !== undefined)
  const total = grid.rows.find((r) => r.courtId === null || r.courtId === undefined)
  const leafOf = new Map(leaves.map((l) => [l.header, l]))
  const headCell = { textAlign: 'center' as const, whiteSpace: 'normal' as const, fontSize: 12, lineHeight: 1.25, px: 0.75, py: 0.5 }

  const row = (r: (typeof grid.rows)[number], isTotal: boolean) => {
    const courtId = isTotal ? undefined : Number(r.courtId)
    return (
      <TableRow key={courtId ?? 'total'} hover={!isTotal && !print}>
        <TableCell component="th" scope="row" sx={{ ...(print ? {} : stickyCellSx), bgcolor: 'inherit', fontWeight: isTotal ? 700 : 500, whiteSpace: 'nowrap' }}>
          {r.court}
        </TableCell>
        {leaves.map((leaf) => {
          const value = Number(r.values[leaf.key] ?? 0)
          const where = r.court
          const cell: DrillCell = {
            measure: leaf.measure ?? '',
            courtId,
            resultColumn: leaf.resultColumn ?? undefined,
            currency: leaf.currency ?? undefined,
          }
          return (
            <TableCell key={leaf.key} align="right" sx={{ fontWeight: isTotal ? 700 : undefined, fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
              {leaf.kind === LEAF_COUNT && !print && leaf.measure ? (
                <NumberCell
                  value={value}
                  label={t.openNumber(formatCount(value), leaf.header, where)}
                  onOpen={() => onOpen({ cell, measureName: leaf.header, where, count: value })}
                />
              ) : (
                gridValue(leaf, value)
              )}
            </TableCell>
          )
        })}
      </TableRow>
    )
  }

  return (
    <TableContainer
      component={Paper}
      variant="outlined"
      sx={print ? undefined : { overflowX: 'auto', maxHeight: '75vh' }}
    >
      <Table size="small" stickyHeader={!print} aria-label={grid.title} sx={{ minWidth: print ? undefined : 1800 }}>
        <TableHead>
          {grid.headerRows.map((cells, i) => (
            <TableRow key={i}>
              {cells.map((c, j) => {
                const colSpan = Number(c.colSpan)
                const rowSpan = Number(c.rowSpan)
                const leaf = rowSpan + i === grid.headerRows.length || (colSpan === 1 && leafOf.has(c.text)) ? leafOf.get(c.text) : undefined
                return (
                  <TableCell
                    key={`${i}-${j}`}
                    colSpan={colSpan > 1 ? colSpan : undefined}
                    rowSpan={rowSpan > 1 ? rowSpan : undefined}
                    sx={{ ...headCell, ...(i === 0 && j === 0 && !print ? { position: 'sticky', left: 0, zIndex: 4 } : {}) }}
                  >
                    {c.text}
                    <Definition text={c.definition ?? leaf?.definition} name={c.text} />
                  </TableCell>
                )
              })}
            </TableRow>
          ))}
          {/* the client's letter row and formula row */}
          <TableRow>
            <TableCell sx={headCell} />
            {leaves.map((l) => (
              <TableCell key={l.key} sx={{ ...headCell, fontWeight: 500 }}>
                {l.letter ?? ''}
              </TableCell>
            ))}
          </TableRow>
          {leaves.some((l) => l.formula) && (
            <TableRow>
              <TableCell sx={headCell} />
              {leaves.map((l) => (
                <TableCell key={l.key} sx={{ ...headCell, fontWeight: 500 }}>
                  {l.formula ?? ''}
                </TableCell>
              ))}
            </TableRow>
          )}
        </TableHead>
        <TableBody>
          {courts.length === 0 ? (
            <TableRow>
              <TableCell colSpan={leaves.length + 1}>
                <EmptyState title={t.empty} />
              </TableCell>
            </TableRow>
          ) : (
            courts.map((r) => row(r, false))
          )}
        </TableBody>
        {total && (
          <TableFooter
            sx={{
              position: print ? undefined : 'sticky',
              bottom: 0,
              zIndex: 2,
              '& td, & th': { bgcolor: 'background.paper', color: 'text.primary', fontSize: 14, borderTop: 2, borderTopColor: 'text.primary' },
            }}
          >
            {row(total, true)}
          </TableFooter>
        )}
      </Table>
    </TableContainer>
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

