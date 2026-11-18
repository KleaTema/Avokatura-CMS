import { Alert, Box, Button, TableCell, TableRow } from '@mui/material'
import { useState } from 'react'
import { matchesFilters } from '../../components/filters/clientFilter'
import { textOf } from '../../components/filters/filterTypes'
import { TableFilterBar } from '../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../components/filters/useUrlFilters'
import { SearchEmptyRow } from '../../components/SearchEmptyRow'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable, PaginationFooter } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAdminOffices, useSetOfficeActive, type AdminOffice } from '../../features/admin/api'
import { useTableFilters } from '../../features/filters/api'
import { sq } from '../../i18n/sq'
import { useClientPaging } from '../../lib/paging'
import { isForbidden, problemMessage } from '../../lib/problem'
import { ForbiddenPage } from '../ErrorPages'
import { StatusChip } from './lists/common'
import { rowSx, useForbiddenRedirect } from './lists/helpers'
import { OfficeDialog } from './OfficeDialog'
import { OfficeCourtsDialog } from './OfficeCourtsDialog'
import { OfficeHeadDialog } from './OfficeHeadDialog'

const t = sq.adminOffices
const tl = sq.adminLists

/** A row's value for a filter key of GET /api/filters/offices. */
function officeField(o: AdminOffice, key: string): unknown {
  switch (key) {
    case 'q':
      return [o.code, o.name, o.head?.name]
    case 'type':
      return o.officeType
    case 'parent':
      return o.parent?.publicId
    case 'active':
      return o.isActive
    default:
      return undefined
  }
}

export function OfficesPage() {
  const offices = useAdminOffices()
  const setActive = useSetOfficeActive()
  const redirectIfForbidden = useForbiddenRedirect()
  const [editing, setEditing] = useState<{ office: AdminOffice | null } | null>(null)
  const [headFor, setHeadFor] = useState<AdminOffice | null>(null)
  const [courtsFor, setCourtsFor] = useState<AdminOffice | null>(null)
  // task 053m: the shared FilterBar (metadata GET /api/filters/offices), kept in the URL
  const meta = useTableFilters('offices')
  const filterDefs = meta.data ?? []
  const url = useUrlFilters(filterDefs)
  const q = textOf(url.values, 'q')

  // a few dozen offices: the filters applied in the browser, numbered pages
  const all = offices.data ?? []
  const filtered = all.filter((o) => matchesFilters(o, filterDefs, url.values, officeField))
  const paging = useClientPaging(filtered)
  if (isForbidden(offices.error)) return <ForbiddenPage />
  const rows = paging.items

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

      <TableFilterBar meta={meta} url={url} />

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
                    <Button size="small" onClick={() => setCourtsFor(office)} aria-label={`${t.courts}: ${office.name}`}>
                      {t.courts}
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
                <SearchEmptyRow q={q} colSpan={8} onClear={() => url.setKey('q', '')} />
              )}
      </DataTable>

      {editing && (
        <OfficeDialog office={editing.office} offices={offices.data ?? []} onClose={() => setEditing(null)} />
      )}
      {headFor && <OfficeHeadDialog office={headFor} onClose={() => setHeadFor(null)} />}
      {courtsFor && <OfficeCourtsDialog office={courtsFor} onClose={() => setCourtsFor(null)} />}
    </Box>
  )
}
