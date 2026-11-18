import { Alert, Box, Button, TableCell, TableRow } from '@mui/material'
import { useState } from 'react'
import { textOf, yesNoOf } from '../../components/filters/filterTypes'
import { TableFilterBar } from '../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../components/filters/useUrlFilters'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAdminInstitutionList, useSetInstitutionActive, type AdminInstitution } from '../../features/admin/api'
import { useTableFilters } from '../../features/filters/api'
import { sq } from '../../i18n/sq'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { useKeysetPaging } from '../../lib/paging'
import { isForbidden, problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'
import { ForbiddenPage } from '../ErrorPages'
import { InstitutionDialog } from './InstitutionDialog'
import { StatusChip } from './lists/common'
import { rowSx, useForbiddenRedirect } from './lists/helpers'

const t = sq.adminInstitutions
const tl = sq.adminLists

/** Institutions (task 015): search anywhere in the name, short name or NIPT; type, ministry and status filters. */
export function InstitutionsPage() {
  // task 053m: the shared FilterBar (GET /api/filters/institutions), the values in the URL, sent to the API
  const meta = useTableFilters('institutions')
  const url = useUrlFilters(meta.data ?? [])
  const search = textOf(url.values, 'q')
  const type = textOf(url.values, 'type')
  const parentId = textOf(url.values, 'parent')
  const active = yesNoOf(url.values, 'active')
  const q = useDebounced(serverQuery(search), 300)
  const paging = useKeysetPaging()
  const institutions = useAdminInstitutionList({ q, type, active, parentId }, { cursor: paging.cursor, size: paging.size })
  const setActive = useSetInstitutionActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ institution: AdminInstitution | null } | null>(null)

  if (isForbidden(institutions.error)) return <ForbiddenPage />

  const rows = institutions.data?.items ?? []
  const filtered = type !== '' || parentId !== '' || active !== ''
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

      <TableFilterBar meta={meta} url={url} />

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
        empty={institutions.isSuccess && rows.length === 0 && !q && <EmptyState title={filtered ? t.emptyFiltered : t.empty} />}
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={rows.length}
            total={Number(institutions.data?.total ?? 0)}
            nextCursor={institutions.data?.nextCursor}
            busy={institutions.isFetching}
            stale={institutions.isPlaceholderData}
          />
        }
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
              <SearchEmptyRow q={search} colSpan={8} onClear={() => url.setKey('q', '')} />
            )}
      </DataTable>

      {editing && <InstitutionDialog institution={editing.institution} onClose={() => setEditing(null)} />}
    </Box>
  )
}
