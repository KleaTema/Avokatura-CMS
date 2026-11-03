import { Alert, Box, Button, Stack, TableCell, TableRow, TextField } from '@mui/material'
import { useState } from 'react'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { AppSelect } from '../../components/ui/AppSelect'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAdminOffices, useSetOfficeActive, type AdminOffice } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { useClientPaging } from '../../lib/paging'
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
  const [type, setType] = useUrlSearch('type')
  const [parent, setParent] = useUrlSearch('parent')
  const [active, setActiveFilter] = useUrlSearch('active')

  // a few dozen offices: "contains" search and the column filters in the browser, numbered pages
  const all = offices.data ?? []
  const filtered = all.filter(
    (o) =>
      matches(q, o.code, o.name, o.head?.name) &&
      (type === '' || o.officeType === type) &&
      (parent === '' || o.parent?.publicId === parent) &&
      (active === '' || String(o.isActive) === active),
  )
  const paging = useClientPaging(filtered)
  if (isForbidden(offices.error)) return <ForbiddenPage />
  const rows = paging.items
  const parents = all.filter((o) => all.some((child) => child.parent?.publicId === o.publicId))

  const toggle = (office: AdminOffice) =>
    setActive.mutate({ publicId: office.publicId, active: !office.isActive }, { onError: redirectIfForbidden })

  return (
    <Box>
      <PageHeader
        title={t.title}
        actions={
          <Button variant="contained" color="secondary" onClick={() => setEditing({ office: null })}>
            {t.add}
          </Button>
        }
      />

      <TextField
        label={t.search}
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ mb: 2, flexWrap: 'wrap' }}>
        <AppSelect
          filter
          size="small"
          label={t.columns.officeType}
          value={type}
          onChange={setType}
          options={Object.entries(t.officeTypes).map(([value, label]) => ({ value, label }))}
          sx={{ width: { xs: '100%', md: 220 } }}
        />
        <AppSelect
          filter
          size="small"
          label={t.columns.parent}
          value={parent}
          onChange={setParent}
          options={parents.map((o) => ({ value: o.publicId, label: o.name }))}
          sx={{ width: { xs: '100%', md: 260 } }}
        />
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

      <DataTable
        label={t.title}
        loading={offices.isPending}
        columns={[
          { key: 'code', label: t.columns.code },
          { key: 'name', label: t.columns.name },
          { key: 'type', label: t.columns.officeType },
          { key: 'parent', label: t.columns.parent },
          { key: 'head', label: t.columns.head },
          { key: 'users', label: t.columns.activeUsers, align: 'right' },
          { key: 'status', label: t.columns.status },
          { key: 'actions', label: sq.common.actions, align: 'right' },
        ]}
        empty={offices.isSuccess && offices.data.length === 0 && <EmptyState title={t.empty} />}
        footer={<PaginationFooter mode="numbered" paging={paging} />}
      >
              {rows.map((office) => (
                <TableRow key={office.publicId} hover sx={rowSx(office.isActive)}>
                  <TableCell>
                    <CodeText>{office.code}</CodeText>
                  </TableCell>
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
              {offices.isSuccess && offices.data.length > 0 && rows.length === 0 && (
                <SearchEmptyRow q={q} colSpan={8} onClear={() => setQ('')} />
              )}
      </DataTable>

      {editing && (
        <OfficeDialog office={editing.office} offices={offices.data ?? []} onClose={() => setEditing(null)} />
      )}
      {headFor && <OfficeHeadDialog office={headFor} onClose={() => setHeadFor(null)} />}
    </Box>
  )
}
