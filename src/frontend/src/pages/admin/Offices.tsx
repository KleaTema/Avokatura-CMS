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
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { useAdminOffices, useSetOfficeActive, type AdminOffice } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { isForbidden, problemMessage } from '../../lib/problem'
import { matches } from '../../lib/search'
import { useUrlSearch } from '../../lib/useUrlSearch'
import { ForbiddenPage } from '../ErrorPages'
import { StatusChip } from './lists/common'
import { rowSx, useForbiddenRedirect } from './lists/helpers'
import { OfficeDialog } from './OfficeDialog'
import { OfficeHeadDialog } from './OfficeHeadDialog'

const t = sq.adminOffices
const tl = sq.adminLists

export function OfficesPage() {
  const offices = useAdminOffices()
  const setActive = useSetOfficeActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ office: AdminOffice | null } | null>(null)
  const [headFor, setHeadFor] = useState<AdminOffice | null>(null)
  const [q, setQ] = useUrlSearch()

  if (isForbidden(offices.error)) return <ForbiddenPage />

  // a few dozen offices: filtered in the browser
  const rows = (offices.data ?? []).filter((o) => matches(q, o.code, o.name, o.head?.name))

  const toggle = (office: AdminOffice) =>
    setActive.mutate({ publicId: office.publicId, active: !office.isActive }, { onError: redirectIfForbidden })

  return (
    <Box>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Button variant="contained" onClick={() => setEditing({ office: null })}>
          {t.add}
        </Button>
      </Stack>

      <TextField
        label={t.search}
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />

      {offices.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(offices.error)}
        </Alert>
      )}
      {setActive.isError && !isForbidden(setActive.error) && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setActive.reset()}>
          {problemMessage(setActive.error)}
        </Alert>
      )}

      {offices.isPending ? (
        <CircularProgress aria-label={sq.errors.loading} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={t.title}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.code}</TableCell>
                <TableCell>{t.columns.name}</TableCell>
                <TableCell>{t.columns.officeType}</TableCell>
                <TableCell>{t.columns.parent}</TableCell>
                <TableCell>{t.columns.head}</TableCell>
                <TableCell align="right">{t.columns.activeUsers}</TableCell>
                <TableCell>{t.columns.status}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((office) => (
                <TableRow key={office.publicId} hover sx={rowSx(office.isActive)}>
                  <TableCell sx={{ fontFamily: 'monospace' }}>{office.code}</TableCell>
                  <TableCell>{office.name}</TableCell>
                  <TableCell>{t.officeTypes[office.officeType] ?? office.officeType}</TableCell>
                  <TableCell>{office.parent?.name ?? ''}</TableCell>
                  <TableCell>{office.head?.name ?? t.noHead}</TableCell>
                  <TableCell align="right">{office.activeUsers}</TableCell>
                  <TableCell>
                    <StatusChip active={office.isActive} />
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    <Button size="small" onClick={() => setEditing({ office })} aria-label={`${t.edit}: ${office.name}`}>
                      {t.edit}
                    </Button>
                    <Button size="small" onClick={() => setHeadFor(office)} aria-label={`${t.setHead}: ${office.name}`}>
                      {t.setHead}
                    </Button>
                    <Button
                      size="small"
                      color={office.isActive ? 'error' : 'primary'}
                      onClick={() => toggle(office)}
                      disabled={setActive.isPending}
                      aria-label={`${office.isActive ? tl.deactivate : tl.activate}: ${office.name}`}
                    >
                      {office.isActive ? tl.deactivate : tl.activate}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
              {offices.isSuccess && offices.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={8}>{t.empty}</TableCell>
                </TableRow>
              )}
              {offices.isSuccess && offices.data.length > 0 && rows.length === 0 && (
                <SearchEmptyRow q={q} colSpan={8} onClear={() => setQ('')} />
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {editing && (
        <OfficeDialog office={editing.office} offices={offices.data ?? []} onClose={() => setEditing(null)} />
      )}
      {headFor && <OfficeHeadDialog office={headFor} onClose={() => setHeadFor(null)} />}
    </Box>
  )
}
