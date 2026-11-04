import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useArchiveDossier, type CaseDetail } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { useFullScreenDialog } from './ui/dialog'

const t = sq.cases.page

/** The dossier's code: the case code without its level ("2026-123456/2" → "2026-123456"). */
const dossierCodeOf = (caseCode: string) => caseCode.replace(/\/\d+$/, '')

/**
 * "Arkivo çështjen?" (task 038): after a final decision, or later from the Vendimi tab. Shown only to a user with
 * decision.close_final (the caller checks permissions[]); the whole dossier becomes read-only.
 */
export function ArchiveDossierDialog({
  detail,
  onClose,
  onArchived,
}: {
  detail: CaseDetail
  onClose: () => void
  onArchived: () => void
}) {
  const fullScreen = useFullScreenDialog()
  const archive = useArchiveDossier(detail.header.publicId, detail.header.dossierPublicId)
  const confirm = () =>
    archive.mutate(Number(detail.header.rowVersion), {
      onSuccess: onArchived,
    })

  return (
    <Dialog open onClose={onClose} fullScreen={fullScreen} aria-labelledby="archive-title" aria-describedby="archive-text">
      <DialogTitle id="archive-title">{t.archiveDialog.title}</DialogTitle>
      <DialogContent>
        {archive.isError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {problemMessage(archive.error)}
          </Alert>
        )}
        <DialogContentText id="archive-text" sx={{ mb: 1 }}>
          {t.archiveDialog.text(dossierCodeOf(detail.header.caseCode))}
        </DialogContentText>
        <DialogContentText>
          <strong>{t.archiveDialog.irreversible}</strong>
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t.archiveDialog.later}</Button>
        <Button variant="contained" color="secondary" onClick={confirm} disabled={archive.isPending}>
          {t.archiveDialog.confirm}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

/** After a result that sends the case back for retrial: Protokoll continues it (the lawyer has no action here). */
export function RetrialDialog({ onClose }: { onClose: () => void }) {
  return (
    <Dialog open onClose={onClose} aria-labelledby="retrial-title" aria-describedby="retrial-text">
      <DialogTitle id="retrial-title">{t.retrialDialog.title}</DialogTitle>
      <DialogContent>
        <DialogContentText id="retrial-text">{t.retrialDialog.text}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} autoFocus>
          {t.retrialDialog.ok}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
