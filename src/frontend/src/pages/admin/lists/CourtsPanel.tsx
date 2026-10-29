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
import { useAdminCourts, useSetCourtActive, type AdminCourt } from '../../../features/admin/api'
import { useCourtLevels } from '../../../features/reference/api'
import { sq } from '../../../i18n/sq'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { ForbiddenPage } from '../../ErrorPages'
import { StatusChip } from './common'
import { rowSx, useForbiddenRedirect } from './helpers'
import { CourtDialog } from './CourtDialog'

const t = sq.adminLists

export function CourtsPanel() {
  const courts = useAdminCourts()
  const levels = useCourtLevels()
  const setActive = useSetCourtActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ court: AdminCourt | null } | null>(null)

  if (isForbidden(courts.error)) return <ForbiddenPage />

  const levelName = (code: string) => levels.data?.find((l) => l.code === code)?.name ?? code
  const toggle = (court: AdminCourt) =>
    setActive.mutate({ id: Number(court.id), active: !court.isActive }, { onError: redirectIfForbidden })

  return (
    <>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h5" component="h2">
          {t.lists.courts}
        </Typography>
        <Button variant="contained" onClick={() => setEditing({ court: null })}>
          {t.add}
        </Button>
      </Stack>

      {courts.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(courts.error)}
        </Alert>
      )}
      {setActive.isError && !isForbidden(setActive.error) && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setActive.reset()}>
          {problemMessage(setActive.error)}
        </Alert>
      )}

      {courts.isPending ? (
        <CircularProgress aria-label={sq.errors.loading} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={t.lists.courts}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.name}</TableCell>
                <TableCell>{t.columns.courtLevel}</TableCell>
                <TableCell>{t.columns.city}</TableCell>
                <TableCell>{t.columns.isForeign}</TableCell>
                <TableCell>{t.columns.status}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(courts.data ?? []).map((court) => (
                <TableRow key={court.id} hover sx={rowSx(court.isActive)}>
                  <TableCell>{court.name}</TableCell>
                  <TableCell>{levelName(court.courtLevel)}</TableCell>
                  <TableCell>{court.city ?? ''}</TableCell>
                  <TableCell>{court.isForeign ? t.yes : t.no}</TableCell>
                  <TableCell>
                    <StatusChip active={court.isActive} />
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    <Button size="small" onClick={() => setEditing({ court })} aria-label={`${t.edit}: ${court.name}`}>
                      {t.edit}
                    </Button>
                    <Button
                      size="small"
                      color={court.isActive ? 'error' : 'primary'}
                      onClick={() => toggle(court)}
                      disabled={setActive.isPending}
                      aria-label={`${court.isActive ? t.deactivate : t.activate}: ${court.name}`}
                    >
                      {court.isActive ? t.deactivate : t.activate}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {courts.isSuccess && courts.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>{t.empty}</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {editing && <CourtDialog court={editing.court} onClose={() => setEditing(null)} />}
    </>
  )
}
