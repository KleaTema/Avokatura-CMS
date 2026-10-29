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
import { useAdminInstitutionList, useSetInstitutionActive, type AdminInstitution } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { isForbidden, problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'
import { useUrlSearch } from '../../lib/useUrlSearch'
import { ForbiddenPage } from '../ErrorPages'
import { InstitutionDialog } from './InstitutionDialog'
import { StatusChip } from './lists/common'
import { rowSx, useForbiddenRedirect } from './lists/helpers'

const t = sq.adminInstitutions
const tl = sq.adminLists

export function InstitutionsPage() {
  const [search, setSearch] = useUrlSearch()
  const q = useDebounced(serverQuery(search), 300)
  const institutions = useAdminInstitutionList(q)
  const setActive = useSetInstitutionActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ institution: AdminInstitution | null } | null>(null)

  if (isForbidden(institutions.error)) return <ForbiddenPage />

  const rows = institutions.data?.pages.flatMap((p) => p.items) ?? []
  const toggle = (institution: AdminInstitution) =>
    setActive.mutate(
      { publicId: institution.publicId, active: !institution.isActive },
      { onError: redirectIfForbidden },
    )

  return (
    <Box>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Button variant="contained" onClick={() => setEditing({ institution: null })}>
          {t.add}
        </Button>
      </Stack>

      <TextField
        label={t.search}
        type="search"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />

      {institutions.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(institutions.error)}
        </Alert>
      )}
      {setActive.isError && !isForbidden(setActive.error) && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setActive.reset()}>
          {problemMessage(setActive.error)}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table size="small" aria-label={t.title}>
          <TableHead>
            <TableRow>
              <TableCell>{t.columns.name}</TableCell>
              <TableCell>{t.columns.shortName}</TableCell>
              <TableCell>{t.columns.institutionType}</TableCell>
              <TableCell>{t.columns.parent}</TableCell>
              <TableCell>{t.columns.nipt}</TableCell>
              <TableCell align="right">{t.columns.activeUsers}</TableCell>
              <TableCell>{t.columns.status}</TableCell>
              <TableCell align="right">{sq.common.actions}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((i) => (
              <TableRow key={i.publicId} hover sx={rowSx(i.isActive)}>
                <TableCell>{i.name}</TableCell>
                <TableCell>{i.shortName ?? ''}</TableCell>
                <TableCell>{t.institutionTypes[i.institutionType] ?? i.institutionType}</TableCell>
                <TableCell>{i.parent?.name ?? ''}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace' }}>{i.nipt ?? ''}</TableCell>
                <TableCell align="right">{i.activeUsers}</TableCell>
                <TableCell>
                  <StatusChip active={i.isActive} />
                </TableCell>
                <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                  <Button size="small" onClick={() => setEditing({ institution: i })} aria-label={`${t.edit}: ${i.name}`}>
                    {t.edit}
                  </Button>
                  <Button
                    size="small"
                    color={i.isActive ? 'error' : 'primary'}
                    onClick={() => toggle(i)}
                    disabled={setActive.isPending}
                    aria-label={`${i.isActive ? tl.deactivate : tl.activate}: ${i.name}`}
                  >
                    {i.isActive ? tl.deactivate : tl.activate}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {institutions.isSuccess && rows.length === 0 && !q && (
              <TableRow>
                <TableCell colSpan={8}>{t.empty}</TableCell>
              </TableRow>
            )}
            {institutions.isSuccess && rows.length === 0 && q && (
              <SearchEmptyRow q={search} colSpan={8} onClear={() => setSearch('')} />
            )}
          </TableBody>
        </Table>
      </TableContainer>

      <Box sx={{ mt: 2, display: 'flex', justifyContent: 'center' }}>
        {institutions.isPending || institutions.isFetchingNextPage ? (
          <CircularProgress aria-label={sq.errors.loading} />
        ) : (
          institutions.hasNextPage && (
            <Button onClick={() => void institutions.fetchNextPage()}>{sq.common.loadMore}</Button>
          )
        )}
      </Box>

      {editing && <InstitutionDialog institution={editing.institution} onClose={() => setEditing(null)} />}
    </Box>
  )
}
