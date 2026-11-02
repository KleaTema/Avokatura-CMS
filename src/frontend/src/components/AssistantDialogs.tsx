import {
  Alert,
  Autocomplete,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
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

const t = sq.cases.assistants

/** A 409 (another user changed the case): the message and "Rifresko", which reloads the case; the choice stays. */
function useConflict(caseId: string) {
  const qc = useQueryClient()
  const [conflict, setConflict] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const refresh = async () => {
    setRefreshing(true)
    try {
      await Promise.all([
        qc.refetchQueries({ queryKey: caseKeys.detail(caseId) }),
        qc.refetchQueries({ queryKey: ['assignable-assistants', caseId] }),
      ])
      setConflict(null)
    } finally {
      setRefreshing(false)
    }
  }
  const alert = conflict ? (
    <Alert
      severity="warning"
      action={
        <Button color="inherit" size="small" onClick={() => void refresh()} disabled={refreshing}>
          {t.refresh}
        </Button>
      }
    >
      {conflict}
    </Alert>
  ) : null
  return { setConflict, alert, refreshing }
}

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
  const [picked, setPicked] = useState<AssignableAssistant | null>(null)
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const { setConflict, alert, refreshing } = useConflict(caseId)

  const submit = async () => {
    if (add.isPending) return
    setFormError(null)
    setConflict(null)
    if (!picked) {
      setFieldError(t.required)
      return
    }
    try {
      await add.mutateAsync({ userPublicId: picked.publicId, rowVersion: Number(detail.header.rowVersion) })
      onAdded(picked.fullName)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) setConflict(problemMessage(error))
      else if (error instanceof ApiError && error.status === 400 && error.problem?.errors?.userPublicId) {
        setFieldError(error.problem.errors.userPublicId.join(' '))
      } else setFormError(problemMessage(error))
    }
  }

  const list = options.data ?? []
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="add-assistant-title">
      <Box
        component="form"
        noValidate
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
              {alert}
              {formError && <Alert severity="error">{formError}</Alert>}
              {list.length === 0 && <Alert severity="info">{t.none}</Alert>}
              <Autocomplete<AssignableAssistant>
                options={list}
                value={picked}
                onChange={(_, value) => {
                  setPicked(value)
                  setFieldError(null)
                }}
                getOptionLabel={(o) => o.fullName}
                isOptionEqualToValue={(a, b) => a.publicId === b.publicId}
                filterOptions={(all, state) => all.filter((o) => matches(state.inputValue, o.fullName))}
                noOptionsText={sq.search.noOptions}
                renderOption={({ key, ...props }, o) => (
                  <Box component="li" key={key} {...props}>
                    <Box>
                      <Typography variant="body1">{o.fullName}</Typography>
                      <Typography variant="caption" component="p" color="text.secondary">
                        {t.workload(Number(o.activeAssignments))}
                      </Typography>
                    </Box>
                  </Box>
                )}
                renderInput={(params) => (
                  <TextField
                    {...params}
                    label={t.picker}
                    required
                    autoFocus
                    error={!!fieldError}
                    helperText={fieldError ?? (picked ? t.workload(Number(picked.activeAssignments)) : undefined)}
                  />
                )}
              />
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" color="secondary" disabled={add.isPending || refreshing || options.isError}>
            {t.confirm}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

/** Confirmation before removing an assistant (destructive: red). */
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
  const [formError, setFormError] = useState<string | null>(null)
  const { setConflict, alert, refreshing } = useConflict(caseId)

  const confirm = async () => {
    if (remove.isPending) return
    setFormError(null)
    setConflict(null)
    try {
      await remove.mutateAsync({ userPublicId: assistant.userPublicId, rowVersion: Number(detail.header.rowVersion) })
      onRemoved()
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) setConflict(problemMessage(error))
      else setFormError(problemMessage(error))
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="remove-assistant-title">
      <DialogTitle id="remove-assistant-title">{t.removeTitle}</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {alert}
          {formError && <Alert severity="error">{formError}</Alert>}
          <DialogContentText>{t.removeQuestion(assistant.name)}</DialogContentText>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" color="error" onClick={() => void confirm()} disabled={remove.isPending || refreshing}>
          {t.remove}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
