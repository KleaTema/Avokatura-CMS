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
  FormHelperText,
  Radio,
  RadioGroup,
  Stack,
  Step,
  StepButton,
  Stepper,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { Controller, useForm, useWatch, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../api/client'
import {
  caseKeys,
  useRecordDecision,
  useUpdateDecision,
  type CaseDetail,
} from '../features/cases/api'
import { useDecisionDocuments } from '../features/documents/api'
import { useDecisionResults } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { formatDateTime, todayInTirane } from '../lib/dates'
import {
  STEP_FIELDS,
  VALUE_TYPES,
  amountField,
  areaField,
  decisionDefaults,
  decisionRequest,
  decisionSchema,
  fieldOfServer,
  MAX_DISPOSITIVE,
  MAX_NUMBER,
  MAX_OTHER_TEXT,
  type DecisionFormValues,
} from '../lib/decisionForm'
import { CURRENCIES } from '../lib/money'
import { problemMessage } from '../lib/problem'
import { AppDatePicker } from './AppDatePicker'
import { MoneyField } from './caseData/MoneyField'
import { UploadQueue } from './documents/UploadQueue'
import { AppSelect } from './ui/AppSelect'
import { ConflictAlert } from './ui/ConflictAlert'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'
import { StatusChip } from './ui/StatusChip'

const t = sq.cases.page.closeDialog
const v = sq.cases.page.decision

/** The hearings listed by a 409 case.open_hearings. */
const openHearingsSchema = z.array(z.object({ publicId: z.string(), scheduledAt: z.string(), hearingType: z.string() }))
type OpenHearing = z.infer<typeof openHearingsSchema>[number]

export type CloseCaseMode = 'record' | 'update'

/**
 * Record the decision and close the level (POST /decision, task 036) or complete it later (PUT /decision, task 037):
 * one dialog with a stepper — Rezultati, Vlerat, Statusi, Vendimi i arsyetuar — over the case page, which never
 * changes. "Vazhdo" checks the step's fields; a server field error opens the step that shows it. Scheduled hearings
 * (409 case.open_hearings) are listed with "Anuloji dhe mbyll", which sends again with cancelOpenHearings.
 */
export function CloseCaseDialog({
  detail,
  mode,
  initialStep = 0,
  onClose,
  onDone,
}: {
  detail: CaseDetail
  mode: CloseCaseMode
  initialStep?: number
  onClose: () => void
  /** after a successful save: the API's suggestions (RETRIAL_EXPECTED, ARCHIVE_SUGGESTED) */
  onDone: (suggestions: string[]) => void
}) {
  const caseId = detail.header.publicId
  const fullScreen = useFullScreenDialog()
  const qc = useQueryClient()
  const results = useDecisionResults()
  const record = useRecordDecision(caseId)
  const update = useUpdateDecision(caseId)
  const saving = record.isPending || update.isPending
  const decision = detail.decision
  const appealKind = detail.data.appealKind
  const alreadyReceived = mode === 'update' && (decision?.decisionReceived ?? false)

  const [step, setStep] = useState(initialStep)
  const [formError, setFormError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const [openHearings, setOpenHearings] = useState<OpenHearing[] | null>(null)
  // a decision uploaded here: selected as soon as its check says "Gati"
  const [uploaded, setUploaded] = useState<string | null>(null)

  const isOther = (code: string) => (results.data ?? []).some((r) => r.code === code && r.isOther)
  const {
    control,
    handleSubmit,
    trigger,
    setError,
    setValue,
    getValues,
    formState: { errors },
  } = useForm<DecisionFormValues>({
    resolver: zodResolver(
      decisionSchema({ isOther, actDate: detail.data.actDate ?? null, today: todayInTirane() }),
    ) as Resolver<DecisionFormValues>,
    defaultValues: decisionDefaults(mode === 'update' ? decision : null),
  })
  const resultCode = useWatch({ control, name: 'resultCode' })
  const currency = useWatch({ control, name: 'currency' })
  const isFinal = useWatch({ control, name: 'isFinal' })
  const isAppealed = useWatch({ control, name: 'isAppealed' })
  const isRecursed = useWatch({ control, name: 'isRecursed' })
  const received = useWatch({ control, name: 'decisionReceived' })
  const documentId = useWatch({ control, name: 'documentPublicId' })

  const documents = useDecisionDocuments(caseId, uploaded !== null)
  const uploadedDocument = (documents.data ?? []).find((d) => d.publicId === uploaded)
  useEffect(() => {
    if (uploadedDocument?.scanStatus === 'CLEAN' && getValues('documentPublicId') !== uploadedDocument.publicId) {
      setValue('documentPublicId', uploadedDocument.publicId, { shouldValidate: true })
    }
  }, [uploadedDocument, getValues, setValue])
  const waiting = uploadedDocument !== undefined && uploadedDocument.scanStatus === 'PENDING'

  const last = STEP_FIELDS.length - 1
  const next = async () => {
    if (await trigger(STEP_FIELDS[step])) setStep((s) => Math.min(last, s + 1))
  }

  const send = async (values: DecisionFormValues, cancelOpenHearings: boolean) => {
    setFormError(null)
    setConflict(false)
    try {
      const result =
        mode === 'record'
          ? await record.mutateAsync(decisionRequest(values, Number(detail.header.rowVersion), cancelOpenHearings))
          : await update.mutateAsync(decisionRequest(values, Number(decision?.rowVersion ?? 0)))
      onDone(result.suggestions)
    } catch (error) {
      if (!(error instanceof ApiError)) {
        setFormError(problemMessage(error))
        return
      }

      const problem = error.problem
      if (error.status === 409 && problem?.code === 'case.open_hearings') {
        const listed = openHearingsSchema.safeParse((problem as Record<string, unknown>).hearings)
        setOpenHearings(listed.success ? listed.data : [])
        return
      }

      if (error.status === 409 && problem?.code === 'case.changed') {
        setConflict(true)
        return
      }

      // field errors: on their fields, the first one's step opened
      let firstStep: number | null = null
      const unmatched: string[] = []
      for (const [key, messages] of Object.entries(problem?.errors ?? {})) {
        const target = fieldOfServer(key)
        if (target) {
          setError(target.field, { type: 'server', message: messages.join(' ') })
          firstStep = firstStep === null ? target.step : Math.min(firstStep, target.step)
        } else {
          unmatched.push(...messages)
        }
      }

      if (firstStep !== null) setStep(firstStep)
      if (unmatched.length > 0 || firstStep === null) setFormError(unmatched.join(' ') || problemMessage(error))
    }
  }

  const submit = handleSubmit(
    (values) => send(values, false),
    (invalid) => {
      // the first step with an error is opened
      const first = STEP_FIELDS.findIndex((fields) => fields.some((f) => invalid[f]))
      if (first >= 0) setStep(first)
    },
  )
  const cancelAndClose = handleSubmit((values) => send(values, true))

  const title = mode === 'record' ? t.titleRecord : t.titleUpdate

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md" fullScreen={fullScreen} aria-labelledby="close-case-title">
      <Box component="form" onSubmit={(e) => void submit(e)} onKeyDown={preventEnterSubmit} noValidate>
        <DialogTitle id="close-case-title">
          {title} — {detail.header.caseCode}
        </DialogTitle>
        <DialogContent>
          {openHearings ? (
            <Stack spacing={2}>
              <Alert severity="warning">{t.openHearingsTitle}</Alert>
              <DialogContentText>{t.openHearingsText}</DialogContentText>
              <Box component="ul" sx={{ m: 0, pl: 3 }}>
                {openHearings.map((h) => (
                  <li key={h.publicId}>
                    {formatDateTime(h.scheduledAt)} — {h.hearingType}
                  </li>
                ))}
              </Box>
            </Stack>
          ) : (
            <Stack spacing={2.5}>
              <Stepper activeStep={step} nonLinear={mode === 'update'} alternativeLabel sx={{ pt: 1 }}>
                {t.steps.map((label, index) => (
                  <Step key={label} completed={index < step}>
                    <StepButton onClick={() => (index < step || mode === 'update' ? setStep(index) : void next())}>
                      {label}
                    </StepButton>
                  </Step>
                ))}
              </Stepper>
              {conflict && (
                <ConflictAlert
                  refreshing={false}
                  onRefresh={() => {
                    setConflict(false)
                    void qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) })
                  }}
                />
              )}
              {formError && <Alert severity="error">{formError}</Alert>}

              {/* 1. Rezultati */}
              {step === 0 && (
                <Stack spacing={2}>
                  <Controller
                    name="resultCode"
                    control={control}
                    render={({ field }) => (
                      <AppSelect
                        required
                        label={v.result}
                        value={field.value}
                        onChange={(value) => {
                          field.onChange(value)
                          if (!isOther(value)) setValue('resultOtherText', '')
                        }}
                        inputRef={field.ref}
                        error={!!errors.resultCode}
                        helperText={errors.resultCode?.message}
                        options={(results.data ?? []).map((r) => ({ value: r.code, label: r.name }))}
                        autoFocus
                      />
                    )}
                  />
                  {isOther(resultCode) && (
                    <Controller
                      name="resultOtherText"
                      control={control}
                      render={({ field }) => (
                        <TextField
                          {...field}
                          inputRef={field.ref}
                          label={t.otherText}
                          required
                          error={!!errors.resultOtherText}
                          helperText={errors.resultOtherText?.message}
                          slotProps={{ htmlInput: { maxLength: MAX_OTHER_TEXT } }}
                        />
                      )}
                    />
                  )}
                  <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                    <Controller
                      name="decisionNumber"
                      control={control}
                      render={({ field }) => (
                        <TextField
                          {...field}
                          inputRef={field.ref}
                          label={v.number}
                          error={!!errors.decisionNumber}
                          helperText={errors.decisionNumber?.message}
                          slotProps={{ htmlInput: { maxLength: MAX_NUMBER } }}
                          sx={{ flex: 1 }}
                        />
                      )}
                    />
                    <Controller
                      name="decisionDate"
                      control={control}
                      render={({ field }) => (
                        <AppDatePicker
                          value={field.value}
                          onChange={field.onChange}
                          inputRef={field.ref}
                          label={v.date}
                          error={!!errors.decisionDate}
                          helperText={errors.decisionDate?.message}
                          maxDate={dayjs(todayInTirane())}
                          minDate={detail.data.actDate ? dayjs(detail.data.actDate) : undefined}
                          sx={{ flex: 1 }}
                        />
                      )}
                    />
                  </Stack>
                  <Controller
                    name="dispositive"
                    control={control}
                    render={({ field }) => (
                      <TextField
                        {...field}
                        inputRef={field.ref}
                        label={v.dispositive}
                        multiline
                        minRows={3}
                        error={!!errors.dispositive}
                        helperText={errors.dispositive?.message}
                        slotProps={{ htmlInput: { maxLength: MAX_DISPOSITIVE } }}
                      />
                    )}
                  />
                </Stack>
              )}

              {/* 2. Vlerat: money and m² side by side per type, one currency */}
              {step === 1 && (
                <Stack spacing={2}>
                  <Controller
                    name="currency"
                    control={control}
                    render={({ field }) => (
                      <AppSelect
                        required
                        label={v.currency}
                        value={field.value}
                        onChange={field.onChange}
                        inputRef={field.ref}
                        options={CURRENCIES.map((c) => ({ value: c, label: c }))}
                        sx={{ width: { xs: '100%', sm: 160 } }}
                      />
                    )}
                  />
                  {VALUE_TYPES.map((type) => (
                    <Box key={type} component="fieldset" sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
                      <Typography component="legend" variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                        {v.valueTypes[type]}
                      </Typography>
                      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                        <Controller
                          name={amountField(type)}
                          control={control}
                          render={({ field }) => (
                            <MoneyField
                              value={field.value}
                              onChange={field.onChange}
                              onBlur={field.onBlur}
                              inputRef={field.ref}
                              label={`${v.valueTypes[type]} — ${v.amount}`}
                              unit={currency}
                              error={!!errors[amountField(type)]}
                              helperText={errors[amountField(type)]?.message}
                            />
                          )}
                        />
                        <Controller
                          name={areaField(type)}
                          control={control}
                          render={({ field }) => (
                            <MoneyField
                              value={field.value}
                              onChange={field.onChange}
                              onBlur={field.onBlur}
                              inputRef={field.ref}
                              label={`${v.valueTypes[type]} — ${v.area}`}
                              unit="m²"
                              error={!!errors[areaField(type)]}
                              helperText={errors[areaField(type)]?.message}
                            />
                          )}
                        />
                      </Stack>
                    </Box>
                  ))}
                  <Box sx={{ maxWidth: { sm: 'calc(50% - 8px)' } }}>
                    <Controller
                      name="courtCostsAmount"
                      control={control}
                      render={({ field }) => (
                        <MoneyField
                          value={field.value}
                          onChange={field.onChange}
                          onBlur={field.onBlur}
                          inputRef={field.ref}
                          label={v.courtCosts}
                          unit={currency}
                          error={!!errors.courtCostsAmount}
                          helperText={errors.courtCostsAmount?.message}
                        />
                      )}
                    />
                  </Box>
                </Stack>
              )}

              {/* 3. Statusi: the level's appeal checkbox OR the final form, never both */}
              {step === 2 && (
                <Stack spacing={1}>
                  {appealKind === 'ANKIM' && (
                    <Controller
                      name="isAppealed"
                      control={control}
                      render={({ field }) => (
                        <FormControlLabel
                          control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                          label={v.appealed}
                          disabled={isFinal}
                        />
                      )}
                    />
                  )}
                  {appealKind === 'REKURS' && (
                    <Controller
                      name="isRecursed"
                      control={control}
                      render={({ field }) => (
                        <FormControlLabel
                          control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                          label={v.recursed}
                          disabled={isFinal}
                        />
                      )}
                    />
                  )}
                  <Controller
                    name="isFinal"
                    control={control}
                    render={({ field }) => (
                      <FormControlLabel
                        control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                        label={v.final}
                        disabled={isAppealed || isRecursed}
                      />
                    )}
                  />
                  <FormHelperText error={!!errors.isFinal}>
                    {errors.isFinal?.message ?? (appealKind ? t.finalHint : t.noAppealHint)}
                  </FormHelperText>
                  {(errors.isAppealed || errors.isRecursed) && (
                    <FormHelperText error>{errors.isAppealed?.message ?? errors.isRecursed?.message}</FormHelperText>
                  )}
                </Stack>
              )}

              {/* 4. Vendimi i arsyetuar: received → upload the PDF here (VENDIM) or pick one of the dossier */}
              {step === 3 && (
                <Stack spacing={2}>
                  <Controller
                    name="decisionReceived"
                    control={control}
                    render={({ field }) => (
                      <FormControlLabel
                        control={
                          <Switch checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />
                        }
                        label={t.receivedSwitch}
                        disabled={alreadyReceived}
                      />
                    )}
                  />
                  {alreadyReceived && <Typography variant="body2" color="text.secondary">{t.receivedLocked}</Typography>}
                  {!received && <Alert severity="info">{t.notReceivedNote}</Alert>}
                  {received && (
                    <>
                      <Typography variant="h5" component="h3">
                        {t.uploadTitle}
                      </Typography>
                      <UploadQueue caseId={caseId} presetType="VENDIM" single secondaryButton onUploaded={setUploaded} />
                      {waiting && (
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }} role="status">
                          <CircularProgress size={18} aria-hidden />
                          <Typography variant="body2">{t.waiting}</Typography>
                        </Stack>
                      )}
                      <Typography variant="h5" component="h3">
                        {t.pickTitle}
                      </Typography>
                      {documents.isPending ? (
                        <CircularProgress size={24} aria-label={sq.errors.loading} />
                      ) : (documents.data ?? []).length === 0 ? (
                        <Typography color="text.secondary">{t.noDocuments}</Typography>
                      ) : (
                        <RadioGroup
                          aria-label={t.pickTitle}
                          value={documentId}
                          onChange={(e) => setValue('documentPublicId', e.target.value, { shouldValidate: true })}
                        >
                          {(documents.data ?? []).map((d) => (
                            <FormControlLabel
                              key={d.publicId}
                              value={d.publicId}
                              disabled={d.scanStatus !== 'CLEAN'}
                              control={<Radio />}
                              label={
                                <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                                  <span>
                                    {d.title} · {d.level} · {formatDateTime(d.uploadedAt)}
                                  </span>
                                  <StatusChip kind="scan" value={d.scanStatus} />
                                </Stack>
                              }
                            />
                          ))}
                        </RadioGroup>
                      )}
                      {errors.documentPublicId && <FormHelperText error>{errors.documentPublicId.message}</FormHelperText>}
                    </>
                  )}
                </Stack>
              )}
            </Stack>
          )}
        </DialogContent>
        <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          {openHearings ? (
            <>
              <Button onClick={() => setOpenHearings(null)}>{t.back}</Button>
              <Button variant="contained" color="secondary" disabled={saving} onClick={() => void cancelAndClose()}>
                {t.cancelAndClose}
              </Button>
            </>
          ) : (
            <>
              {step > 0 && <Button onClick={() => setStep((s) => s - 1)}>{t.back}</Button>}
              {step < last && (
                <Button variant="outlined" onClick={() => void next()}>
                  {t.next}
                </Button>
              )}
              {(step === last || mode === 'update') && (
                <Button type="submit" variant="contained" color="secondary" disabled={saving || waiting}>
                  {mode === 'record' ? t.confirmRecord : t.confirmUpdate}
                </Button>
              )}
            </>
          )}
        </DialogActions>
      </Box>
    </Dialog>
  )
}
