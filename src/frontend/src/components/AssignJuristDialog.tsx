import {
  Alert,
  Autocomplete,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { caseKeys, useAssignableJurists, useAssignJurist, type AssignableJurist, type CaseDetail } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { matches } from '../lib/search'
import { useSaveOutcome } from './caseData/useSaveOutcome'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.cases.institutions

/**
 * The institution user assigns its institution's jurist (institution.assign_jurist, only where the institution is
 * linked). The current jurist is shown and cannot be picked again; choosing another one replaces them.
 */
export function AssignJuristDialog({
  detail,
  institutionName,
  onClose,
  onDone,
}: {
  detail: CaseDetail
  institutionName: string
  onClose: () => void
  onDone: (juristName: string) => void
}) {
  const caseId = detail.header.publicId
  const jurists = useAssignableJurists(caseId, true)
  const assign = useAssignJurist(caseId)
  // "Rifresko" also reloads the jurists (the current one may have changed); the choice stays
  const outcome = useSaveOutcome(caseId, undefined, {}, [caseKeys.picker(caseId, 'jurists')])
  const fullScreen = useFullScreenDialog()
  const [picked, setPicked] = useState<AssignableJurist | null>(null)
  const [missing, setMissing] = useState(false)
  const list = jurists.data ?? []
  const current = list.find((j) => j.isCurrent)

  const submit = async () => {
    if (assign.isPending) return
    outcome.reset()
    if (!picked) {
      setMissing(true)
      return
    }
    try {
      await assign.mutateAsync({ userPublicId: picked.publicId, rowVersion: Number(detail.header.rowVersion) })
      onDone(picked.fullName)
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="jurist-title">
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        <DialogTitle id="jurist-title">
          {t.juristTitle}
          <Typography variant="body2" color="text.secondary" component="p">
            {institutionName}
          </Typography>
        </DialogTitle>
        <DialogContent>
          {jurists.isError ? (
            <Alert severity="error">{problemMessage(jurists.error)}</Alert>
          ) : jurists.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : (
            <Stack spacing={2} sx={{ pt: 1 }}>
              {outcome.feedback}
              {list.length === 0 && <Alert severity="info">{t.juristNone}</Alert>}
              {current && (
                <Typography variant="body2">
                  <strong>{t.juristCurrent}:</strong> {current.fullName}
                </Typography>
              )}
              <Autocomplete<AssignableJurist>
                options={list}
                value={picked}
                onChange={(_, value) => {
                  setPicked(value)
                  setMissing(false)
                }}
                getOptionLabel={(o) => o.fullName}
                getOptionDisabled={(o) => o.isCurrent}
                isOptionEqualToValue={(a, b) => a.publicId === b.publicId}
                filterOptions={(all, state) => all.filter((o) => matches(state.inputValue, o.fullName))}
                noOptionsText={sq.search.noOptions}
                renderOption={({ key, ...props }, o) => (
                  <Box component="li" key={key} {...props}>
                    <Box>
                      <Typography variant="body1">{o.fullName}</Typography>
                      {o.isCurrent && (
                        <Typography variant="caption" component="p" color="text.secondary">
                          {t.juristCurrent}
                        </Typography>
                      )}
                    </Box>
                  </Box>
                )}
                renderInput={(params) => (
                  <TextField
                    {...params}
                    label={t.juristPicker}
                    required
                    autoFocus
                    error={missing}
                    helperText={missing ? t.juristRequired : undefined}
                  />
                )}
              />
              {current && picked && <Alert severity="warning">{t.juristReplaced}</Alert>}
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button
            type="submit"
            variant="contained"
            color="secondary"
            disabled={assign.isPending || outcome.refreshing || jurists.isError}
          >
            {t.assignJurist}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}
