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
} from '@mui/material'
import { Users } from 'lucide-react'
import { useId, useState } from 'react'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useParticipants } from './api'

const t = sq.chat

/**
 * "Pjesëmarrësit" (task 050): who receives this conversation's messages now — the API's participant rows — with their
 * role on the case and institution. Loaded when opened; refreshed by participants.changed.
 */
export function ParticipantsButton({ conversationId }: { conversationId: string }) {
  const [open, setOpen] = useState(false)
  const participants = useParticipants(conversationId, open)
  const titleId = useId()
  const items = participants.data ?? []

  return (
    <>
      <Button variant="outlined" size="small" startIcon={<Users size={18} aria-hidden />} onClick={() => setOpen(true)}>
        {t.participants}
      </Button>
      <Dialog open={open} onClose={() => setOpen(false)} aria-labelledby={titleId} fullWidth maxWidth="xs">
        <DialogTitle id={titleId}>{participants.data ? t.participantsTitle(items.length) : t.participants}</DialogTitle>
        <DialogContent dividers>
          {participants.isPending && <CircularProgress size={24} aria-label={sq.errors.loading} />}
          {participants.isError && <Alert severity="error">{problemMessage(participants.error) || t.participantsError}</Alert>}
          <List dense disablePadding>
            {items.map((p) => (
              <ListItem key={p.id} disableGutters>
                <ListItemText
                  primary={p.isMe ? `${p.name} (${t.you})` : p.name}
                  secondary={[p.role ? (t.roles[p.role] ?? p.role) : t.noRole, p.institutionName].filter(Boolean).join(' · ')}
                />
              </ListItem>
            ))}
          </List>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t.close}</Button>
        </DialogActions>
      </Dialog>
    </>
  )
}
