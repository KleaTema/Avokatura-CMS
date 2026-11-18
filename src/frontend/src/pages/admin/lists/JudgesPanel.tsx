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
import { textOf, yesNoOf } from '../../../components/filters/filterTypes'
import { TableFilterBar } from '../../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../../components/filters/useUrlFilters'
import { PaginationFooter } from '../../../components/ui/DataTable'
import { useAdminJudges, useSetJudgeActive, type AdminJudge } from '../../../features/admin/api'
import { useTableFilters } from '../../../features/filters/api'
import { sq } from '../../../i18n/sq'
import { SearchEmptyRow } from '../../../components/SearchEmptyRow'
import { useKeysetPaging } from '../../../lib/paging'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { serverQuery } from '../../../lib/search'
import { useDebounced } from '../../../lib/useDebounced'
import { ForbiddenPage } from '../../ErrorPages'
import { StatusChip } from './common'
import { rowSx, useForbiddenRedirect } from './helpers'
import { JudgeDialog } from './JudgeDialog'

const t = sq.adminLists

/** Judges: search anywhere in the name (server), court and status filters, keyset pages of 10. */
export function JudgesPanel() {
  // task 053m: the shared FilterBar (GET /api/filters/judges), the values in the URL, sent to the API
  const meta = useTableFilters('judges')
  const url = useUrlFilters(meta.data ?? [])
  const search = textOf(url.values, 'q')
  const active = yesNoOf(url.values, 'active')
  const courtId = textOf(url.values, 'court')
  const q = useDebounced(serverQuery(search), 300)
  const paging = useKeysetPaging()
  const judges = useAdminJudges({ q, active, courtId }, { cursor: paging.cursor, size: paging.size })
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

      <TableFilterBar meta={meta} url={url} />

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
              <SearchEmptyRow q={search} colSpan={5} onClear={() => url.setKey('q', '')} />
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
