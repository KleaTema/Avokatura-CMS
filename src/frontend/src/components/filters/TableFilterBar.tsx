import { Alert, Box } from '@mui/material'
import type { ReactNode } from 'react'
import { problemMessage } from '../../lib/problem'
import { FilterBar } from './FilterBar'
import { UnusedFilterGuard } from './UnusedFilterGuard'
import type { useUrlFilters } from './useUrlFilters'

interface Props {
  /** the table's metadata query (useTableFilters) */
  meta: { isError: boolean; error: unknown }
  /** the URL-kept values (useUrlFilters over the same metadata) */
  url: ReturnType<typeof useUrlFilters>
  before?: ReactNode
  after?: ReactNode
  /** the list is loading the rows of the current filters (live search: the search field's spinner) */
  busy?: boolean
}

/**
 * Task 053m — a list page's filter row: the metadata error, the shared FilterBar over the URL-kept values, and the
 * "unused filter" leave dialog.
 */
export function TableFilterBar({ meta, url, before, after, busy }: Props) {
  return (
    <Box sx={{ mb: 2 }}>
      {meta.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(meta.error)}
        </Alert>
      )}
      <FilterBar
        filters={url.filters}
        values={url.values}
        added={url.added}
        onChange={url.set}
        onAdd={url.add}
        onRemove={url.remove}
        before={before}
        after={after}
        busy={busy}
      />
      <UnusedFilterGuard unused={url.unused} />
    </Box>
  )
}
