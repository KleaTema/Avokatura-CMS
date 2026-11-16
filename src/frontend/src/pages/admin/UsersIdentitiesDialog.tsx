import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useUnlinkIdentity, useUserIdentities, type AdminUser, type UserIdentity } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'

const t = sq.adminUsers.identities

/**
 * Task 051: the user's sign-in identities — one per provider at which they signed in (linked by e-mail at the first
 * login) — and "Shkëput" after a confirmation (audited). Unlinked, the next login at that provider links again by
 * e-mail, or is refused if the e-mail no longer matches an active user.
 */
export function UsersIdentitiesDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const identities = useUserIdentities(user.publicId)
  const unlink = useUnlinkIdentity()
  const [confirming, setConfirming] = useState<UserIdentity | null>(null)
  const name = `${user.firstName} ${user.lastName}`

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="identities-title">
      <DialogTitle id="identities-title">{t.title(name)}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {t.hint}
        </Typography>
        {identities.isPending && <CircularProgress aria-label={sq.errors.loading} />}
        {identities.isError && <Alert severity="error">{problemMessage(identities.error)}</Alert>}
        {unlink.isError && <Alert severity="error">{problemMessage(unlink.error)}</Alert>}
        {identities.isSuccess && identities.data.length === 0 && <Alert severity="info">{t.none}</Alert>}
        <List dense>
          {(identities.data ?? []).map((i) => (
            <ListItem
              key={i.publicId}
              disableGutters
              secondaryAction={
                <Button size="small" color="error" onClick={() => setConfirming(i)} aria-label={`${t.unlink}: ${i.provider}`}>
                  {t.unlink}
                </Button>
              }
            >
              <ListItemText
                primary={i.provider}
                secondary={t.line(i.emailAtLink ?? '—', formatDateTime(i.linkedAt), i.lastLoginAt ? formatDateTime(i.lastLoginAt) : '—')}
              />
            </ListItem>
          ))}
        </List>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.close}</Button>
      </DialogActions>

      <Dialog open={confirming !== null} onClose={() => setConfirming(null)} aria-labelledby="unlink-title">
        <DialogTitle id="unlink-title">{confirming ? t.confirm(confirming.provider, name) : ''}</DialogTitle>
        <DialogContent>
          <Typography variant="body2">{t.confirmText}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirming(null)}>{sq.common.cancel}</Button>
          <Button
            color="error"
            variant="contained"
            disabled={unlink.isPending}
            onClick={() =>
              confirming &&
              unlink.mutate({ id: user.publicId, identityId: confirming.publicId }, { onSuccess: () => setConfirming(null) })
            }
          >
            {t.unlink}
          </Button>
        </DialogActions>
      </Dialog>
    </Dialog>
  )
}
