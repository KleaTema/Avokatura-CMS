import { Alert, CircularProgress, Stack, TextField, Typography } from '@mui/material'
import { useHandoffDestination, type HandoffValues } from '../features/cases/api'
import { useCourtLevels, useCourts } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { OptionPicker } from './OptionPicker'
import { AppSelect } from './ui/AppSelect'

const t = sq.handoff

/** The server's minimum for the reason of a hand-picked office (HandoffService.MinReasonLength). */
export const MIN_HANDOFF_REASON = 10

export type HandoffErrors = Partial<Record<keyof HandoffValues, string>>

/**
 * Whether the destination can be sent: level and court chosen, and the office resolved from the court — or, when no
 * office covers the court, chosen with a reason.
 */
export function useHandoffComplete(values: HandoffValues): boolean {
  const destination = useHandoffDestination(values.courtId)
  if (!values.levelCode || !values.courtId || !destination.data) return false
  if (destination.data.office) return true
  return values.officePublicId !== '' && values.reason.trim().length >= MIN_HANDOFF_REASON
}

/**
 * "Përcjellja" (task 053c): Shkalla e radhës ▼ → Gjykata ▼ (the courts of that level; any level, a lower one too),
 * then the receiving office shown read-only ("Do t'i përcillet: Zyra …"). Only when no office covers the court: the
 * office and a reason.
 */
export function HandoffFields({
  values,
  onChange,
  errors = {},
  autoFocus,
}: {
  values: HandoffValues
  onChange: (values: HandoffValues) => void
  errors?: HandoffErrors
  autoFocus?: boolean
}) {
  const levels = useCourtLevels()
  const courts = useCourts()
  const destination = useHandoffDestination(values.courtId)
  const ofLevel = (courts.data ?? []).filter((c) => c.courtLevelCode === values.levelCode)

  return (
    <Stack spacing={2}>
      <AppSelect
        required
        label={t.level}
        value={values.levelCode}
        // another level: its court (and the office chosen for it) go
        onChange={(levelCode) => onChange({ ...values, levelCode, courtId: '', officePublicId: '', reason: '' })}
        options={(levels.data ?? []).map((l) => ({ value: l.code, label: l.name }))}
        error={!!errors.levelCode}
        helperText={errors.levelCode ?? t.lowerAllowed}
        autoFocus={autoFocus}
      />
      <OptionPicker
        required
        label={t.court}
        options={ofLevel.map((c) => ({ value: String(c.id), label: c.name }))}
        value={values.courtId}
        onChange={(courtId) => onChange({ ...values, courtId, officePublicId: '', reason: '' })}
        disabled={!values.levelCode}
        error={!!errors.courtId}
        helperText={errors.courtId ?? (values.levelCode ? undefined : t.chooseLevelFirst)}
      />
      {values.courtId && destination.isPending && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }} role="status">
          <CircularProgress size={18} aria-hidden />
          <Typography variant="body2">{t.resolving}</Typography>
        </Stack>
      )}
      {destination.data?.office && (
        <Alert severity="info" icon={false} role="status">
          {t.resolvedOffice(destination.data.office.name)}
        </Alert>
      )}
      {destination.data && !destination.data.office && (
        <>
          <Alert severity="warning">{t.unmapped}</Alert>
          <AppSelect
            required
            label={t.office}
            value={values.officePublicId}
            onChange={(officePublicId) => onChange({ ...values, officePublicId })}
            options={destination.data.offices.map((o) => ({ value: o.publicId, label: o.name }))}
            error={!!errors.officePublicId}
            helperText={errors.officePublicId}
          />
          <TextField
            required
            label={t.reason}
            value={values.reason}
            onChange={(e) => onChange({ ...values, reason: e.target.value })}
            multiline
            minRows={2}
            error={!!errors.reason}
            helperText={errors.reason ?? t.reasonHint(MIN_HANDOFF_REASON)}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
        </>
      )}
    </Stack>
  )
}

/** Server field names (POST /decision, PUT /handoff) → the field here. */
export const HANDOFF_SERVER_FIELDS: Record<string, keyof HandoffValues> = {
  nextCourtLevelCode: 'levelCode',
  nextCourtId: 'courtId',
  receivingOfficePublicId: 'officePublicId',
  reason: 'reason',
  handoffReason: 'reason',
}
