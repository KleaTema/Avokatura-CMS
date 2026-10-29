import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { InstitutionPicker, type PickedInstitution } from '../../components/InstitutionPicker'
import { useSaveInstitution, type AdminInstitution } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminInstitutions
const MINISTRY = 'MINISTRY'

// Mirrors the backend; NIPT is trimmed and upper-cased before the check, as the server does.
const schema = z.object({
  name: z.string().trim().min(1, t.validation.nameRequired).max(300, t.validation.nameRequired),
  shortName: z.string().trim().max(60, t.validation.shortNameTooLong),
  institutionType: z.string().min(1, t.validation.typeRequired),
  nipt: z
    .string()
    .trim()
    .transform((v) => v.toUpperCase())
    .refine((v) => v === '' || /^[A-Z][0-9]{8}[A-Z]$/.test(v), t.validation.niptInvalid),
})

type FormInput = z.input<typeof schema>
type FormOutput = z.output<typeof schema>

/** Add (institution = null) or edit an institution. Only active ministries can be a parent; a ministry has none. */
export function InstitutionDialog({ institution, onClose }: { institution: AdminInstitution | null; onClose: () => void }) {
  const isNew = institution === null
  const save = useSaveInstitution()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const [parent, setParent] = useState<PickedInstitution | null>(institution?.parent ?? null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormInput, unknown, FormOutput>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: institution?.name ?? '',
      shortName: institution?.shortName ?? '',
      institutionType: institution?.institutionType ?? '',
      nipt: institution?.nipt ?? '',
    },
  })
  const institutionType = useWatch({ control, name: 'institutionType' })
  const isMinistry = institutionType === MINISTRY

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        publicId: institution?.publicId ?? null,
        body: {
          name: v.name,
          shortName: v.shortName || null,
          institutionType: v.institutionType,
          // the parent picker is hidden for a ministry, and a ministry never sends one
          parentInstitutionId: isMinistry ? null : (parent?.publicId ?? null),
          nipt: v.nipt || null,
        },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="institution-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="institution-title">{isNew ? t.createTitle : t.editTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {formError && <Alert severity="error">{formError}</Alert>}
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
              name="shortName"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.shortName}
                  error={!!errors.shortName}
                  helperText={errors.shortName?.message}
                />
              )}
            />
            <Controller
              name="institutionType"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  select
                  label={t.columns.institutionType}
                  required
                  error={!!errors.institutionType}
                  helperText={errors.institutionType?.message}
                >
                  {Object.entries(t.institutionTypes).map(([code, label]) => (
                    <MenuItem key={code} value={code}>
                      {label}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            {!isMinistry && (
              <InstitutionPicker
                value={parent}
                onChange={setParent}
                label={t.parentLabel}
                helperText={t.parentHint}
                onlyType={MINISTRY}
                excludeId={institution?.publicId}
              />
            )}
            <Controller
              name="nipt"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.nipt}
                  error={!!errors.nipt}
                  helperText={errors.nipt?.message ?? t.niptHint}
                  slotProps={{ htmlInput: { style: { fontFamily: 'monospace', textTransform: 'uppercase' } } }}
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
