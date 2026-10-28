import {
  Alert,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useAdminRoles, useGrantRole, useRevokeRole, type AdminUser } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'

const t = sq.adminUsers.roles

export function UsersRolesDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const roles = useAdminRoles()
  const grant = useGrantRole()
  const revoke = useRevokeRole()
  // The mutations return the updated user, so the dialog shows the server's state without waiting for the list.
  const [current, setCurrent] = useState(user)
  const [selected, setSelected] = useState('')
  const [error, setError] = useState<string | null>(null)

  const scope = current.institution ? 'INSTITUTION' : 'OFFICE'
  const nameOf = (code: string) => roles.data?.find((r) => r.code === code)?.name ?? code
  const grantable = (roles.data ?? []).filter((r) => r.scope === scope && !current.roles.includes(r.code))
  const busy = grant.isPending || revoke.isPending

  const onGrant = async () => {
    if (!selected) return
    setError(null)
    try {
      setCurrent(await grant.mutateAsync({ id: current.publicId, role: selected }))
      setSelected('')
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  const onRevoke = async (role: string) => {
    setError(null)
    try {
      setCurrent(await revoke.mutateAsync({ id: current.publicId, role }))
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="user-roles-title">
      <DialogTitle id="user-roles-title">
        {t.title}: {current.firstName} {current.lastName}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {(error ?? roles.isError) && <Alert severity="error">{error ?? problemMessage(roles.error)}</Alert>}
          {current.roles.length === 0 ? (
            <Typography>{t.none}</Typography>
          ) : (
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
              {current.roles.map((code) => (
                <Chip
                  key={code}
                  label={nameOf(code)}
                  onDelete={busy ? undefined : () => void onRevoke(code)}
                  aria-label={`${nameOf(code)} — ${t.revoke}`}
                />
              ))}
            </Stack>
          )}
          {grantable.length === 0 ? (
            roles.isSuccess && <Typography color="text.secondary">{t.noneAvailable}</Typography>
          ) : (
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <TextField
                select
                label={t.grantLabel}
                value={selected}
                onChange={(e) => setSelected(e.target.value)}
                sx={{ flexGrow: 1 }}
              >
                {grantable.map((r) => (
                  <MenuItem key={r.code} value={r.code}>
                    {r.name}
                  </MenuItem>
                ))}
              </TextField>
              <Button variant="contained" onClick={() => void onGrant()} disabled={!selected || busy}>
                {t.grant}
              </Button>
            </Stack>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.close}</Button>
      </DialogActions>
    </Dialog>
  )
}
