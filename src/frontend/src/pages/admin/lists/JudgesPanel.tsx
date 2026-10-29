import {
  Alert,
  Box,
  Button,
  CircularProgress,
  MenuItem,
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
import { useAdminJudges, useSetJudgeActive, type AdminJudge } from '../../../features/admin/api'
import { sq } from '../../../i18n/sq'
import { SearchEmptyRow } from '../../../components/SearchEmptyRow'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { serverQuery } from '../../../lib/search'
import { useDebounced } from '../../../lib/useDebounced'
import { useUrlSearch } from '../../../lib/useUrlSearch'
import { ForbiddenPage } from '../../ErrorPages'
import { StatusChip } from './common'
import { rowSx, useForbiddenRedirect } from './helpers'
import { JudgeDialog } from './JudgeDialog'

const t = sq.adminLists

type ActiveFilter = 'all' | 'active' | 'inactive'
const activeParam: Record<ActiveFilter, boolean | undefined> = { all: undefined, active: true, inactive: false }

export function JudgesPanel() {
  const [search, setSearch] = useUrlSearch()
  const [filter, setFilter] = useState<ActiveFilter>('all')
  const q = useDebounced(serverQuery(search), 300)
  const judges = useAdminJudges(q, activeParam[filter])
  const setActive = useSetJudgeActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ judge: AdminJudge | null } | null>(null)

  if (isForbidden(judges.error)) return <ForbiddenPage />

  const rows = judges.data?.pages.flatMap((p) => p.items) ?? []
  const toggle = (judge: AdminJudge) =>
    setActive.mutate({ id: Number(judge.id), active: !judge.isActive }, { onError: redirectIfForbidden })

  return (
    <>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h5" component="h2">
          {t.lists.judges}
        </Typography>
        <Button variant="contained" onClick={() => setEditing({ judge: null })}>
          {t.add}
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
        <TextField
          label={t.search}
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          sx={{ flexGrow: 1 }}
        />
        <TextField
          select
          label={t.activeFilter.label}
          value={filter}
          onChange={(e) => setFilter(e.target.value as ActiveFilter)}
          sx={{ minWidth: 160 }}
        >
          <MenuItem value="all">{t.activeFilter.all}</MenuItem>
          <MenuItem value="active">{t.activeFilter.active}</MenuItem>
          <MenuItem value="inactive">{t.activeFilter.inactive}</MenuItem>
        </TextField>
      </Stack>

      {judges.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(judges.error)}
        </Alert>
      )}
      {setActive.isError && !isForbidden(setActive.error) && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setActive.reset()}>
          {problemMessage(setActive.error)}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table size="small" aria-label={t.lists.judges}>
          <TableHead>
            <TableRow>
              <TableCell>{t.columns.fullName}</TableCell>
              <TableCell>{t.columns.court}</TableCell>
              <TableCell>{t.columns.externalRef}</TableCell>
              <TableCell>{t.columns.status}</TableCell>
              <TableCell align="right">{sq.common.actions}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((judge) => {
              const fullName = `${judge.firstName} ${judge.lastName}`
              return (
                <TableRow key={judge.id} hover sx={rowSx(judge.isActive)}>
                  <TableCell>{fullName}</TableCell>
                  <TableCell>{judge.courtName ?? ''}</TableCell>
                  <TableCell>{judge.externalRef ?? ''}</TableCell>
                  <TableCell>
                    <StatusChip active={judge.isActive} />
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    <Button size="small" onClick={() => setEditing({ judge })} aria-label={`${t.edit}: ${fullName}`}>
                      {t.edit}
                    </Button>
                    <Button
                      size="small"
                      color={judge.isActive ? 'error' : 'primary'}
                      onClick={() => toggle(judge)}
                      disabled={setActive.isPending}
                      aria-label={`${judge.isActive ? t.deactivate : t.activate}: ${fullName}`}
                    >
                      {judge.isActive ? t.deactivate : t.activate}
                    </Button>
                  </TableCell>
                </TableRow>
              )
            })}
            {judges.isSuccess && rows.length === 0 && !q && (
              <TableRow>
                <TableCell colSpan={5}>{t.empty}</TableCell>
              </TableRow>
            )}
            {judges.isSuccess && rows.length === 0 && q && (
              <SearchEmptyRow q={search} colSpan={5} onClear={() => setSearch('')} />
            )}
          </TableBody>
        </Table>
      </TableContainer>

      <Box sx={{ mt: 2, display: 'flex', justifyContent: 'center' }}>
        {judges.isPending || judges.isFetchingNextPage ? (
          <CircularProgress aria-label={sq.errors.loading} />
        ) : (
          judges.hasNextPage && <Button onClick={() => void judges.fetchNextPage()}>{sq.common.loadMore}</Button>
        )}
      </Box>

      {editing && <JudgeDialog judge={editing.judge} onClose={() => setEditing(null)} />}
    </>
  )
}
