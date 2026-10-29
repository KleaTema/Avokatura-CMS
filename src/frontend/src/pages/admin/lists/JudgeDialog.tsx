import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { OptionPicker } from '../../../components/OptionPicker'
import { useAdminCourts, useSaveJudge, type AdminJudge } from '../../../features/admin/api'
import { sq } from '../../../i18n/sq'
import { problemMessage } from '../../../lib/problem'
import { useForbiddenRedirect } from './helpers'

const t = sq.adminLists

const schema = z.object({
  firstName: z.string().trim().min(1, t.validation.firstNameRequired).max(100, t.validation.firstNameRequired),
  lastName: z.string().trim().min(1, t.validation.lastNameRequired).max(100, t.validation.lastNameRequired),
  courtId: z.string(),
  externalRef: z.string().trim().max(50, t.validation.externalRefTooLong),
})

type FormValues = z.infer<typeof schema>

/** Add (judge = null) or edit a judge. The court picker offers active courts, plus the judge's current one. */
export function JudgeDialog({ judge, onClose }: { judge: AdminJudge | null; onClose: () => void }) {
  const isNew = judge === null
  const courts = useAdminCourts()
  const save = useSaveJudge()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const currentCourtId = judge?.courtId == null ? '' : String(judge.courtId)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: judge?.firstName ?? '',
      lastName: judge?.lastName ?? '',
      courtId: currentCourtId,
      externalRef: judge?.externalRef ?? '',
    },
  })

  // the backend accepts an unchanged (since deactivated) court, so it stays selectable
  const courtOptions = (courts.data ?? []).filter((c) => c.isActive || String(c.id) === currentCourtId)

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        id: judge ? Number(judge.id) : null,
        body: {
          firstName: v.firstName,
          lastName: v.lastName,
          courtId: v.courtId ? Number(v.courtId) : null,
          externalRef: v.externalRef || null,
        },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="judge-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="judge-title">
          {isNew ? t.createTitle(t.lists.judges) : t.editTitle(t.lists.judges)}
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {(formError ?? courts.isError) && (
              <Alert severity="error">{formError ?? problemMessage(courts.error)}</Alert>
            )}
            <Controller
              name="firstName"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.firstName}
                  required
                  autoFocus
                  error={!!errors.firstName}
                  helperText={errors.firstName?.message}
                />
              )}
            />
            <Controller
              name="lastName"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.lastName}
                  required
                  error={!!errors.lastName}
                  helperText={errors.lastName?.message}
                />
              )}
            />
            <Controller
              name="courtId"
              control={control}
              render={({ field }) => (
                <OptionPicker
                  options={courtOptions.map((c) => ({
                    value: String(c.id),
                    label: c.isActive ? c.name : `${c.name}${t.inactiveSuffix}`,
                  }))}
                  value={field.value}
                  onChange={field.onChange}
                  label={t.columns.court}
                  disabled={courts.isPending}
                  error={!!errors.courtId}
                  helperText={errors.courtId?.message ?? (field.value ? undefined : t.noCourt)}
                />
              )}
            />
            <Controller
              name="externalRef"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.externalRef}
                  error={!!errors.externalRef}
                  helperText={errors.externalRef?.message}
                />
              )}
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" disabled={isSubmitting}>
            {sq.common.save}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
