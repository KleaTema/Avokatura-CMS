import {
  Alert,
  Box,
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
import { OptionPicker } from '../../../components/OptionPicker'
import { AppSelect } from '../../../components/ui/AppSelect'
import { PaginationFooter } from '../../../components/ui/DataTable'
import { useAdminJudges, useSetJudgeActive, type AdminJudge } from '../../../features/admin/api'
import { useCourts } from '../../../features/reference/api'
import { sq } from '../../../i18n/sq'
import { SearchEmptyRow } from '../../../components/SearchEmptyRow'
import { useKeysetPaging } from '../../../lib/paging'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { serverQuery } from '../../../lib/search'
import { useDebounced } from '../../../lib/useDebounced'
import { useUrlSearch } from '../../../lib/useUrlSearch'
import { ForbiddenPage } from '../../ErrorPages'
import { StatusChip } from './common'
import { rowSx, useForbiddenRedirect } from './helpers'
import { JudgeDialog } from './JudgeDialog'

const t = sq.adminLists

/** Judges: search anywhere in the name (server), court and status filters, keyset pages of 10. */
export function JudgesPanel() {
  const [search, setSearch] = useUrlSearch()
  const [active, setActiveFilter] = useUrlSearch('active')
  const [courtId, setCourtId] = useUrlSearch('court')
  const q = useDebounced(serverQuery(search), 300)
  const paging = useKeysetPaging()
  const judges = useAdminJudges({ q, active, courtId }, { cursor: paging.cursor, size: paging.size })
  const courts = useCourts()
  const setActive = useSetJudgeActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ judge: AdminJudge | null } | null>(null)

  if (isForbidden(judges.error)) return <ForbiddenPage />

  const rows = judges.data?.items ?? []
  const toggle = (judge: AdminJudge) =>
    setActive.mutate({ id: Number(judge.id), active: !judge.isActive }, { onError: redirectIfForbidden })

  return (
    <>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h2">{t.lists.judges}</Typography>
        <Button variant="contained" color="secondary" onClick={() => setEditing({ judge: null })}>
          {t.add}
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap' }}>
        <TextField
          label={t.search}
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          sx={{ flexGrow: 1 }}
        />
        <Box sx={{ width: { xs: '100%', sm: 300 } }}>
          <OptionPicker
            filter
            label={t.columns.court}
            options={(courts.data ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
            value={courtId}
            onChange={setCourtId}
          />
        </Box>
        <AppSelect
          filter
          label={t.activeFilter.label}
          value={active}
          onChange={setActiveFilter}
          options={[
            { value: 'true', label: t.activeFilter.active },
            { value: 'false', label: t.activeFilter.inactive },
          ]}
          sx={{ minWidth: 160 }}
        />
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
      {judges.isPending && <CircularProgress aria-label={sq.errors.loading} sx={{ mt: 2 }} />}
      <PaginationFooter
        mode="keyset"
        paging={paging}
        count={rows.length}
        total={Number(judges.data?.total ?? 0)}
        nextCursor={judges.data?.nextCursor}
        busy={judges.isFetching}
        stale={judges.isPlaceholderData}
      />

      {editing && <JudgeDialog judge={editing.judge} onClose={() => setEditing(null)} />}
    </>
  )
}
