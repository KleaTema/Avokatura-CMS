import { Button, TableCell, TableRow } from '@mui/material'
import { sq } from '../i18n/sq'

/** Table row for "nothing found": the search text and a button that clears it. */
export function SearchEmptyRow({ q, colSpan, onClear }: { q: string; colSpan: number; onClear: () => void }) {
  return (
    <TableRow>
      <TableCell colSpan={colSpan}>
        {sq.search.noResults(q.trim())}{' '}
        <Button size="small" onClick={onClear}>
          {sq.search.clear}
        </Button>
      </TableCell>
    </TableRow>
  )
}
