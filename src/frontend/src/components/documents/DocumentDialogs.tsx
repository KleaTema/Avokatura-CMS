import {
  Alert,
  AppBar,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  Stack,
  TextField,
  Toolbar,
  Typography,
} from '@mui/material'
import { Download, X } from 'lucide-react'
import { useState } from 'react'
import { saveBlob, useDeleteDocument, type DocumentItem, type FetchedFile } from '../../features/documents/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useFullScreenDialog } from '../ui/dialog'

const t = sq.cases.documents
const MAX_REASON = 300

/**
 * "Shiko": the PDF or image fetched as a blob (with the login header), shown full screen from a blob URL; "Shkarko"
 * saves the same blob. The caller creates the blob URL when it opens the dialog and revokes it when it closes
 * (event handlers, not effects: React StrictMode's second effect run would revoke a URL still in use).
 */
export function DocumentPreviewDialog({
  title,
  file,
  url,
  onClose,
}: {
  title: string
  file: FetchedFile
  url: string
  onClose: () => void
}) {
  const isImage = file.blob.type.startsWith('image/')

  return (
    <Dialog open fullScreen onClose={onClose} aria-labelledby="document-preview-title">
      <AppBar position="static" color="primary" elevation={0}>
        <Toolbar sx={{ gap: 1 }}>
          <Typography id="document-preview-title" variant="h3" component="h2" sx={{ flex: 1, minWidth: 0, color: 'inherit' }} noWrap>
            {t.previewTitle(title)}
          </Typography>
          <Button color="inherit" startIcon={<Download size={20} aria-hidden />} onClick={() => saveBlob(file)}>
            {t.download}
          </Button>
          <IconButton color="inherit" aria-label={t.close} onClick={onClose} autoFocus>
            <X size={22} />
          </IconButton>
        </Toolbar>
      </AppBar>
      <Box sx={{ flex: 1, minHeight: 0, bgcolor: 'grey.200', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        {isImage ? (
          <Box component="img" src={url} alt={title} sx={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }} />
        ) : (
          <Box component="iframe" src={url} title={title} sx={{ border: 0, width: '100%', height: '100%' }} />
        )}
      </Box>
    </Dialog>
  )
}

/**
 * "Hiq" (document.delete): the reason is required; the document leaves the list, stays in the history and in the
 * archive. A document that is the decision or the security measure's record cannot be removed: the API's 409 message.
 */
export function DeleteDocumentDialog({
  caseId,
  document,
  onClose,
  onDone,
}: {
  caseId: string
  document: DocumentItem
  onClose: () => void
  onDone: () => void
}) {
  const remove = useDeleteDocument(caseId)
  const fullScreen = useFullScreenDialog()
  const [reason, setReason] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)

  const submit = async () => {
    if (remove.isPending) return
    const text = reason.trim()
    if (!text) {
      setFieldError(t.reasonRequired)
      return
    }

    if (text.length > MAX_REASON) {
      setFieldError(t.reasonTooLong)
      return
    }

    try {
      await remove.mutateAsync({ documentId: document.publicId, reason: text })
      onDone()
    } catch {
      // the message is shown from remove.error
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="delete-document-title">
      <DialogTitle id="delete-document-title">{t.deleteTitle}</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {remove.isError && <Alert severity="error">{problemMessage(remove.error)}</Alert>}
          <DialogContentText>{t.deleteExplain(document.title)}</DialogContentText>
          <TextField
            label={t.reason}
            value={reason}
            onChange={(e) => {
              setReason(e.target.value)
              setFieldError(null)
            }}
            required
            autoFocus
            multiline
            minRows={2}
            error={!!fieldError}
            helperText={fieldError ?? `${reason.length} / ${MAX_REASON}`}
            slotProps={{ htmlInput: { maxLength: MAX_REASON } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" color="error" onClick={() => void submit()} disabled={remove.isPending}>
          {t.confirmDelete}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
