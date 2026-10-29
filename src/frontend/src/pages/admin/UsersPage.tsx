import {
  Alert,
  Box,
  Button,
  Chip,
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
import { useActivateUser, useAdminRoles, useAdminUsers, type AdminUser } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'
import { useUrlSearch } from '../../lib/useUrlSearch'
import { UsersCreateDialog } from './UsersCreateDialog'
import { UsersDeactivateDialog } from './UsersDeactivateDialog'
import { UsersRolesDialog } from './UsersRolesDialog'

const t = sq.adminUsers

export function UsersPage() {
  const [search, setSearch] = useUrlSearch()
  const q = useDebounced(serverQuery(search), 300)
  const users = useAdminUsers(q)
  const roles = useAdminRoles()
  const activate = useActivateUser()
  const [creating, setCreating] = useState(false)
  const [rolesFor, setRolesFor] = useState<AdminUser | null>(null)
  const [deactivating, setDeactivating] = useState<AdminUser | null>(null)

  const rows = users.data?.pages.flatMap((p) => p.items) ?? []
  const roleName = (code: string) => roles.data?.find((r) => r.code === code)?.name ?? code

  return (
    <Box>
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Button variant="contained" onClick={() => setCreating(true)}>
          {t.addUser}
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

      {users.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(users.error)}
        </Alert>
      )}
      {activate.isError && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => activate.reset()}>
          {problemMessage(activate.error)}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table size="small" aria-label={t.title}>
          <TableHead>
            <TableRow>
              <TableCell>{t.columns.name}</TableCell>
              <TableCell>{t.columns.email}</TableCell>
              <TableCell>{t.columns.organization}</TableCell>
              <TableCell>{t.columns.roles}</TableCell>
              <TableCell>{t.columns.status}</TableCell>
              <TableCell>{t.columns.lastLogin}</TableCell>
              <TableCell align="right">{sq.common.actions}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((u) => {
              const fullName = `${u.firstName} ${u.lastName}`
              return (
                <TableRow key={u.publicId} hover>
                  <TableCell>{fullName}</TableCell>
                  <TableCell>{u.email}</TableCell>
                  <TableCell>{u.office?.name ?? u.institution?.name ?? ''}</TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                      {u.roles.map((r) => (
                        <Chip key={r} size="small" label={roleName(r)} />
                      ))}
                    </Stack>
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      color={u.isActive ? 'success' : 'default'}
                      label={u.isActive ? t.active : t.inactive}
                    />
                  </TableCell>
                  <TableCell>{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : t.neverLoggedIn}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    <Button size="small" onClick={() => setRolesFor(u)} aria-label={`${t.roles.manage}: ${fullName}`}>
                      {t.roles.manage}
                    </Button>
                    {u.isActive ? (
                      <Button
                        size="small"
                        color="error"
                        onClick={() => setDeactivating(u)}
                        aria-label={`${t.deactivate.button}: ${fullName}`}
                      >
                        {t.deactivate.button}
                      </Button>
                    ) : (
                      <Button
                        size="small"
                        onClick={() => activate.mutate(u.publicId)}
                        disabled={activate.isPending}
                        aria-label={`${t.activate}: ${fullName}`}
                      >
                        {t.activate}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              )
            })}
            {users.isSuccess && rows.length === 0 && !q && (
              <TableRow>
                <TableCell colSpan={7}>{t.empty}</TableCell>
              </TableRow>
            )}
            {users.isSuccess && rows.length === 0 && q && (
              <SearchEmptyRow q={search} colSpan={7} onClear={() => setSearch('')} />
            )}
          </TableBody>
        </Table>
      </TableContainer>

      <Box sx={{ mt: 2, display: 'flex', justifyContent: 'center' }}>
        {users.isPending || users.isFetchingNextPage ? (
          <CircularProgress aria-label={sq.errors.loading} />
        ) : (
          users.hasNextPage && <Button onClick={() => void users.fetchNextPage()}>{sq.common.loadMore}</Button>
        )}
      </Box>

      <UsersCreateDialog open={creating} onClose={() => setCreating(false)} />
      {rolesFor && <UsersRolesDialog user={rolesFor} onClose={() => setRolesFor(null)} />}
      {deactivating && <UsersDeactivateDialog user={deactivating} onClose={() => setDeactivating(null)} />}
    </Box>
  )
}
