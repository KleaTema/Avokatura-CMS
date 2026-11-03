import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormLabel,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useDelegateCase, useUndelegateCase, type CaseDetail } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { useSaveOutcome } from './caseData/useSaveOutcome'
import { AppSelect } from './ui/AppSelect'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.cases.institutions
const MODES = ['DELEGIM', 'RELATIM']

/**
 * "Delego / Relato" (case.delegate, the primary lawyer): the institution is one of the case's MONITORING institutions
 * (a party), the mode DELEGIM or RELATIM, with what changes. The API decides the rest — a court level without
 * delegation (Gjykata e Lartë) comes back as its message; a 409 offers "Rifresko".
 */
export function DelegateDialog({
  detail,
  onClose,
  onDone,
}: {
  detail: CaseDetail
  onClose: () => void
  onDone: (institutionName: string) => void
}) {
  const caseId = detail.header.publicId
  const delegate = useDelegateCase(caseId)
  const outcome = useSaveOutcome(caseId)
  const fullScreen = useFullScreenDialog()
  const monitoring = detail.institutions.filter((i) => i.mode === 'MONITORING')
  const [institution, setInstitution] = useState(monitoring.length === 1 ? monitoring[0].institutionPublicId : '')
  const [mode, setMode] = useState('DELEGIM')
  const [missing, setMissing] = useState(false)

  const submit = async () => {
    if (delegate.isPending) return
    outcome.reset()
    if (!institution) {
      setMissing(true)
      return
    }
    try {
      const result = await delegate.mutateAsync({ institutionPublicId: institution, mode, rowVersion: Number(detail.header.rowVersion) })
      onDone(result.institutionName)
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="delegate-title">
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
      >
        <DialogTitle id="delegate-title">{t.delegateTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            {outcome.feedback}
            {monitoring.length === 0 ? (
              <Alert severity="info">{t.noMonitoring}</Alert>
            ) : (
              <>
                <AppSelect
                  required
                  label={t.institution}
                  value={institution}
                  onChange={(value) => {
                    setInstitution(value)
                    setMissing(false)
                  }}
                  autoFocus
                  error={missing}
                  helperText={missing ? t.institutionRequired : undefined}
                  options={monitoring.map((i) => ({ value: i.institutionPublicId, label: i.name }))}
                />
                <FormControl component="fieldset">
                  <FormLabel component="legend">{t.mode}</FormLabel>
                  <RadioGroup row value={mode} onChange={(e) => setMode(e.target.value)}>
                    {MODES.map((m) => (
                      <FormControlLabel key={m} value={m} control={<Radio />} label={t.modes[m]} />
                    ))}
                  </RadioGroup>
                </FormControl>
              </>
            )}
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {t.explain.map((line) => (
                <Typography key={line} component="li" variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
                  {line}
                </Typography>
              ))}
            </Box>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button
            type="submit"
            variant="contained"
            color="secondary"
            disabled={delegate.isPending || outcome.refreshing || monitoring.length === 0}
          >
            {t.confirmDelegate}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

/** "Anulo delegimin": the institution monitors the case again; an optional reason goes to the history. */
export function UndelegateDialog({
  detail,
  institutionName,
  onClose,
  onDone,
}: {
  detail: CaseDetail
  institutionName: string
  onClose: () => void
  onDone: () => void
}) {
  const caseId = detail.header.publicId
  const undelegate = useUndelegateCase(caseId)
  const outcome = useSaveOutcome(caseId)
  const fullScreen = useFullScreenDialog()
  const [reason, setReason] = useState('')
  const tooLong = reason.trim().length > 500

  const submit = async () => {
    if (undelegate.isPending || tooLong) return
    outcome.reset()
    try {
      await undelegate.mutateAsync({ reason: reason.trim() || null, rowVersion: Number(detail.header.rowVersion) })
      onDone()
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="undelegate-title">
      <DialogTitle id="undelegate-title">{t.undelegateTitle}</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          {outcome.feedback}
          <DialogContentText>{t.undelegateExplain(institutionName)}</DialogContentText>
          <TextField
            label={t.reason}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            autoFocus
            multiline
            minRows={2}
            error={tooLong}
            helperText={tooLong ? t.reasonTooLong : undefined}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={undelegate.isPending || outcome.refreshing || tooLong}>
          {t.confirmUndelegate}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
