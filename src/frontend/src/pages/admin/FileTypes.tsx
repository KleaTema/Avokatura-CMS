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
  Typography,
} from '@mui/material'
import { useState } from 'react'
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
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Button variant="contained" onClick={() => setEditing({ fileType: null })}>
          {t.add}
        </Button>
      </Stack>

      {fileTypes.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(fileTypes.error)}
        </Alert>
      )}

      {fileTypes.isPending ? (
        <CircularProgress aria-label={sq.errors.loading} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={t.title}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.extension}</TableCell>
                <TableCell>{t.columns.contentType}</TableCell>
                <TableCell>{t.columns.magicHex}</TableCell>
                <TableCell align="right">{t.columns.maxSize}</TableCell>
                <TableCell>{t.columns.appliesTo}</TableCell>
                <TableCell>{t.columns.status}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(fileTypes.data ?? []).map((f) => (
                <TableRow key={f.id} hover sx={rowSx(f.isActive)}>
                  <TableCell>{f.extension}</TableCell>
                  <TableCell>{f.contentType}</TableCell>
                  <TableCell sx={{ fontFamily: 'monospace' }}>{f.magicHex}</TableCell>
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
              {fileTypes.isSuccess && fileTypes.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>{t.empty}</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      {editing && <FileTypeDialog fileType={editing.fileType} onClose={() => setEditing(null)} />}
    </Box>
  )
}
