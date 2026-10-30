import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { Controller, useForm, type Path, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../../api/client'
import { useNotifyNotClosed } from '../../features/cases/api'
import { useCourtLevels, useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { isPickerValue, utcIsoOf, type PickerValue } from '../../lib/pickerValues'
import { problemMessage } from '../../lib/problem'
import { CourtPicker } from '../CourtPicker'
import { AppDatePicker } from '../AppDatePicker'
import { AppTimePicker } from '../AppTimePicker'
import { checkHearing } from './registrationValues'

const t = sq.cases.continue.notifyDialog
const v = sq.cases.register.validation

interface NotifyValues {
  summonsNumber: string
  courtId: string
  hearingDate: PickerValue
  hearingTime: PickerValue
}

const schema = z
  .object({
    summonsNumber: z.string().refine((s) => s.trim().length > 0 && s.trim().length <= 50, v.summonsNumberRequired),
    courtId: z.string().min(1, v.courtRequired),
    hearingDate: z.custom<PickerValue>(isPickerValue),
    hearingTime: z.custom<PickerValue>(isPickerValue),
  })
  .superRefine((x, ctx) =>
    checkHearing(x.hearingDate, x.hearingTime, (path, message) =>
      ctx.addIssue({ code: 'custom', path: [path], message }),
    ),
  )

const SERVER_FIELDS: Record<string, Path<NotifyValues>> = {
  summonsNumber: 'summonsNumber',
  courtId: 'courtId',
  hearingAt: 'hearingDate',
}

/**
 * A next-level summons arrived but the level is not closed in the system: tell the primary lawyer and the Kryetar
 * (POST notify-not-closed; the server sends it at most once a day for the same summons).
 */
export function NotifyNotClosedDialog({
  previousCasePublicId,
  caseCode,
  onClose,
}: {
  previousCasePublicId: string
  caseCode: string
  onClose: () => void
}) {
  const courts = useCourts()
  const levels = useCourtLevels()
  const notify = useNotifyNotClosed(previousCasePublicId)
  const [result, setResult] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<NotifyValues>({
    resolver: zodResolver(schema) as Resolver<NotifyValues>,
    defaultValues: { summonsNumber: '', courtId: '', hearingDate: null, hearingTime: null },
  })

  const onSubmit = handleSubmit(async (x) => {
    if (notify.isPending) return
    setFormError(null)
    try {
      const response = await notify.mutateAsync({
        summonsNumber: x.summonsNumber.trim(),
        courtId: Number(x.courtId),
        hearingAt: utcIsoOf(x.hearingDate, x.hearingTime),
      })
      setResult(response.alreadyNotified ? t.already(formatDateTime(response.notifiedAt)) : t.sent)
    } catch (error) {
      if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
        const unmatched: string[] = []
        for (const [key, messages] of Object.entries(error.problem.errors)) {
          const path = SERVER_FIELDS[key]
          if (path) setError(path, { type: 'server', message: messages.join(' ') })
          else unmatched.push(...messages)
        }
        if (unmatched.length > 0) setFormError(unmatched.join(' '))
      } else {
        setFormError(problemMessage(error))
      }
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="notify-title">
      <Box component="form" onSubmit={onSubmit} noValidate>
        <DialogTitle id="notify-title">
          {t.title} — {caseCode}
        </DialogTitle>
        <DialogContent>
          {result ? (
            <Alert severity="success" role="status">
              {result}
            </Alert>
          ) : (
            <Stack spacing={2}>
              <DialogContentText>{t.intro}</DialogContentText>
              {formError && <Alert severity="error">{formError}</Alert>}
              <Controller
                name="summonsNumber"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    inputRef={field.ref}
                    label={t.summonsNumber}
                    required
                    autoFocus
                    error={!!errors.summonsNumber}
                    helperText={errors.summonsNumber?.message}
                    slotProps={{ htmlInput: { maxLength: 50 } }}
                  />
                )}
              />
              <Controller
                name="courtId"
                control={control}
                render={({ field }) => (
                  <CourtPicker
                    courts={courts.data ?? []}
                    levels={levels.data ?? []}
                    levelCode=""
                    value={field.value}
                    onChange={(court) => field.onChange(court ? String(court.id) : '')}
                    inputRef={field.ref}
                    label={t.court}
                    required
                    error={!!errors.courtId}
                    helperText={errors.courtId?.message}
                  />
                )}
              />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <Controller
                  name="hearingDate"
                  control={control}
                  render={({ field }) => (
                    <AppDatePicker
                      value={field.value}
                      onChange={field.onChange}
                      inputRef={field.ref}
                      label={t.hearingDate}
                      required
                      error={!!errors.hearingDate}
                      helperText={errors.hearingDate?.message}
                    />
                  )}
                />
                <Controller
                  name="hearingTime"
                  control={control}
                  render={({ field }) => (
                    <AppTimePicker
                      value={field.value}
                      onChange={field.onChange}
                      inputRef={field.ref}
                      label={t.hearingTime}
                      required
                      error={!!errors.hearingTime}
                      helperText={errors.hearingTime?.message}
                    />
                  )}
                />
              </Stack>
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          {result ? (
            <Button onClick={onClose} autoFocus>
              {sq.common.close}
            </Button>
          ) : (
            <>
              <Button onClick={onClose}>{sq.common.cancel}</Button>
              <Button type="submit" variant="contained" disabled={notify.isPending}>
                {t.send}
              </Button>
            </>
          )}
        </DialogActions>
      </Box>
    </Dialog>
  )
}
