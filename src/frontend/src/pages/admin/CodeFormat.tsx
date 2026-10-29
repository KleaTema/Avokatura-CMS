import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
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
      <Stack direction="row" sx={{ mb: 2, alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Button variant="contained" onClick={() => setCreating(true)}>
          {t.add}
        </Button>
      </Stack>

      {formats.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(formats.error)}
        </Alert>
      )}

      {formats.isPending ? (
        <CircularProgress aria-label={sq.errors.loading} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small" aria-label={t.title}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.template}</TableCell>
                <TableCell align="right">{t.columns.randomDigits}</TableCell>
                <TableCell>{t.columns.note}</TableCell>
                <TableCell>{t.columns.createdAt}</TableCell>
                <TableCell>{t.columns.status}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(formats.data ?? []).map((f) => (
                <TableRow key={f.id} hover sx={rowSx(f.isActive)}>
                  <TableCell sx={{ fontFamily: 'monospace' }}>{f.template}</TableCell>
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
              {formats.isSuccess && formats.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={6}>{t.empty}</TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      )}

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
