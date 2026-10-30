import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Controller, useForm, useWatch, type Control, type Resolver } from 'react-hook-form'
import { Link, useBlocker, useNavigate } from 'react-router'
import { ApiError } from '../../api/client'
import {
  hasCaseWithActNumber,
  possibleDuplicates,
  type PossibleDuplicates,
  type RegisterCaseRequest,
  type RegisterCaseResponse,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import {
  useCourtLevels,
  useCourts,
  useHearingTypes,
  useSummonsTypes,
  type Court,
} from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'
import { isFilled, todayValue, type PickerValue } from '../../lib/pickerValues'
import { CourtPicker } from '../CourtPicker'
import { AppDatePicker } from '../AppDatePicker'
import { AppTimePicker } from '../AppTimePicker'
import type { DraftPartyErrors } from '../draftParty'
import { JudgePicker } from '../JudgePicker'
import { PartyEditor } from '../PartyEditor'
import {
  buildRegistrationSchema,
  emptyRegistrationValues,
  scrollToFirstError,
  toFormPath,
  toRegisterRequest,
  type RegistrationMode,
  type RegistrationValues,
} from './registrationValues'

const t = sq.cases.register

interface DuplicateState extends PossibleDuplicates {
  sameActNumber: boolean
  values: RegistrationValues
}

/**
 * The summons form shared by "new case" (task 016) and "next level" (task 018): same fields in the order of the
 * paper summons, same 400 / 409 possible-duplicate / 201 handling and success view. The next level has no object
 * and no parties (they are copied); `above` shows what will be copied.
 * `send` posts the request and returns the created case; a 400/409 ApiError is handled here.
 */
export function RegistrationForm({
  mode,
  title,
  above,
  send: post,
  anotherLabel,
  onAnother,
}: {
  mode: RegistrationMode
  title: string
  above?: ReactNode
  send: (request: RegisterCaseRequest) => Promise<RegisterCaseResponse>
  anotherLabel: string
  /** after success; default: an empty form keeping court level, court and received date */
  onAnother?: () => void
}) {
  const me = useMe(true)
  const canContinue = mode === 'new' && (me.data?.permissions.includes('case.link_previous') ?? false)
  const navigate = useNavigate()
  const levels = useCourtLevels()
  const courts = useCourts()
  const summonsTypes = useSummonsTypes()
  const hearingTypes = useHearingTypes()

  const [busy, setBusy] = useState(false)
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [duplicate, setDuplicate] = useState<DuplicateState | null>(null)
  const [done, setDone] = useState<RegisterCaseResponse | null>(null)
  const [copied, setCopied] = useState(false)
  const leaveAllowed = useRef(false)

  const schema = useMemo(() => {
    const other = new Set((hearingTypes.data ?? []).filter((h) => h.isOther).map((h) => h.code))
    return buildRegistrationSchema(mode, (code) => other.has(code))
  }, [mode, hearingTypes.data])

  const {
    control,
    handleSubmit,
    setValue,
    setError,
    getValues,
    reset,
    formState: { errors, isDirty },
  } = useForm<RegistrationValues>({
    resolver: zodResolver(schema) as Resolver<RegistrationValues>,
    defaultValues: emptyRegistrationValues(),
  })

  const [courtLevelCode, courtId, showAllJudges, hearingTypeCode, receivedDate] = useWatch({
    control,
    name: ['courtLevelCode', 'courtId', 'showAllJudges', 'hearingTypeCode', 'receivedDate'],
  })
  const today = todayValue()
  const isOtherHearing = (hearingTypes.data ?? []).some((h) => h.code === hearingTypeCode && h.isOther)

  const courtList = useMemo(() => courts.data ?? [], [courts.data])
  const courtName = (id: string | null) => courtList.find((c) => String(c.id) === id)?.name

  // ---------------------------------------------------------------- leaving with unsaved input
  const unsaved = isDirty && done === null
  // the next-level form lives at /cases/continue?case=…: leaving it only changes the query string
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      unsaved &&
      !leaveAllowed.current &&
      (currentLocation.pathname !== nextLocation.pathname || currentLocation.search !== nextLocation.search),
  )
  useEffect(() => {
    if (!unsaved) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [unsaved])

  // ---------------------------------------------------------------- dependent pickers
  // Choosing a court sets its level; a judge of another court goes (unless all judges are shown).
  const changeCourt = (court: Court | null) => {
    const id = court ? String(court.id) : ''
    setValue('courtId', id, { shouldDirty: true, shouldValidate: !!errors.courtId })
    if (court?.courtLevelCode) {
      setValue('courtLevelCode', court.courtLevelCode, { shouldDirty: true, shouldValidate: !!errors.courtLevelCode })
    }
    const judge = getValues('judge')
    if (judge && !getValues('showAllJudges') && judge.courtId !== id) setValue('judge', null, { shouldDirty: true })
  }

  // Choosing a level filters the courts; a court of another level goes, and its judge with it.
  const changeCourtLevel = (code: string) => {
    setValue('courtLevelCode', code, { shouldDirty: true, shouldValidate: !!errors.courtLevelCode })
    const court = courtList.find((c) => String(c.id) === getValues('courtId'))
    if (court && court.courtLevelCode !== code) {
      setValue('courtId', '', { shouldDirty: true })
      setValue('judge', null, { shouldDirty: true })
    }
  }

  const toggleShowAll = (all: boolean) => {
    setValue('showAllJudges', all, { shouldDirty: true })
    const judge = getValues('judge')
    if (!all && judge && judge.courtId !== getValues('courtId')) setValue('judge', null, { shouldDirty: true })
  }

  // ---------------------------------------------------------------- submit
  const applyServerErrors = (fieldErrors: Record<string, string[]>) => {
    const unmatched: string[] = []
    for (const [key, messages] of Object.entries(fieldErrors)) {
      const path = toFormPath(key)
      if (path) setError(path, { type: 'server', message: messages.join(' ') })
      else unmatched.push(...messages)
    }
    setFormErrors(unmatched)
  }

  const send = async (values: RegistrationValues, confirm: boolean) => {
    if (busy) return // no double submit
    setBusy(true)
    setFormErrors([])
    try {
      const created = await post(toRegisterRequest(values, isOtherHearing, confirm))
      setDuplicate(null)
      setCopied(false)
      setDone(created)
    } catch (error) {
      const matches = possibleDuplicates(error)
      if (matches) {
        const sameActNumber = canContinue && (await hasCaseWithActNumber(values.actNumber))
        setDuplicate({ ...matches, sameActNumber, values })
        return
      }
      setDuplicate(null)
      if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
        applyServerErrors(error.problem.errors)
      } else {
        setFormErrors([problemMessage(error)])
      }
      scrollToFirstError()
    } finally {
      setBusy(false)
    }
  }

  const onSubmit = handleSubmit((values) => send(values, false), scrollToFirstError)

  const submitOnCtrlEnter = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      void onSubmit()
    }
  }

  const another = () => {
    if (onAnother) {
      onAnother()
      return
    }
    reset(emptyRegistrationValues(getValues()))
    setDone(null)
    setFormErrors([])
  }

  const copyCode = async () => {
    if (!done) return
    try {
      await navigator.clipboard.writeText(done.caseCode)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  if (levels.isPending || courts.isPending || hearingTypes.isPending) {
    return <CircularProgress aria-label={sq.errors.loading} />
  }

  if (done) {
    return (
      <RegistrationSuccess
        created={done}
        copied={copied}
        onCopy={copyCode}
        anotherLabel={anotherLabel}
        onAnother={another}
      />
    )
  }

  // ---------------------------------------------------------------- form
  const partyErrors = errors.parties
  const rowErrors = (count: number): (DraftPartyErrors | undefined)[] =>
    Array.from({ length: count }, (_, i) => {
      const row = partyErrors?.[i]
      return row
        ? {
            partyRoleCode: row.partyRoleCode?.message,
            kind: row.kind?.message,
            displayName: row.displayName?.message,
            identifier: row.identifier?.message,
            institution: row.institution?.message,
          }
        : undefined
    })

  return (
    <Box component="form" onSubmit={onSubmit} onKeyDown={submitOnCtrlEnter} noValidate sx={{ maxWidth: 960 }}>
      <Stack spacing={3}>
        <Typography variant="h4" component="h1">
          {title}
        </Typography>
        {above}
        {formErrors.length > 0 && (
          <Alert severity="error" data-form-error>
            {formErrors.map((message) => (
              <div key={message}>{message}</div>
            ))}
          </Alert>
        )}

        <FormSection title={t.sections.summons}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <Controller
              name="summonsNumber"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  label={t.fields.summonsNumber}
                  required
                  autoFocus
                  error={!!errors.summonsNumber}
                  helperText={errors.summonsNumber?.message}
                  slotProps={{ htmlInput: { maxLength: 50 } }}
                  sx={{ flex: 1 }}
                />
              )}
            />
            <Controller
              name="summonsTypeCode"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  select
                  label={t.fields.summonsType}
                  error={!!errors.summonsTypeCode}
                  helperText={errors.summonsTypeCode?.message}
                  sx={{ flex: 1 }}
                >
                  <MenuItem value="">{t.none}</MenuItem>
                  {(summonsTypes.data ?? []).map((s) => (
                    <MenuItem key={s.code} value={s.code}>
                      {s.name}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
          </Stack>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <Controller
              name="actNumber"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  label={t.fields.actNumber}
                  required
                  error={!!errors.actNumber}
                  helperText={errors.actNumber?.message}
                  slotProps={{ htmlInput: { maxLength: 50 } }}
                  sx={{ flex: 1 }}
                />
              )}
            />
            <DateField
              control={control}
              name="actDate"
              label={t.fields.actDate}
              error={errors.actDate?.message}
              maxDate={isFilled(receivedDate) ? receivedDate : today}
            />
            <DateField
              control={control}
              name="receivedDate"
              label={t.fields.receivedDate}
              required
              error={errors.receivedDate?.message}
              maxDate={today}
            />
          </Stack>
        </FormSection>

        <FormSection title={t.sections.court}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <Controller
              name="courtLevelCode"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  onChange={(e) => changeCourtLevel(e.target.value)}
                  select
                  label={t.fields.courtLevel}
                  required
                  error={!!errors.courtLevelCode}
                  helperText={errors.courtLevelCode?.message}
                  sx={{ flex: 1 }}
                >
                  {(levels.data ?? []).map((l) => (
                    <MenuItem key={l.code} value={l.code}>
                      {l.name}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Box sx={{ flex: 2 }}>
              <Controller
                name="courtId"
                control={control}
                render={({ field }) => (
                  <CourtPicker
                    courts={courtList}
                    levels={levels.data ?? []}
                    levelCode={courtLevelCode}
                    value={field.value}
                    onChange={changeCourt}
                    inputRef={field.ref}
                    label={t.fields.court}
                    required
                    error={!!errors.courtId}
                    helperText={errors.courtId?.message}
                  />
                )}
              />
            </Box>
          </Stack>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: { md: 'flex-start' } }}>
            <Box sx={{ flex: 2 }}>
              <Controller
                name="judge"
                control={control}
                render={({ field }) => (
                  <JudgePicker
                    courtId={courtId ? Number(courtId) : null}
                    showAll={showAllJudges}
                    courtName={courtName}
                    value={field.value}
                    onChange={field.onChange}
                    inputRef={field.ref}
                    label={t.fields.judge}
                    required
                    error={!!errors.judge}
                    helperText={errors.judge?.message}
                  />
                )}
              />
            </Box>
            <Controller
              name="showAllJudges"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  sx={{ flex: 1, mt: { md: 1 } }}
                  control={<Checkbox checked={field.value} onChange={(e) => toggleShowAll(e.target.checked)} />}
                  label={t.fields.showAllJudges}
                />
              )}
            />
          </Stack>
        </FormSection>

        <FormSection title={t.sections.hearing}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <DateField
              control={control}
              name="hearingDate"
              label={t.fields.hearingDate}
              required
              error={errors.hearingDate?.message}
            />
            <Controller
              name="hearingTime"
              control={control}
              render={({ field }) => (
                <AppTimePicker
                  value={field.value}
                  onChange={field.onChange}
                  inputRef={field.ref}
                  label={t.fields.hearingTime}
                  required
                  error={!!errors.hearingTime}
                  helperText={errors.hearingTime?.message}
                />
              )}
            />
            <Controller
              name="hearingTypeCode"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  select
                  label={t.fields.hearingType}
                  required
                  error={!!errors.hearingTypeCode}
                  helperText={errors.hearingTypeCode?.message}
                  sx={{ flex: 1 }}
                >
                  {(hearingTypes.data ?? []).map((h) => (
                    <MenuItem key={h.code} value={h.code}>
                      {h.name}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
          </Stack>
          {isOtherHearing && (
            <Controller
              name="hearingOtherText"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  label={t.fields.hearingOther}
                  required
                  error={!!errors.hearingOtherText}
                  helperText={errors.hearingOtherText?.message}
                  slotProps={{ htmlInput: { maxLength: 200 } }}
                />
              )}
            />
          )}
        </FormSection>

        {mode === 'new' && (
          <>
            <FormSection title={t.fields.objectText}>
              <Controller
                name="objectText"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    inputRef={field.ref}
                    label={t.fields.objectText}
                    required
                    multiline
                    minRows={3}
                    error={!!errors.objectText}
                    helperText={errors.objectText?.message}
                    slotProps={{ htmlInput: { maxLength: 4000 } }}
                  />
                )}
              />
            </FormSection>

            <FormSection title={t.sections.parties}>
              <Controller
                name="parties"
                control={control}
                render={({ field }) => (
                  <PartyEditor
                    value={field.value}
                    onChange={field.onChange}
                    rowErrors={rowErrors(field.value.length)}
                    error={partyErrors?.message ?? partyErrors?.root?.message}
                  />
                )}
              />
            </FormSection>
          </>
        )}

        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button type="submit" variant="contained" size="large" disabled={busy}>
            {busy ? t.submitting : t.submit}
          </Button>
          <Typography variant="body2" color="text.secondary">
            {t.shortcut}
          </Typography>
        </Stack>
      </Stack>

      {duplicate && (
        <DuplicateDialog
          duplicate={duplicate}
          busy={busy}
          onCancel={() => setDuplicate(null)}
          onRegisterAnyway={() => void send(duplicate.values, true)}
          onContinueNext={() => {
            leaveAllowed.current = true // the user chose to leave this form
            navigate(`/cases/continue?q=${encodeURIComponent(duplicate.values.actNumber.trim())}`)
          }}
        />
      )}

      <Dialog open={blocker.state === 'blocked'} onClose={() => blocker.reset?.()} aria-labelledby="leave-title">
        <DialogTitle id="leave-title">{t.leave.title}</DialogTitle>
        <DialogContent>
          <DialogContentText>{t.leave.text}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => blocker.reset?.()} autoFocus>
            {t.leave.stay}
          </Button>
          <Button color="error" onClick={() => blocker.proceed?.()}>
            {t.leave.leave}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}

export function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Paper variant="outlined" component="section" sx={{ p: 2 }}>
      <Typography variant="h6" component="h2" gutterBottom>
        {title}
      </Typography>
      <Stack spacing={2}>{children}</Stack>
    </Paper>
  )
}

/** A calendar date of the form (date picker, DD.MM.YYYY). */
function DateField({
  control,
  name,
  label,
  required,
  error,
  maxDate,
}: {
  control: Control<RegistrationValues>
  name: 'actDate' | 'receivedDate' | 'hearingDate'
  label: string
  required?: boolean
  error?: string
  maxDate?: PickerValue
}) {
  return (
    <Controller
      name={name}
      control={control}
      render={({ field }) => (
        <AppDatePicker
          value={field.value}
          onChange={field.onChange}
          inputRef={field.ref}
          label={label}
          required={required}
          error={!!error}
          helperText={error}
          maxDate={maxDate}
        />
      )}
    />
  )
}

function RegistrationSuccess({
  created,
  copied,
  onCopy,
  anotherLabel,
  onAnother,
}: {
  created: RegisterCaseResponse
  copied: boolean
  onCopy: () => void
  anotherLabel: string
  onAnother: () => void
}) {
  return (
    <Stack spacing={3} sx={{ maxWidth: 720 }}>
      <Typography variant="h4" component="h1">
        {t.success.title}
      </Typography>
      <Paper variant="outlined" sx={{ p: 3 }}>
        <Typography color="text.secondary">{t.success.codeLabel}</Typography>
        <Typography variant="h2" component="p" sx={{ fontWeight: 700, fontFamily: 'monospace', wordBreak: 'break-all', my: 1 }}>
          {created.caseCode}
        </Typography>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button variant="outlined" onClick={onCopy}>
            {t.success.copy}
          </Button>
          <Typography role="status" color="success.main">
            {copied ? t.success.copied : ''}
          </Typography>
        </Stack>
      </Paper>
      {created.warnings.length > 0 && (
        <Stack spacing={1}>
          <Typography variant="h6" component="h2">
            {t.success.warnings}
          </Typography>
          {created.warnings.map((w, i) => (
            <Alert key={`${w.code}-${i}`} severity="warning">
              {w.message}
            </Alert>
          ))}
        </Stack>
      )}
      <Stack direction="row" spacing={2}>
        <Button variant="contained" onClick={onAnother} autoFocus>
          {anotherLabel}
        </Button>
        <Button variant="outlined" component={Link} to={`/cases/${created.casePublicId}`}>
          {t.success.openCase}
        </Button>
      </Stack>
    </Stack>
  )
}

function DuplicateDialog({
  duplicate,
  busy,
  onCancel,
  onRegisterAnyway,
  onContinueNext,
}: {
  duplicate: DuplicateState
  busy: boolean
  onCancel: () => void
  onRegisterAnyway: () => void
  onContinueNext: () => void
}) {
  const d = t.duplicate
  return (
    <Dialog open onClose={onCancel} fullWidth maxWidth="md" aria-labelledby="duplicate-title">
      <DialogTitle id="duplicate-title">{d.title}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>{d.intro}</DialogContentText>
        {duplicate.matches.length > 0 && (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{d.code}</TableCell>
                <TableCell>{d.status}</TableCell>
                <TableCell>{d.court}</TableCell>
                <TableCell>{d.registeredAt}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {duplicate.matches.map((m) => (
                <TableRow key={m.caseCode}>
                  <TableCell>{m.caseCode}</TableCell>
                  <TableCell>{sq.cases.status[m.status] ?? m.status}</TableCell>
                  <TableCell>{m.courtName}</TableCell>
                  <TableCell>{formatDateTime(m.registeredAt)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {duplicate.hiddenCount > 0 && <Typography sx={{ mt: 1 }}>{d.hidden(duplicate.hiddenCount)}</Typography>}
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel} autoFocus>
          {d.cancel}
        </Button>
        {duplicate.sameActNumber && <Button onClick={onContinueNext}>{d.continueNext}</Button>}
        <Button variant="contained" onClick={onRegisterAnyway} disabled={busy}>
          {d.registerAnyway}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
