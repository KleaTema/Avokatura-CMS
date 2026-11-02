import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormHelperText,
  FormLabel,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import {
  caseKeys,
  hearingTypeName,
  useAddHearing,
  useCompleteHearing,
  useHearingDefaults,
  useHearings,
  useUpdateHearing,
  type HearingView,
  type HearingWarning,
} from '../features/cases/api'
import { useHearingTypes, type HearingType } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { formatDateTime } from '../lib/dates'
import { pickerValuesOfUtc, utcIsoOf, type PickerValue } from '../lib/pickerValues'
import { AppDatePicker } from './AppDatePicker'
import { AppTimePicker } from './AppTimePicker'
import { useSaveOutcome } from './caseData/useSaveOutcome'
import { checkHearing } from './registration/registrationValues'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.cases.hearings
const MAX_DESCRIPTION = 4000
const MAX_OTHER_TYPE = 200
const OUTCOMES = ['HELD', 'POSTPONED', 'CANCELLED']

/** What a dialog hands back after a save: the toast text and the API's warnings (shown on the tab). */
export interface HearingSaved {
  message: string
  warnings: HearingWarning[]
}

interface DialogProps {
  caseId: string
  onClose: () => void
  onDone: (saved: HearingSaved) => void
}

// ---------------------------------------------------------------- date, time and type of a hearing

interface Schedule {
  date: PickerValue
  time: PickerValue
  typeCode: string
  otherText: string
}

type ScheduleErrors = Partial<Record<keyof Schedule, string>>

const scheduleOf = (hearing: Pick<HearingView, 'scheduledAt' | 'type' | 'otherTypeText'> | null, typeCode = ''): Schedule => ({
  ...(hearing ? pickerValuesOfUtc(hearing.scheduledAt) : { date: null, time: null }),
  typeCode: hearing?.type.code ?? typeCode,
  otherText: hearing?.otherTypeText ?? '',
})

/** The same checks as the registration form (task 016): date and time required and existing in Tirane, "Tjetër" needs its text. */
function checkSchedule(s: Schedule, isOther: boolean): ScheduleErrors {
  const errors: ScheduleErrors = {}
  checkHearing(s.date, s.time, (path, message) => {
    errors[path === 'hearingDate' ? 'date' : 'time'] = message
  })
  if (!s.typeCode) errors.typeCode = sq.cases.register.validation.hearingTypeRequired
  const other = s.otherText.trim()
  if (isOther && (other.length === 0 || other.length > MAX_OTHER_TYPE)) {
    errors.otherText = sq.cases.register.validation.hearingOtherRequired
  }
  return errors
}

/** The active types; a hearing's own type is kept in the list even when it was deactivated since. */
function useTypeOptions(current?: HearingView['type']) {
  const types = useHearingTypes()
  const list: HearingType[] = types.data ?? []
  const options =
    current && current.code && !list.some((x) => x.code === current.code)
      ? [...list, { code: current.code, name: current.name, isOther: false }]
      : list
  return { options, isOther: (code: string) => options.some((x) => x.code === code && x.isOther), loading: types.isPending }
}

function ScheduleFields({
  value,
  onChange,
  errors,
  options,
  isOther,
  autoFocus,
}: {
  value: Schedule
  onChange: (next: Schedule) => void
  errors: ScheduleErrors
  options: HearingType[]
  isOther: boolean
  autoFocus?: boolean
}) {
  return (
    <Stack spacing={2}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <AppDatePicker
          value={value.date}
          onChange={(date) => onChange({ ...value, date })}
          label={t.date}
          required
          autoFocus={autoFocus}
          error={!!errors.date}
          helperText={errors.date}
          fullWidth
          sx={{ flex: 1 }}
        />
        <AppTimePicker
          value={value.time}
          onChange={(time) => onChange({ ...value, time })}
          label={t.time}
          required
          error={!!errors.time}
          helperText={errors.time}
          fullWidth
          sx={{ width: { xs: '100%', sm: 150 }, flexShrink: 0 }}
        />
      </Stack>
      <TextField
        select
        label={t.hearingType}
        value={value.typeCode}
        onChange={(e) => onChange({ ...value, typeCode: e.target.value })}
        required
        error={!!errors.typeCode}
        helperText={errors.typeCode}
      >
        {options.map((x) => (
          <MenuItem key={x.code} value={x.code}>
            {x.name}
          </MenuItem>
        ))}
      </TextField>
      {isOther && (
        <TextField
          label={t.otherType}
          value={value.otherText}
          onChange={(e) => onChange({ ...value, otherText: e.target.value })}
          required
          error={!!errors.otherText}
          helperText={errors.otherText}
          slotProps={{ htmlInput: { maxLength: MAX_OTHER_TYPE } }}
        />
      )}
    </Stack>
  )
}

function DescriptionField({
  label,
  value,
  onChange,
  error,
  hint,
  required,
  autoFocus,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  error?: string
  hint?: string
  required?: boolean
  autoFocus?: boolean
}) {
  return (
    <TextField
      label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      required={required}
      autoFocus={autoFocus}
      multiline
      minRows={3}
      error={!!error}
      helperText={error ?? hint}
      slotProps={{ htmlInput: { maxLength: MAX_DESCRIPTION } }}
    />
  )
}

const descriptionError = (text: string, required: boolean): string | undefined =>
  required && text.trim().length === 0
    ? t.descriptionRequired
    : text.trim().length > MAX_DESCRIPTION
      ? t.descriptionTooLong
      : undefined

const noErrors = (errors: object) => Object.values(errors).every((x) => x === undefined)

/** The dialog frame shared by the three: a form (Enter does not submit), full screen on a phone, one confirm button. */
function HearingDialog({
  id,
  title,
  confirm,
  gold,
  busy,
  onClose,
  onSubmit,
  children,
}: {
  id: string
  title: string
  confirm: string
  gold?: boolean
  busy: boolean
  onClose: () => void
  onSubmit: () => void
  children: ReactNode
}) {
  const fullScreen = useFullScreenDialog()
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby={id}>
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(e) => {
          e.preventDefault()
          onSubmit()
        }}
      >
        <DialogTitle id={id}>{title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} sx={{ pt: 1 }}>
            {children}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" color={gold ? 'secondary' : 'primary'} disabled={busy}>
            {confirm}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

const Loading = () => (
  <Box sx={{ display: 'flex', justifyContent: 'center', py: 3 }}>
    <CircularProgress aria-label={sq.errors.loading} />
  </Box>
)

/** The current copy of a hearing from the case's list — after "Rifresko" it carries the new rowVersion. */
const useCurrentHearing = (caseId: string, hearingId: string) =>
  useHearings(caseId).data?.find((h) => h.publicId === hearingId) ?? null

// ---------------------------------------------------------------- "Shto seancë"

/** hearing.add: a new SCHEDULED hearing; the type starts as the last hearing's (new-defaults). */
export function AddHearingDialog({ caseId, onClose, onDone }: DialogProps) {
  const defaults = useHearingDefaults(caseId, true)
  const types = useTypeOptions()
  const ready = !defaults.isPending && !types.loading
  return (
    <AddHearingForm
      key={ready ? 'ready' : 'loading'}
      caseId={caseId}
      onClose={onClose}
      onDone={onDone}
      ready={ready}
      initialType={defaults.data?.type?.code ?? ''}
      initialOther={defaults.data?.otherTypeText ?? ''}
      types={types}
    />
  )
}

function AddHearingForm({
  caseId,
  onClose,
  onDone,
  ready,
  initialType,
  initialOther,
  types,
}: DialogProps & {
  ready: boolean
  initialType: string
  initialOther: string
  types: ReturnType<typeof useTypeOptions>
}) {
  const add = useAddHearing(caseId)
  const outcome = useSaveOutcome(caseId, undefined, {}, [caseKeys.hearings(caseId)])
  const [schedule, setSchedule] = useState<Schedule>({ ...scheduleOf(null, initialType), otherText: initialOther })
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<ScheduleErrors & { notes?: string }>({})
  const isOther = types.isOther(schedule.typeCode)

  const submit = async () => {
    if (add.isPending) return
    outcome.reset()
    const found = { ...checkSchedule(schedule, isOther), notes: descriptionError(notes, false) }
    setErrors(found)
    if (!noErrors(found)) return
    try {
      const r = await add.mutateAsync({
        at: utcIsoOf(schedule.date, schedule.time),
        hearingTypeCode: schedule.typeCode,
        otherTypeText: isOther ? schedule.otherText.trim() : null,
        description: notes.trim() || null,
      })
      onDone({ message: t.added, warnings: r.warnings })
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <HearingDialog
      id="add-hearing-title"
      title={t.addTitle}
      confirm={t.confirmAdd}
      busy={add.isPending || outcome.refreshing || !ready}
      onClose={onClose}
      onSubmit={() => void submit()}
    >
      {outcome.feedback}
      {!ready ? (
        <Loading />
      ) : (
        <>
          <ScheduleFields
            value={schedule}
            onChange={setSchedule}
            errors={errors}
            options={types.options}
            isOther={isOther}
            autoFocus
          />
          <DescriptionField label={t.notes} value={notes} onChange={setNotes} error={errors.notes} />
        </>
      )}
    </HearingDialog>
  )
}

// ---------------------------------------------------------------- "Përfundo seancën"

/**
 * hearing.edit, the main flow: how the hearing ended, its description and — for "U zhvillua" / "U shty", shown by
 * default — the next hearing. One confirm, one request (POST complete with `next`).
 */
export function CompleteHearingDialog({ caseId, hearingId, onClose, onDone }: DialogProps & { hearingId: string }) {
  const hearing = useCurrentHearing(caseId, hearingId)
  const defaults = useHearingDefaults(caseId, true)
  const types = useTypeOptions(hearing?.type)
  const ready = hearing !== null && !defaults.isPending && !types.loading
  if (!ready) {
    return (
      <HearingDialog id="complete-hearing-title" title={t.completeTitle} confirm={t.confirmComplete} gold busy onClose={onClose} onSubmit={() => {}}>
        <Loading />
      </HearingDialog>
    )
  }
  // the hearing's own description; when it has none, the latest written one as a starting point
  const own = hearing.description ?? ''
  const prefilled = own === '' ? (defaults.data?.description ?? '') : ''
  return (
    <CompleteHearingForm
      caseId={caseId}
      hearing={hearing}
      initialDescription={own || prefilled}
      prefilled={prefilled !== ''}
      types={types}
      onClose={onClose}
      onDone={onDone}
    />
  )
}

function CompleteHearingForm({
  caseId,
  hearing,
  initialDescription,
  prefilled,
  types,
  onClose,
  onDone,
}: DialogProps & {
  hearing: HearingView
  initialDescription: string
  prefilled: boolean
  types: ReturnType<typeof useTypeOptions>
}) {
  const complete = useCompleteHearing(caseId)
  const outcome = useSaveOutcome(caseId, undefined, {}, [caseKeys.hearings(caseId)])
  const [result, setResult] = useState('')
  const [description, setDescription] = useState(initialDescription)
  // the next hearing: on by default for HELD / POSTPONED, off for CANCELLED, until the user switches it
  const [nextChoice, setNextChoice] = useState<boolean | null>(null)
  const [next, setNext] = useState<Schedule>({ ...scheduleOf(null, hearing.type.code), otherText: hearing.otherTypeText ?? '' })
  const [errors, setErrors] = useState<{ outcome?: string; description?: string; next?: ScheduleErrors }>({})
  const withNext = nextChoice ?? (result === 'HELD' || result === 'POSTPONED')
  const nextIsOther = types.isOther(next.typeCode)

  const submit = async () => {
    if (complete.isPending) return
    outcome.reset()
    const nextErrors = withNext ? checkSchedule(next, nextIsOther) : {}
    const found = {
      outcome: result ? undefined : t.outcomeRequired,
      description: descriptionError(description, result !== 'CANCELLED'),
      next: noErrors(nextErrors) ? undefined : nextErrors,
    }
    setErrors(found)
    if (!noErrors(found)) return
    try {
      const r = await complete.mutateAsync({
        hearingId: hearing.publicId,
        body: {
          outcome: result,
          description: description.trim() || null,
          next: withNext
            ? {
                at: utcIsoOf(next.date, next.time),
                hearingTypeCode: next.typeCode,
                otherTypeText: nextIsOther ? next.otherText.trim() : null,
              }
            : null,
          rowVersion: Number(hearing.rowVersion),
        },
      })
      onDone({
        message: r.next ? t.completedWithNext(formatDateTime(r.next.scheduledAt)) : t.completed,
        warnings: r.warnings,
      })
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <HearingDialog
      id="complete-hearing-title"
      title={t.completeTitle}
      confirm={t.confirmComplete}
      gold
      busy={complete.isPending || outcome.refreshing}
      onClose={onClose}
      onSubmit={() => void submit()}
    >
      {outcome.feedback}
      <Typography>
        <strong>{formatDateTime(hearing.scheduledAt)}</strong> · {hearingTypeName(hearing)}
      </Typography>
      <FormControl component="fieldset" required error={!!errors.outcome}>
        <FormLabel component="legend">{t.outcome}</FormLabel>
        <RadioGroup
          row
          value={result}
          onChange={(e) => {
            setResult(e.target.value)
            setErrors((x) => ({ ...x, outcome: undefined }))
          }}
        >
          {OUTCOMES.map((o, index) => (
            <FormControlLabel
              key={o}
              value={o}
              control={<Radio autoFocus={index === 0} />}
              label={t.outcomes[o]}
            />
          ))}
        </RadioGroup>
        {errors.outcome && <FormHelperText>{errors.outcome}</FormHelperText>}
      </FormControl>
      <DescriptionField
        label={result === 'CANCELLED' ? t.descriptionOptional : t.description}
        value={description}
        onChange={setDescription}
        required={result !== 'CANCELLED'}
        error={errors.description}
        hint={prefilled ? t.descriptionPrefilled : undefined}
      />
      <Box>
        <FormControlLabel
          control={<Switch checked={withNext} onChange={(e) => setNextChoice(e.target.checked)} />}
          label={<Typography sx={{ fontWeight: 600 }}>{t.scheduleNext}</Typography>}
        />
      </Box>
      {withNext && (
        <Box component="fieldset" sx={{ border: 0, m: 0, p: 0 }} aria-label={t.scheduleNext}>
          <ScheduleFields
            value={next}
            onChange={setNext}
            errors={errors.next ?? {}}
            options={types.options}
            isOther={nextIsOther}
          />
        </Box>
      )}
    </HearingDialog>
  )
}

// ---------------------------------------------------------------- "Ndrysho"

/**
 * hearing.edit: a SCHEDULED hearing may be rescheduled (date, time, type) and its notes changed; a completed one only
 * its description. The hearing's own rowVersion — a 409 offers "Rifresko" like the case.
 */
export function EditHearingDialog({ caseId, hearingId, onClose, onDone }: DialogProps & { hearingId: string }) {
  const hearing = useCurrentHearing(caseId, hearingId)
  const types = useTypeOptions(hearing?.type)
  if (hearing === null || types.loading) {
    return (
      <HearingDialog id="edit-hearing-title" title={t.editTitle} confirm={sq.common.save} busy onClose={onClose} onSubmit={() => {}}>
        <Loading />
      </HearingDialog>
    )
  }
  return <EditHearingForm caseId={caseId} hearing={hearing} types={types} onClose={onClose} onDone={onDone} />
}

function EditHearingForm({
  caseId,
  hearing,
  types,
  onClose,
  onDone,
}: DialogProps & { hearing: HearingView; types: ReturnType<typeof useTypeOptions> }) {
  const update = useUpdateHearing(caseId)
  const outcome = useSaveOutcome(caseId, undefined, {}, [caseKeys.hearings(caseId)])
  const scheduled = hearing.status === 'SCHEDULED'
  const [schedule, setSchedule] = useState<Schedule>(scheduleOf(hearing))
  const [description, setDescription] = useState(hearing.description ?? '')
  const [errors, setErrors] = useState<ScheduleErrors & { description?: string }>({})
  const isOther = types.isOther(schedule.typeCode)

  const submit = async () => {
    if (update.isPending) return
    outcome.reset()
    const found = {
      ...(scheduled ? checkSchedule(schedule, isOther) : {}),
      description: descriptionError(description, false),
    }
    setErrors(found)
    if (!noErrors(found)) return
    try {
      const r = await update.mutateAsync({
        hearingId: hearing.publicId,
        body: {
          at: scheduled ? utcIsoOf(schedule.date, schedule.time) : null,
          hearingTypeCode: scheduled ? schedule.typeCode : null,
          otherTypeText: scheduled && isOther ? schedule.otherText.trim() : null,
          description: description.trim(),
          rowVersion: Number(hearing.rowVersion),
        },
      })
      onDone({ message: t.saved, warnings: r.warnings })
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <HearingDialog
      id="edit-hearing-title"
      title={scheduled ? t.editTitle : t.editDescriptionTitle}
      confirm={sq.common.save}
      busy={update.isPending || outcome.refreshing}
      onClose={onClose}
      onSubmit={() => void submit()}
    >
      {outcome.feedback}
      {scheduled ? (
        <ScheduleFields
          value={schedule}
          onChange={setSchedule}
          errors={errors}
          options={types.options}
          isOther={isOther}
          autoFocus
        />
      ) : (
        <Alert severity="info">{t.editDescriptionOnly}</Alert>
      )}
      <DescriptionField
        label={scheduled ? t.notes : t.description}
        value={description}
        onChange={setDescription}
        error={errors.description}
        autoFocus={!scheduled}
      />
    </HearingDialog>
  )
}
