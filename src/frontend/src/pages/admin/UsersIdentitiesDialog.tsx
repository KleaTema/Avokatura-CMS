import {
  Alert,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import {
  useAllowIdentity,
  useUnlinkIdentity,
  useUserIdentities,
  type AdminUser,
  type UserIdentity,
} from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'

const t = sq.adminUsers.identities

/**
 * Task 051: the user's sign-in accounts — one per provider at which they signed in (linked by e-mail at the first
 * login). "Shkëput" BLOCKS that one account (from the next request; never re-linked by e-mail), confirmed and
 * audited; "Lejo sërish" lifts the block with a reason. To shut the person out completely: deactivate the user.
 */
export function UsersIdentitiesDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const identities = useUserIdentities(user.publicId)
  const unlink = useUnlinkIdentity()
  const allow = useAllowIdentity()
  const [confirming, setConfirming] = useState<UserIdentity | null>(null)
  const [allowing, setAllowing] = useState<UserIdentity | null>(null)
  const [reason, setReason] = useState('')
  const name = `${user.firstName} ${user.lastName}`

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="identities-title">
      <DialogTitle id="identities-title">{t.title(name)}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {t.hint}
        </Typography>
        <Alert severity="info" sx={{ mb: 1 }}>
          {t.blockExplained}
        </Alert>
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
                i.blockedAt ? (
                  <Button size="small" onClick={() => setAllowing(i)} aria-label={`${t.allow}: ${i.provider}`}>
                    {t.allow}
                  </Button>
                ) : (
                  <Button size="small" color="error" onClick={() => setConfirming(i)} aria-label={`${t.unlink}: ${i.provider}`}>
                    {t.unlink}
                  </Button>
                )
              }
            >
              <ListItemText
                primary={
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>{i.provider}</span>
                    {i.blockedAt && <Chip size="small" color="error" variant="outlined" label={t.blocked(formatDateTime(i.blockedAt))} />}
                  </Stack>
                }
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

      <Dialog open={allowing !== null} onClose={() => setAllowing(null)} fullWidth maxWidth="xs" aria-labelledby="allow-title">
        <DialogTitle id="allow-title">{allowing ? t.allowTitle(allowing.provider, name) : ''}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography variant="body2">{t.allowText}</Typography>
            <TextField
              required
              autoFocus
              multiline
              minRows={2}
              label={t.reason}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              slotProps={{ htmlInput: { maxLength: 500 } }}
            />
            {allow.isError && <Alert severity="error">{problemMessage(allow.error)}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAllowing(null)}>{sq.common.cancel}</Button>
          <Button
            variant="contained"
            disabled={allow.isPending || reason.trim().length < 5}
            onClick={() =>
              allowing &&
              allow.mutate(
                { id: user.publicId, identityId: allowing.publicId, reason: reason.trim() },
                {
                  onSuccess: () => {
                    setAllowing(null)
                    setReason('')
                  },
                },
              )
            }
          >
            {t.allow}
          </Button>
        </DialogActions>
      </Dialog>
    </Dialog>
  )
}
