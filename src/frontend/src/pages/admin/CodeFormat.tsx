import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  TableCell,
  TableRow,
} from '@mui/material'
import { useState } from 'react'
import { CodeText } from '../../components/ui/CodeText'
import { DataTable } from '../../components/ui/DataTable'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { useActivateCodeFormat, useAdminCodeFormats, type AdminCodeFormat } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { isForbidden, problemMessage } from '../../lib/problem'
import { ForbiddenPage } from '../ErrorPages'
import { CodeFormatCreateDialog } from './CodeFormatCreateDialog'
import { rowSx, useForbiddenRedirect } from './lists/helpers'

const t = sq.adminCodeFormat

export function CodeFormatPage() {
  const formats = useAdminCodeFormats()
  const [creating, setCreating] = useState(false)
  const [activating, setActivating] = useState<AdminCodeFormat | null>(null)

  if (isForbidden(formats.error)) return <ForbiddenPage />

  return (
    <Box>
      <PageHeader
        title={t.title}
        actions={
          <Button variant="contained" color="secondary" onClick={() => setCreating(true)}>
            {t.add}
          </Button>
        }
      />

      {formats.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(formats.error)}
        </Alert>
      )}

      <DataTable
        label={t.title}
        loading={formats.isPending}
        columns={[
          { key: 'template', label: t.columns.template },
          { key: 'digits', label: t.columns.randomDigits, align: 'right' },
          { key: 'note', label: t.columns.note },
          { key: 'createdAt', label: t.columns.createdAt },
          { key: 'status', label: t.columns.status },
          { key: 'actions', label: sq.common.actions, align: 'right' },
        ]}
        empty={formats.isSuccess && formats.data.length === 0 && <EmptyState title={t.empty} />}
      >
              {(formats.data ?? []).map((f) => (
                <TableRow key={f.id} hover sx={rowSx(f.isActive)}>
                  <TableCell>
                    <CodeText>{f.template}</CodeText>
                  </TableCell>
                  <TableCell align="right">{f.randomDigits}</TableCell>
                  <TableCell>{f.note ?? ''}</TableCell>
                  <TableCell>{formatDateTime(f.createdAt)}</TableCell>
                  <TableCell>
                    {f.isActive ? <Chip size="small" color="success" label={t.activeChip} /> : t.inactive}
                  </TableCell>
                  <TableCell align="right">
                    {!f.isActive && (
                      <Button size="small" onClick={() => setActivating(f)} aria-label={`${t.activate}: ${f.template}`}>
                        {t.activate}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
      </DataTable>

      {creating && <CodeFormatCreateDialog onClose={() => setCreating(false)} />}
      {activating && <ActivateDialog format={activating} onClose={() => setActivating(null)} />}
    </Box>
  )
}

function ActivateDialog({ format, onClose }: { format: AdminCodeFormat; onClose: () => void }) {
  const activate = useActivateCodeFormat()
  const redirectIfForbidden = useForbiddenRedirect()

  const confirm = () =>
    activate.mutate(Number(format.id), {
      onSuccess: onClose,
      onError: redirectIfForbidden,
    })

  return (
    <Dialog open onClose={onClose} aria-labelledby="activate-format-title" aria-describedby="activate-format-note">
      <DialogTitle id="activate-format-title">{t.activateTitle}</DialogTitle>
      <DialogContent>
        {activate.isError && !isForbidden(activate.error) && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {problemMessage(activate.error)}
          </Alert>
        )}
        <DialogContentText sx={{ mb: 1 }}>{t.activateQuestion(format.template)}</DialogContentText>
        <DialogContentText id="activate-format-note">{t.activateNote}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" onClick={confirm} disabled={activate.isPending}>
          {t.activate}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
