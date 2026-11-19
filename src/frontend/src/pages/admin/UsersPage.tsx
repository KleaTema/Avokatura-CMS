import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TableCell,
  TableRow,
} from '@mui/material'
import { hasValue, rangeOf, textOf, yesNoOf } from '../../components/filters/filterTypes'
import { OnlyItemChip } from '../../components/filters/OnlyItemChip'
import { useOnlyItem } from '../../components/filters/useOnlyItem'
import { TableFilterBar } from '../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../components/filters/useUrlFilters'
import { ImportPanel } from '../../components/imports/ImportPanel'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { useActivateUser, useAdminRoles, useAdminUsers, type AdminUser } from '../../features/admin/api'
import { useTableFilters } from '../../features/filters/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { problemMessage } from '../../lib/problem'
import { useLiveSearch } from '../../lib/useLiveSearch'
import { UsersCreateDialog } from './UsersCreateDialog'
import { UsersDeactivateDialog } from './UsersDeactivateDialog'
import { UsersRolesDialog } from './UsersRolesDialog'
import { UsersIdentitiesDialog } from './UsersIdentitiesDialog'

const t = sq.adminUsers

/**
 * Users (task 013): search anywhere in the name or email, keyset pages of 10. Task 053m: the shared FilterBar — search,
 * role, active on top, "+ Shto filtër" for office, institution, position, last login (all sent to the API).
 */
export function UsersPage() {
  const meta = useTableFilters('users')
  const filterDefs = meta.data ?? []
  const url = useUrlFilters(filterDefs)
  const search = textOf(url.values, 'q')
  const q = useLiveSearch(search)
  const position = useLiveSearch(textOf(url.values, 'position'))
  const role = textOf(url.values, 'role')
  const officeId = textOf(url.values, 'office')
  const institutionId = textOf(url.values, 'institution')
  const active = yesNoOf(url.values, 'active')
  const lastLoginFrom = rangeOf(url.values, 'lastLogin', 'from')
  const lastLoginTo = rangeOf(url.values, 'lastLogin', 'to')
  const paging = useKeysetPaging()
  // task 053n: the admin dashboard's cards open these; a "what changed" row opens one user (?userId=)
  const userId = useOnlyItem('userId')
  const users = useAdminUsers(
    {
      q,
      role,
      officeId,
      institutionId,
      active,
      position,
      lastLoginFrom,
      lastLoginTo,
      noRole: yesNoOf(url.values, 'noRole'),
      neverLoggedIn: yesNoOf(url.values, 'neverLoggedIn'),
      createdFrom: rangeOf(url.values, 'created', 'from'),
      createdTo: rangeOf(url.values, 'created', 'to'),
      login: textOf(url.values, 'login'),
      userId,
    },
    { cursor: paging.cursor, size: paging.size },
  )
  const roles = useAdminRoles()
  const activate = useActivateUser()
  const [creating, setCreating] = useState(false)
  // the import dialog in the URL (?import=users): the admin dashboard's "Importet me gabime" opens it
  const [params, setParams] = useSearchParams()
  const importing = params.get('import') === 'users'
  const setImporting = (open: boolean) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (open) next.set('import', 'users')
      else for (const p of ['import', 'impQ', 'impApplied', 'impFrom', 'impTo', 'impErrors']) next.delete(p)
      return next
    })
  const [rolesFor, setRolesFor] = useState<AdminUser | null>(null)
  const [identitiesFor, setIdentitiesFor] = useState<AdminUser | null>(null)
  const [deactivating, setDeactivating] = useState<AdminUser | null>(null)

  const rows = users.data?.items ?? []
  const roleName = (code: string) => roles.data?.find((r) => r.code === code)?.name ?? code
  const filtered = filterDefs.some((f) => hasValue(url.values[f.key]))

  return (
    <Box>
      <PageHeader
        title={t.title}
        actions={
          <Stack direction="row" spacing={1}>
            {/* task 053l: the shared import pipeline, with its instructions and template */}
            <Button variant="outlined" onClick={() => setImporting(true)}>
              {sq.imports.usersButton}
            </Button>
            <Button variant="contained" color="secondary" onClick={() => setCreating(true)}>
              {t.addUser}
            </Button>
          </Stack>
        }
      />

      <TableFilterBar meta={meta} url={url} busy={users.isFetching} />
      <OnlyItemChip param="userId" name={rows[0] ? `${rows[0].firstName} ${rows[0].lastName}` : undefined} />

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

      <DataTable
        label={t.title}
        loading={users.isPending}
        columns={[
          { key: 'name', label: t.columns.name },
          { key: 'email', label: t.columns.email },
          { key: 'position', label: t.columns.position },
          { key: 'organization', label: t.columns.organization },
          { key: 'roles', label: t.columns.roles },
          { key: 'status', label: t.columns.status },
          { key: 'lastLogin', label: t.columns.lastLogin },
          { key: 'actions', label: sq.common.actions, align: 'right' },
        ]}
        empty={users.isSuccess && rows.length === 0 && !q && <EmptyState title={filtered ? t.emptyFiltered : t.empty} />}
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={rows.length}
            total={Number(users.data?.total ?? 0)}
            nextCursor={users.data?.nextCursor}
            busy={users.isFetching}
            stale={users.isPlaceholderData}
          />
        }
      >
            {rows.map((u) => {
              const fullName = `${u.firstName} ${u.lastName}`
              return (
                <TableRow key={u.publicId} hover>
                  <TableCell>{fullName}</TableCell>
                  <TableCell>{u.email}</TableCell>
                  <TableCell>{u.position ?? ''}</TableCell>
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
                    <Button size="small" onClick={() => setIdentitiesFor(u)} aria-label={`${t.identities.manage}: ${fullName}`}>
                      {t.identities.manage}
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
            {users.isSuccess && rows.length === 0 && q && (
              <SearchEmptyRow q={search} colSpan={8} onClear={() => url.setKey('q', '')} />
            )}
      </DataTable>

      <UsersCreateDialog open={creating} onClose={() => setCreating(false)} />
      <Dialog open={importing} onClose={() => setImporting(false)} fullWidth maxWidth="lg" aria-labelledby="users-import-title">
        <DialogTitle id="users-import-title">{sq.imports.usersTitle}</DialogTitle>
        <DialogContent>{importing && <ImportPanel kind="users" />}</DialogContent>
        <DialogActions>
          <Button onClick={() => setImporting(false)}>{sq.imports.close}</Button>
        </DialogActions>
      </Dialog>
      {rolesFor && <UsersRolesDialog user={rolesFor} onClose={() => setRolesFor(null)} />}
      {identitiesFor && <UsersIdentitiesDialog user={identitiesFor} onClose={() => setIdentitiesFor(null)} />}
      {deactivating && <UsersDeactivateDialog user={deactivating} onClose={() => setDeactivating(null)} />}
    </Box>
  )
}
