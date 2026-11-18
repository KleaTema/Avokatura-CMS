import {
  Alert,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { matchesFilters } from '../../../components/filters/clientFilter'
import { textOf } from '../../../components/filters/filterTypes'
import { TableFilterBar } from '../../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../../components/filters/useUrlFilters'
import { SearchEmptyRow } from '../../../components/SearchEmptyRow'
import { PaginationFooter } from '../../../components/ui/DataTable'
import {
  useSetSimpleItemActive,
  useSimpleList,
  type SimpleListItem,
  type SimpleListKey,
} from '../../../features/admin/api'
import { useTableFilters } from '../../../features/filters/api'
import { sq } from '../../../i18n/sq'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { useClientPaging } from '../../../lib/paging'
import { ForbiddenPage } from '../../ErrorPages'
import { StatusChip } from './common'
import { rowSx, useForbiddenRedirect } from './helpers'
import { SimpleItemDialog } from './SimpleItemDialog'
import { groupLabels, simpleListConfig } from './simpleLists'

const t = sq.adminLists

export function SimpleListPanel({ list }: { list: SimpleListKey }) {
  const config = simpleListConfig[list]
  const rows = useSimpleList(list)
  const setActive = useSetSimpleItemActive(list)
  const redirectIfForbidden = useForbiddenRedirect()
  // null = closed; { item: null } = create; { item } = edit
  const [editing, setEditing] = useState<{ item: SimpleListItem | null } | null>(null)
  // task 053m: the shared FilterBar (GET /api/filters/lists?list=…), kept in the URL, applied in the browser
  const meta = useTableFilters('lists', { list })
  const url = useUrlFilters(meta.data ?? [])
  const q = textOf(url.values, 'q')
  const field = (row: SimpleListItem, key: string): unknown => {
    const fields: Record<string, unknown> = {
      q: [row.code, row.name],
      active: row.isActive,
      group: row.reportGroup ?? row.outcomeGroup ?? '',
    }
    return fields[key]
  }

  const all = rows.data ?? []
  const filtered = all.filter((row) => matchesFilters(row, url.filters, url.values, field))
  const paging = useClientPaging(filtered)
  if (isForbidden(rows.error)) return <ForbiddenPage />
  const visible = paging.items
  const title = t.lists[list]
  const groupHeader = config.group === 'report' ? t.columns.reportGroup : t.columns.outcomeGroup
  const columnCount =
    4 + (config.sortOrder ? 1 : 0) + (config.isOther ? 1 : 0) + (config.group ? 1 : 0) + (config.group === 'outcome' ? 1 : 0)

  const toggle = (item: SimpleListItem) =>
    setActive.mutate({ code: item.code, active: !item.isActive }, { onError: redirectIfForbidden })

  return (
    <>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h2">{title}</Typography>
        <Button variant="contained" color="secondary" onClick={() => setEditing({ item: null })}>
          {t.add}
        </Button>
      </Stack>

      <TableFilterBar meta={meta} url={url} />

      {rows.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(rows.error)}
        </Alert>
      )}
      {setActive.isError && !isForbidden(setActive.error) && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setActive.reset()}>
          {problemMessage(setActive.error)}
        </Alert>
      )}

      {rows.isPending ? (
        <CircularProgress aria-label={sq.errors.loading} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={title}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.code}</TableCell>
                <TableCell>{t.columns.name}</TableCell>
                {config.sortOrder && <TableCell align="right">{t.columns.sortOrder}</TableCell>}
                {config.isOther && <TableCell>{t.columns.isOther}</TableCell>}
                {config.group && <TableCell>{groupHeader}</TableCell>}
                {config.group === 'outcome' && <TableCell>{t.columns.sendsBackForRetrial}</TableCell>}
                {list === 'party-roles' && <TableCell>{t.columns.copyToNextLevel}</TableCell>}
                <TableCell>{t.columns.status}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {visible.map((row) => {
                const groupCode = row.reportGroup ?? row.outcomeGroup ?? ''
                return (
                  <TableRow key={row.code} hover sx={rowSx(row.isActive)}>
                    <TableCell>{row.code}</TableCell>
                    <TableCell>{row.name}</TableCell>
                    {config.sortOrder && <TableCell align="right">{row.sortOrder}</TableCell>}
                    {config.isOther && <TableCell>{row.isOther ? t.yes : t.no}</TableCell>}
                    {config.group && <TableCell>{groupLabels(config.group)[groupCode] ?? groupCode}</TableCell>}
                    {config.group === 'outcome' && <TableCell>{row.sendsBackForRetrial ? t.yes : t.no}</TableCell>}
                    {list === 'party-roles' && <TableCell>{row.copyToNextLevel ? t.yes : t.no}</TableCell>}
                    <TableCell>
                      <StatusChip active={row.isActive} />
                    </TableCell>
                    <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                      <Button size="small" onClick={() => setEditing({ item: row })} aria-label={`${t.edit}: ${row.name}`}>
                        {t.edit}
                      </Button>
                      <Button
                        size="small"
                        color={row.isActive ? 'error' : 'primary'}
                        onClick={() => toggle(row)}
                        disabled={setActive.isPending}
                        aria-label={`${row.isActive ? t.deactivate : t.activate}: ${row.name}`}
                      >
                        {row.isActive ? t.deactivate : t.activate}
                      </Button>
                    </TableCell>
                  </TableRow>
                )
              })}
              {rows.isSuccess && all.length === 0 && (
                <TableRow>
                  <TableCell colSpan={columnCount}>{t.empty}</TableCell>
                </TableRow>
              )}
              {rows.isSuccess && all.length > 0 && visible.length === 0 && (
                <SearchEmptyRow q={q} colSpan={columnCount} onClear={() => url.setKey('q', '')} />
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      <PaginationFooter mode="numbered" paging={paging} />

      {editing && <SimpleItemDialog list={list} item={editing.item} onClose={() => setEditing(null)} />}
    </>
  )
}
