import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import { Send } from 'lucide-react'
import { useState } from 'react'
import { ApiError } from '../api/client'
import { emptyHandoff, useCaseHandoff, useChangeHandoff, type CaseHandoff, type HandoffValues } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { formatDateTime } from '../lib/dates'
import { problemMessage } from '../lib/problem'
import { HANDOFF_SERVER_FIELDS, HandoffFields, useHandoffComplete, type HandoffErrors } from './HandoffFields'
import { SectionCard } from './ui/SectionCard'
import { SuccessToast } from './ui/SuccessToast'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.handoff

/**
 * The open handoff of a closed level (task 053c): where the case goes next, and — for whoever records the decision
 * (canChange) — "Ndrysho përcjelljen" until the next level is registered. Nothing when there is no open handoff.
 */
export function HandoffCard({ caseId, enabled }: { caseId: string; enabled: boolean }) {
  const handoff = useCaseHandoff(caseId, enabled)
  const [editing, setEditing] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  if (!handoff.data) return null
  const h = handoff.data

  return (
    <>
      <SectionCard
        title={t.cardTitle}
        actions={
          h.canChange && (
            <Button variant="outlined" startIcon={<Send size={18} aria-hidden />} onClick={() => setEditing(true)}>
              {t.change}
            </Button>
          )
        }
      >
        <Stack spacing={0.5}>
          <Typography>{t.cardText(h.courtLevelName, h.courtName, h.office.name)}</Typography>
          {h.overrideReason && (
            <Typography variant="body2" color="text.secondary">
              {t.cardReason(h.overrideReason)}
            </Typography>
          )}
          <Typography variant="body2" color="text.secondary">
            {formatDateTime(h.createdAt)}
          </Typography>
        </Stack>
      </SectionCard>
      {editing && (
        <ChangeHandoffDialog
          caseId={caseId}
          current={h}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false)
            setToast(t.saved)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </>
  )
}

function ChangeHandoffDialog({
  caseId,
  current,
  onClose,
  onSaved,
}: {
  caseId: string
  current: CaseHandoff
  onClose: () => void
  onSaved: () => void
}) {
  const fullScreen = useFullScreenDialog()
  const change = useChangeHandoff(caseId)
  const [values, setValues] = useState<HandoffValues>({
    ...emptyHandoff,
    levelCode: current.courtLevelCode,
    courtId: String(current.courtId),
  })
  const [errors, setErrors] = useState<HandoffErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const complete = useHandoffComplete(values)

  const save = async () => {
    setErrors({})
    setFormError(null)
    try {
      await change.mutateAsync(values)
      onSaved()
    } catch (error) {
      const found: HandoffErrors = {}
      const fieldErrors = error instanceof ApiError ? (error.problem?.errors ?? {}) : {}
      for (const [key, messages] of Object.entries(fieldErrors)) {
        const field = HANDOFF_SERVER_FIELDS[key]
        if (field) found[field] = messages.join(' ')
      }

      setErrors(found)
      if (Object.keys(found).length === 0) setFormError(problemMessage(error))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="change-handoff-title">
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <DialogTitle id="change-handoff-title">{t.changeTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography variant="body2">{t.changeIntro}</Typography>
            {formError && <Alert severity="error">{formError}</Alert>}
            <HandoffFields values={values} onChange={setValues} errors={errors} autoFocus />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" color="secondary" disabled={!complete || change.isPending}>
            {t.save}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}
