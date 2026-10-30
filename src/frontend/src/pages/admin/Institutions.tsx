import { Alert, Box, Button, CircularProgress, TableCell, TableRow, TextField } from '@mui/material'
import { useState } from 'react'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
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
      <PageHeader
        title={t.title}
        actions={
          <Button variant="contained" color="secondary" onClick={() => setEditing({ institution: null })}>
            {t.add}
          </Button>
        }
      />

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

      <DataTable
        label={t.title}
        loading={institutions.isPending}
        columns={[
          { key: 'name', label: t.columns.name },
          { key: 'short', label: t.columns.shortName },
          { key: 'type', label: t.columns.institutionType },
          { key: 'parent', label: t.columns.parent },
          { key: 'nipt', label: t.columns.nipt },
          { key: 'users', label: t.columns.activeUsers, align: 'right' },
          { key: 'status', label: t.columns.status },
          { key: 'actions', label: sq.common.actions, align: 'right' },
        ]}
        empty={institutions.isSuccess && rows.length === 0 && !q && <EmptyState title={t.empty} />}
      >
            {rows.map((i) => (
              <TableRow key={i.publicId} hover sx={rowSx(i.isActive)}>
                <TableCell>{i.name}</TableCell>
                <TableCell>{i.shortName ?? ''}</TableCell>
                <TableCell>{t.institutionTypes[i.institutionType] ?? i.institutionType}</TableCell>
                <TableCell>{i.parent?.name ?? ''}</TableCell>
                <TableCell>
                  <CodeText>{i.nipt ?? ''}</CodeText>
                </TableCell>
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
            {institutions.isSuccess && rows.length === 0 && q && (
              <SearchEmptyRow q={search} colSpan={8} onClear={() => setSearch('')} />
            )}
      </DataTable>

      <Box sx={{ mt: 2, display: 'flex', justifyContent: 'center' }}>
        {institutions.isFetchingNextPage ? (
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
