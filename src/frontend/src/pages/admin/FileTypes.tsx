import { Alert, Box, Button, TableCell, TableRow } from '@mui/material'
import { useState } from 'react'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useAdminFileTypes, type AdminFileType } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { isForbidden, problemMessage } from '../../lib/problem'
import { ForbiddenPage } from '../ErrorPages'
import { StatusChip } from './lists/common'
import { FileTypeDialog } from './FileTypeDialog'
import { formatMegabytes } from './fileTypeSize'
import { rowSx } from './lists/helpers'

const t = sq.adminFileTypes

export function FileTypesPage() {
  const fileTypes = useAdminFileTypes()
  const [editing, setEditing] = useState<{ fileType: AdminFileType | null } | null>(null)

  if (isForbidden(fileTypes.error)) return <ForbiddenPage />

  return (
    <Box>
      <PageHeader
        title={t.title}
        actions={
          <Button variant="contained" color="secondary" onClick={() => setEditing({ fileType: null })}>
            {t.add}
          </Button>
        }
      />

      {fileTypes.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(fileTypes.error)}
        </Alert>
      )}

      <DataTable
        label={t.title}
        loading={fileTypes.isPending}
        columns={[
          { key: 'extension', label: t.columns.extension },
          { key: 'contentType', label: t.columns.contentType },
          { key: 'magicHex', label: t.columns.magicHex },
          { key: 'maxSize', label: t.columns.maxSize, align: 'right' },
          { key: 'appliesTo', label: t.columns.appliesTo },
          { key: 'status', label: t.columns.status },
          { key: 'actions', label: sq.common.actions, align: 'right' },
        ]}
        empty={fileTypes.isSuccess && fileTypes.data.length === 0 && <EmptyState title={t.empty} />}
      >
        {(fileTypes.data ?? []).map((f) => (
          <TableRow key={f.id} hover sx={rowSx(f.isActive)}>
            <TableCell>{f.extension}</TableCell>
            <TableCell>{f.contentType}</TableCell>
            <TableCell>
              <CodeText>{f.magicHex}</CodeText>
            </TableCell>
            <TableCell align="right">{t.megabytes(formatMegabytes(Number(f.maxSizeBytes)))}</TableCell>
            <TableCell>{t.appliesToValues[f.appliesTo] ?? f.appliesTo}</TableCell>
            <TableCell>
              <StatusChip active={f.isActive} />
            </TableCell>
            <TableCell align="right">
              <Button
                size="small"
                onClick={() => setEditing({ fileType: f })}
                aria-label={`${t.edit}: ${f.extension} (${t.appliesToValues[f.appliesTo] ?? f.appliesTo})`}
              >
                {t.edit}
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </DataTable>

      {editing && <FileTypeDialog fileType={editing.fileType} onClose={() => setEditing(null)} />}
    </Box>
  )
}
