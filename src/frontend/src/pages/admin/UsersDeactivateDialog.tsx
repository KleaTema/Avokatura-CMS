import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useDeactivateUser, type AdminUser } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'

const t = sq.adminUsers.deactivate

export function UsersDeactivateDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const deactivate = useDeactivateUser()
  const assignments = Number(user.currentAssignments)

  const onConfirm = () => deactivate.mutate(user.publicId, { onSuccess: onClose })

  return (
    <Dialog open onClose={onClose} aria-labelledby="deactivate-title" aria-describedby="deactivate-text">
      <DialogTitle id="deactivate-title">{t.title}</DialogTitle>
      <DialogContent>
        {deactivate.isError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {problemMessage(deactivate.error)}
          </Alert>
        )}
        <DialogContentText id="deactivate-text">
          {t.question(`${user.firstName} ${user.lastName}`)}
        </DialogContentText>
        <Alert severity={assignments > 0 ? 'warning' : 'info'} sx={{ mt: 2 }}>
          {assignments > 0 ? t.assignments(assignments) : t.noAssignments}
        </Alert>
        <DialogContentText sx={{ mt: 2 }}>{t.historyNote}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} autoFocus>
          {sq.common.cancel}
        </Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={deactivate.isPending}>
          {t.button}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
