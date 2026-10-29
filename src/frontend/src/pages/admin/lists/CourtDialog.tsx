import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { useSaveCourt, type AdminCourt } from '../../../features/admin/api'
import { useCourtLevels } from '../../../features/reference/api'
import { sq } from '../../../i18n/sq'
import { problemMessage } from '../../../lib/problem'
import { useForbiddenRedirect } from './helpers'

const t = sq.adminLists

const schema = z.object({
  name: z.string().trim().min(1, t.validation.nameRequired(250)).max(250, t.validation.nameRequired(250)),
  courtLevel: z.string().min(1, t.validation.courtLevelRequired),
  city: z.string().trim().max(100, t.validation.cityTooLong),
  isForeign: z.boolean(),
})

type FormValues = z.infer<typeof schema>

/** Add (court = null) or edit a court; every field stays editable. */
export function CourtDialog({ court, onClose }: { court: AdminCourt | null; onClose: () => void }) {
  const isNew = court === null
  const levels = useCourtLevels()
  const save = useSaveCourt()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: court?.name ?? '',
      courtLevel: court?.courtLevel ?? '',
      city: court?.city ?? '',
      isForeign: court?.isForeign ?? false,
    },
  })

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        id: court ? Number(court.id) : null,
        body: { name: v.name, courtLevel: v.courtLevel, city: v.city || null, isForeign: v.isForeign },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="court-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="court-title">
          {isNew ? t.createTitle(t.lists.courts) : t.editTitle(t.lists.courts)}
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {(formError ?? levels.isError) && (
              <Alert severity="error">{formError ?? problemMessage(levels.error)}</Alert>
            )}
            <Controller
              name="name"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.name}
                  required
                  autoFocus
                  error={!!errors.name}
                  helperText={errors.name?.message}
                />
              )}
            />
            <Controller
              name="courtLevel"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  value={levels.data ? field.value : ''}
                  select
                  label={t.columns.courtLevel}
                  required
                  disabled={levels.isPending}
                  error={!!errors.courtLevel}
                  helperText={errors.courtLevel?.message}
                >
                  {(levels.data ?? []).map((l) => (
                    <MenuItem key={l.code} value={l.code}>
                      {l.name}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="city"
              control={control}
              render={({ field }) => (
                <TextField {...field} label={t.columns.city} error={!!errors.city} helperText={errors.city?.message} />
              )}
            />
            <Controller
              name="isForeign"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                  label={t.columns.isForeign}
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
