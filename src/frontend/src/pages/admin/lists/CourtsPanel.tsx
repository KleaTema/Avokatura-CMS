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
import { AppSelect } from '../../../components/ui/AppSelect'
import { PaginationFooter } from '../../../components/ui/DataTable'
import { useAdminCourts, useSetCourtActive, type AdminCourt } from '../../../features/admin/api'
import { useCourtLevels } from '../../../features/reference/api'
import { sq } from '../../../i18n/sq'
import { isForbidden, problemMessage } from '../../../lib/problem'
import { useClientPaging } from '../../../lib/paging'
import { matches } from '../../../lib/search'
import { useUrlSearch } from '../../../lib/useUrlSearch'
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
  const [q, setQ] = useUrlSearch()
  const [level, setLevel] = useUrlSearch('level')
  const [foreign, setForeign] = useUrlSearch('foreign')
  const [active, setActiveFilter] = useUrlSearch('active')

  const levelName = (code: string) => levels.data?.find((l) => l.code === code)?.name ?? code
  // about 100 courts: "contains" search and the column filters in the browser
  const all = courts.data ?? []
  const filtered = all.filter(
    (c) =>
      matches(q, c.name, c.city, levelName(c.courtLevel)) &&
      (level === '' || c.courtLevel === level) &&
      (foreign === '' || String(c.isForeign) === foreign) &&
      (active === '' || String(c.isActive) === active),
  )
  const paging = useClientPaging(filtered)
  if (isForbidden(courts.error)) return <ForbiddenPage />
  const visible = paging.items
  const toggle = (court: AdminCourt) =>
    setActive.mutate({ id: Number(court.id), active: !court.isActive }, { onError: redirectIfForbidden })

  return (
    <>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h2">{t.lists.courts}</Typography>
        <Button variant="contained" color="secondary" onClick={() => setEditing({ court: null })}>
          {t.add}
        </Button>
      </Stack>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap' }}>
        <TextField
          label={t.searchCourts}
          type="search"
          value={q}
          onChange={(e) => setQ(e.target.value)}
          sx={{ flexGrow: 1 }}
        />
        <AppSelect
          filter
          label={t.columns.courtLevel}
          value={level}
          onChange={setLevel}
          options={(levels.data ?? []).map((l) => ({ value: l.code, label: l.name }))}
          sx={{ minWidth: 220 }}
        />
        <AppSelect
          filter
          label={t.columns.isForeign}
          value={foreign}
          onChange={setForeign}
          options={[
            { value: 'true', label: t.yes },
            { value: 'false', label: t.no },
          ]}
          sx={{ minWidth: 140 }}
        />
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
              {visible.map((court) => (
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
              {courts.isSuccess && all.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>{t.empty}</TableCell>
                </TableRow>
              )}
              {courts.isSuccess && all.length > 0 && visible.length === 0 && (
                <SearchEmptyRow q={q} colSpan={6} onClear={() => setQ('')} />
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      <PaginationFooter mode="numbered" paging={paging} />

      {editing && <CourtDialog court={editing.court} onClose={() => setEditing(null)} />}
    </>
  )
}
