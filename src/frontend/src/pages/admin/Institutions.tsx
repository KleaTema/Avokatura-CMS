import { Alert, Box, Button, Stack, TableCell, TableRow, TextField } from '@mui/material'
import { useState } from 'react'
import { InstitutionPicker } from '../../components/InstitutionPicker'
import { AppSelect } from '../../components/ui/AppSelect'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAdminInstitutionList, useSetInstitutionActive, type AdminInstitution } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { useKeysetPaging } from '../../lib/paging'
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

/** Institutions (task 015): search anywhere in the name, short name or NIPT; type, ministry and status filters. */
export function InstitutionsPage() {
  const [search, setSearch] = useUrlSearch()
  const [type, setType] = useUrlSearch('type')
  const [parentId, setParentId] = useUrlSearch('parent')
  const [parentName, setParentName] = useUrlSearch('parentName')
  const [active, setActiveFilter] = useUrlSearch('active')
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

      <TextField
        label={t.search}
        type="search"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />
      {/* a filter for every column a user narrows by: type, ministry, status */}
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap' }}>
        <AppSelect
          filter
          size="small"
          label={t.columns.institutionType}
          value={type}
          onChange={setType}
          options={Object.entries(t.institutionTypes).map(([value, label]) => ({ value, label }))}
          sx={{ width: { xs: '100%', md: 220 } }}
        />
        <Box sx={{ width: { xs: '100%', md: 300 } }}>
          <InstitutionPicker
            filter
            size="small"
            label={t.columns.parent}
            onlyType="MINISTRY"
            value={parentId ? { publicId: parentId, name: parentName } : null}
            onChange={(picked) => {
              setParentId(picked?.publicId ?? '')
              setParentName(picked?.name ?? '')
            }}
          />
        </Box>
        <AppSelect
          filter
          size="small"
          label={t.columns.status}
          value={active}
          onChange={setActiveFilter}
          options={[
            { value: 'true', label: tl.active },
            { value: 'false', label: tl.inactive },
          ]}
          sx={{ width: { xs: '100%', md: 180 } }}
        />
      </Stack>

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
              <SearchEmptyRow q={search} colSpan={8} onClear={() => setSearch('')} />
            )}
      </DataTable>

      {editing && <InstitutionDialog institution={editing.institution} onClose={() => setEditing(null)} />}
    </Box>
  )
}
