import {
  Paper,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material'
import type { ReactNode } from 'react'

export interface DataColumn {
  key: string
  label: ReactNode
  align?: 'left' | 'right' | 'center'
  width?: number | string
}

const SKELETON_ROWS = 5

/**
 * A table in a card: the themed header row, the caller's rows, skeleton rows while `loading`, and `empty`
 * (e.g. an EmptyState) across all columns when there is nothing to show.
 */
export function DataTable({
  label,
  columns,
  loading,
  empty,
  children,
}: {
  /** accessible name of the table */
  label: string
  columns: DataColumn[]
  loading?: boolean
  /** shown instead of the rows; pass it only when there are no rows */
  empty?: ReactNode
  children?: ReactNode
}) {
  return (
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
  )
}
