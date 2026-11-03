import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { ApiError } from '../api/client'
import {
  caseKeys,
  useAddAssistant,
  useAssignableAssistants,
  useRemoveAssistant,
  type AssignableAssistant,
  type CaseDetail,
} from '../features/cases/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { matches } from '../lib/search'
import { useSaveOutcome } from './caseData/useSaveOutcome'
import { AppAutocomplete } from './ui/AppAutocomplete'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.cases.assistants

/** "Shto ndihmës": the office's legal assistants not yet on the case, with how many open cases each assists. */
export function AddAssistantDialog({
  detail,
  onClose,
  onAdded,
}: {
  detail: CaseDetail
  onClose: () => void
  onAdded: (name: string) => void
}) {
  const caseId = detail.header.publicId
  const options = useAssignableAssistants(caseId, true)
  const add = useAddAssistant(caseId)
  const fullScreen = useFullScreenDialog()
  // "Rifresko" also reloads the picker; the choice stays
  const outcome = useSaveOutcome(caseId, undefined, {}, [caseKeys.picker(caseId, 'assistants')])
  const [picked, setPicked] = useState<AssignableAssistant | null>(null)
  const [fieldError, setFieldError] = useState<string | null>(null)

  const submit = async () => {
    if (add.isPending) return
    outcome.reset()
    if (!picked) {
      setFieldError(t.required)
      return
    }
    try {
      await add.mutateAsync({ userPublicId: picked.publicId, rowVersion: Number(detail.header.rowVersion) })
      onAdded(picked.fullName)
    } catch (error) {
      // the one field error belongs under the picker; everything else goes through the shared outcome
      if (error instanceof ApiError && error.status === 400 && error.problem?.errors?.userPublicId) {
        setFieldError(error.problem.errors.userPublicId.join(' '))
      } else {
        outcome.fail(error)
      }
    }
  }

  const list = options.data ?? []
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="add-assistant-title">
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(ev) => {
          ev.preventDefault()
          void submit()
        }}
      >
        <DialogTitle id="add-assistant-title">{t.addTitle}</DialogTitle>
        <DialogContent>
          {options.isError ? (
            <Alert severity="error">{problemMessage(options.error)}</Alert>
          ) : options.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : (
            <Stack spacing={2} sx={{ pt: 1 }}>
              {outcome.feedback}
              {list.length === 0 && <Alert severity="info">{t.none}</Alert>}
              <AppAutocomplete<AssignableAssistant>
                options={list}
                value={picked}
                onChange={(value) => {
                  setPicked(value)
                  setFieldError(null)
                }}
                getOptionLabel={(o) => o.fullName}
                getOptionKey={(o) => o.publicId}
                filterOptions={(all, input) => all.filter((o) => matches(input, o.fullName))}
                renderOptionLabel={(o) => (
                  <Box>
                    <Typography variant="body1">{o.fullName}</Typography>
                    <Typography variant="caption" component="p" color="text.secondary">
                      {t.workload(Number(o.activeAssignments))}
                    </Typography>
                  </Box>
                )}
                label={t.picker}
                required
                autoFocus
                error={!!fieldError}
                helperText={fieldError ?? (picked ? t.workload(Number(picked.activeAssignments)) : undefined)}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button
            type="submit"
            variant="contained"
            color="secondary"
            disabled={add.isPending || outcome.refreshing || options.isError}
          >
            {t.confirm}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

/** Confirmation before removing an assistant (destructive: red; the focus starts on "Anulo"). */
export function RemoveAssistantDialog({
  detail,
  assistant,
  onClose,
  onRemoved,
}: {
  detail: CaseDetail
  assistant: { userPublicId: string; name: string }
  onClose: () => void
  onRemoved: () => void
}) {
  const caseId = detail.header.publicId
  const remove = useRemoveAssistant(caseId)
  const fullScreen = useFullScreenDialog()
  const outcome = useSaveOutcome(caseId)

  const confirm = async () => {
    if (remove.isPending) return
    outcome.reset()
    try {
      await remove.mutateAsync({ userPublicId: assistant.userPublicId, rowVersion: Number(detail.header.rowVersion) })
      onRemoved()
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth fullScreen={fullScreen} aria-labelledby="remove-assistant-title">
      <DialogTitle id="remove-assistant-title">{t.removeTitle}</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {outcome.feedback}
          <DialogContentText>{t.removeQuestion(assistant.name)}</DialogContentText>
        </Stack>
      </DialogContent>
      <DialogActions>
        {/* no field to focus: the safe choice gets the focus */}
        <Button onClick={onClose} autoFocus>
          {sq.common.cancel}
        </Button>
        <Button variant="contained" color="error" onClick={() => void confirm()} disabled={remove.isPending || outcome.refreshing}>
          {t.remove}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
