import {
  Box,
  Button,
  Pagination,
  Paper,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import type { ReactNode } from 'react'
import { sq } from '../../i18n/sq'
import { PAGE_SIZES, type ClientPaging, type KeysetPaging } from '../../lib/paging'
import { AppSelect } from './AppSelect'

export interface DataColumn {
  key: string
  label: ReactNode
  align?: 'left' | 'right' | 'center'
  width?: number | string
}

const SKELETON_ROWS = 5

/**
 * A table in a card: the themed header row, the caller's rows, skeleton rows while `loading`, and `empty`
 * (e.g. an EmptyState) across all columns when there is nothing to show; `footer` (a PaginationFooter) below it.
 */
export function DataTable({
  label,
  columns,
  loading,
  empty,
  footer,
  children,
}: {
  /** accessible name of the table */
  label: string
  columns: DataColumn[]
  loading?: boolean
  /** shown instead of the rows; pass it only when there are no rows */
  empty?: ReactNode
  footer?: ReactNode
  children?: ReactNode
}) {
  return (
    <Box>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small" aria-label={label}>
          <TableHead>
            <TableRow>
              {columns.map((c) => (
                <TableCell key={c.key} align={c.align} sx={c.width === undefined ? undefined : { width: c.width }}>
                  {c.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {loading
              ? Array.from({ length: SKELETON_ROWS }, (_, i) => (
                  <TableRow key={i}>
                    {columns.map((c) => (
                      <TableCell key={c.key}>
                        <Skeleton variant="text" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              : children}
            {!loading && empty && (
              <TableRow>
                <TableCell colSpan={columns.length}>{empty}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      {footer}
    </Box>
  )
}

type FooterProps =
  | {
      /** a server list: "‹ Para" / "Pas ›" over the keyset cursor */
      mode: 'keyset'
      paging: KeysetPaging
      /** rows on this page and the total of the filtered list (from the API) */
      count: number
      total: number
      nextCursor: string | null | undefined
      busy?: boolean
      /** the rows shown are still the previous page's (TanStack's placeholder data while the new page loads) */
      stale?: boolean
    }
  | {
      /** a list loaded at once: numbered pages 1 2 3 … */
      mode: 'numbered'
      paging: ClientPaging<unknown>
    }

/**
 * The footer under every table (both modes look the same): the range "11–20 nga 45", the page controls and the page
 * size (10 / 25 / 50). Nothing when the list is empty.
 */
export function PaginationFooter(props: FooterProps) {
  const { page, size } = props.paging
  const stale = props.mode === 'keyset' && props.stale
  const total = props.mode === 'keyset' ? props.total : props.paging.total
  const count = props.mode === 'keyset' ? props.count : props.paging.items.length
  if (total === 0 && count === 0) return null
  const from = (page - 1) * size + 1
  // the total is counted beside the page: under concurrent writes the page itself is the truth
  const to = from + count - 1
  const shownTotal = Math.max(total, to)

  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={{ xs: 1, sm: 2 }}
      useFlexGap
      sx={{ alignItems: { xs: 'stretch', sm: 'center' }, justifyContent: 'flex-end', pt: 1.5, flexWrap: 'wrap' }}
      component="nav"
      aria-label={sq.paging.label}
    >
      {/* while the next page loads the old rows stay visible; their range would be wrong, so it waits */}
      <Typography
        variant="body2"
        color="text.secondary"
        aria-live="polite"
        sx={{ mr: { sm: 'auto' }, visibility: stale ? 'hidden' : 'visible' }}
      >
        {sq.paging.range(count === 0 ? 0 : from, to, shownTotal)}
      </Typography>
      <AppSelect
        required
        hideRequiredMark
        size="small"
        label={sq.paging.pageSize}
        value={String(size)}
        onChange={(value) => props.paging.setSize(Number(value))}
        options={PAGE_SIZES.map((n) => ({ value: String(n), label: String(n) }))}
        sx={{ width: { xs: '100%', sm: 150 } }}
      />
      {props.mode === 'keyset' ? (
        <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
          <Button variant="outlined" size="small" onClick={props.paging.previous} disabled={page <= 1 || props.busy}>
            {sq.paging.previous}
          </Button>
          <Button
            variant="outlined"
            size="small"
            onClick={() => props.nextCursor && props.paging.next(props.nextCursor)}
            disabled={!props.nextCursor || props.busy}
          >
            {sq.paging.next}
          </Button>
        </Stack>
      ) : (
        <Pagination
          count={Math.max(1, Math.ceil(total / size))}
          page={page}
          onChange={(_, value) => props.paging.setPage(value)}
          size="small"
          color="primary"
          shape="rounded"
          getItemAriaLabel={(type, value) =>
            type === 'page' ? sq.paging.pageOf(value ?? 1) : type === 'previous' ? sq.paging.previous : sq.paging.next
          }
          sx={{ alignSelf: { xs: 'center', sm: 'auto' } }}
        />
      )}
    </Stack>
  )
}
