import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  FormLabel,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { Controller, useForm, useWatch, type Path } from 'react-hook-form'
import { z } from 'zod'
import { useAdminInstitutions, useAdminOffices, useCreateUser } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemCode, problemMessage } from '../../lib/problem'

const t = sq.adminUsers

const schema = z
  .object({
    email: z.string().trim().max(254, t.validation.emailInvalid).email(t.validation.emailInvalid),
    firstName: z.string().trim().min(1, t.validation.firstNameRequired).max(100, t.validation.firstNameRequired),
    lastName: z.string().trim().min(1, t.validation.lastNameRequired).max(100, t.validation.lastNameRequired),
    organizationType: z.enum(['office', 'institution']),
    officeId: z.string(),
    institutionId: z.string(),
  })
  .superRefine((v, ctx) => {
    if (v.organizationType === 'office' && !v.officeId)
      ctx.addIssue({ code: 'custom', path: ['officeId'], message: t.validation.officeRequired })
    if (v.organizationType === 'institution' && !v.institutionId)
      ctx.addIssue({ code: 'custom', path: ['institutionId'], message: t.validation.institutionRequired })
  })

type FormValues = z.infer<typeof schema>

const defaultValues: FormValues = {
  email: '',
  firstName: '',
  lastName: '',
  organizationType: 'office',
  officeId: '',
  institutionId: '',
}

// Server error codes (ProblemDetails `code`) that belong to a single form field.
const fieldOfCode: Record<string, Path<FormValues>> = {
  'user.email_invalid': 'email',
  'user.email_exists': 'email',
  'user.first_name_invalid': 'firstName',
  'user.last_name_invalid': 'lastName',
  'user.office_not_found': 'officeId',
  'user.institution_not_found': 'institutionId',
}

export function UsersCreateDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const offices = useAdminOffices(open)
  const institutions = useAdminInstitutions(open)
  const create = useCreateUser()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues })
  const organizationType = useWatch({ control, name: 'organizationType' })

  const close = () => {
    reset(defaultValues)
    setFormError(null)
    create.reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await create.mutateAsync({
        email: v.email,
        firstName: v.firstName,
        lastName: v.lastName,
        officeId: v.organizationType === 'office' ? v.officeId : null,
        institutionId: v.organizationType === 'institution' ? v.institutionId : null,
        roles: null,
      })
      close()
    } catch (error) {
      const field = fieldOfCode[problemCode(error) ?? '']
      if (field) setError(field, { message: problemMessage(error) }, { shouldFocus: true })
      else setFormError(problemMessage(error))
    }
  })

  const lookupError = offices.isError ? offices.error : institutions.isError ? institutions.error : null

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="sm" aria-labelledby="create-user-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="create-user-title">{t.create.title}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {(formError ?? lookupError) && <Alert severity="error">{formError ?? problemMessage(lookupError)}</Alert>}
            <Controller
              name="email"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.create.email}
                  type="email"
                  required
                  autoFocus
                  error={!!errors.email}
                  helperText={errors.email?.message}
                />
              )}
            />
            <Controller
              name="firstName"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.create.firstName}
                  required
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
                  label={t.create.lastName}
                  required
                  error={!!errors.lastName}
                  helperText={errors.lastName?.message}
                />
              )}
            />
            <Controller
              name="organizationType"
              control={control}
              render={({ field }) => (
                <div>
                  <FormLabel id="org-type-label">{t.create.organizationType}</FormLabel>
                  <RadioGroup {...field} row aria-labelledby="org-type-label">
                    <FormControlLabel value="office" control={<Radio />} label={t.create.office} />
                    <FormControlLabel value="institution" control={<Radio />} label={t.create.institution} />
                  </RadioGroup>
                </div>
              )}
            />
            {organizationType === 'office' ? (
              <Controller
                name="officeId"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    select
                    label={t.create.office}
                    required
                    disabled={offices.isPending}
                    error={!!errors.officeId}
                    helperText={errors.officeId?.message}
                  >
                    {(offices.data ?? []).map((o) => (
                      <MenuItem key={o.publicId} value={o.publicId}>
                        {o.name}
                      </MenuItem>
                    ))}
                  </TextField>
                )}
              />
            ) : (
              <Controller
                name="institutionId"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    select
                    label={t.create.institution}
                    required
                    disabled={institutions.isPending}
                    error={!!errors.institutionId}
                    helperText={errors.institutionId?.message}
                  >
                    {(institutions.data ?? []).map((i) => (
                      <MenuItem key={i.publicId} value={i.publicId}>
                        {i.name}
                      </MenuItem>
                    ))}
                  </TextField>
                )}
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" disabled={isSubmitting}>
            {sq.common.save}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
