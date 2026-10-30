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
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { SearchEmptyRow } from '../../../components/SearchEmptyRow'
import {
  useSetSimpleItemActive,
  useSimpleList,
  type SimpleListItem,
  type SimpleListKey,
} from '../../../features/admin/api'
import { sq } from '../../../i18n/sq'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { matches } from '../../../lib/search'
import { useUrlSearch } from '../../../lib/useUrlSearch'
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
  const [q, setQ] = useUrlSearch()

  if (isForbidden(rows.error)) return <ForbiddenPage />

  const all = rows.data ?? []
  const visible = all.filter((row) => matches(q, row.code, row.name))
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

      <TextField
        label={t.searchSimple}
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />

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
                <SearchEmptyRow q={q} colSpan={columnCount} onClear={() => setQ('')} />
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {editing && <SimpleItemDialog list={list} item={editing.item} onClose={() => setEditing(null)} />}
    </>
  )
}
