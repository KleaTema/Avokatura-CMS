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
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { OptionPicker } from '../../components/OptionPicker'
import { useSaveOffice, type AdminOffice } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminOffices

const buildSchema = (isNew: boolean) =>
  z.object({
    // the code of an existing office is shown disabled and never sent
    code: isNew ? z.string().trim().regex(/^[A-Z0-9]{2,10}$/, t.validation.codeInvalid) : z.string(),
    name: z.string().trim().min(1, t.validation.nameRequired).max(200, t.validation.nameRequired),
    officeType: z.string().min(1, t.validation.typeRequired),
    parentOfficeId: z.string(),
  })

type FormValues = z.infer<ReturnType<typeof buildSchema>>

/** The office itself and everything below it — none of them can become its parent (the server checks too). */
function selfAndDescendants(officeId: string, offices: AdminOffice[]) {
  const result = new Set([officeId])
  let added = true
  while (added) {
    added = false
    for (const o of offices) {
      if (o.parent && result.has(o.parent.publicId) && !result.has(o.publicId)) {
        result.add(o.publicId)
        added = true
      }
    }
  }
  return result
}

/** Add (office = null) or edit an office. The code is a stable identifier and cannot change after creation. */
export function OfficeDialog({
  office,
  offices,
  onClose,
}: {
  office: AdminOffice | null
  offices: AdminOffice[]
  onClose: () => void
}) {
  const isNew = office === null
  const save = useSaveOffice()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(buildSchema(isNew)),
    defaultValues: {
      code: office?.code ?? '',
      name: office?.name ?? '',
      officeType: office?.officeType ?? '',
      parentOfficeId: office?.parent?.publicId ?? '',
    },
  })

  const excluded = office ? selfAndDescendants(office.publicId, offices) : new Set<string>()
  const currentParent = office?.parent?.publicId
  // active offices, plus the current parent so the picker can still show it
  const parentOptions = offices.filter(
    (o) => !excluded.has(o.publicId) && (o.isActive || o.publicId === currentParent),
  )

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        publicId: office?.publicId ?? null,
        body: { code: v.code, name: v.name, officeType: v.officeType, parentOfficeId: v.parentOfficeId || null },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="office-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="office-title">{isNew ? t.createTitle : t.editTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {formError && <Alert severity="error">{formError}</Alert>}
            <Controller
              name="code"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  onChange={(e) => field.onChange(e.target.value.toUpperCase())}
                  label={t.columns.code}
                  required={isNew}
                  disabled={!isNew}
                  autoFocus={isNew}
                  error={!!errors.code}
                  helperText={errors.code?.message ?? (isNew ? t.codeHint : t.codeImmutable)}
                />
              )}
            />
            <Controller
              name="name"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.name}
                  required
                  autoFocus={!isNew}
                  error={!!errors.name}
                  helperText={errors.name?.message}
                />
              )}
            />
            <Controller
              name="officeType"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  select
                  label={t.columns.officeType}
                  required
                  error={!!errors.officeType}
                  helperText={errors.officeType?.message}
                >
                  {Object.entries(t.officeTypes).map(([code, label]) => (
                    <MenuItem key={code} value={code}>
                      {label}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="parentOfficeId"
              control={control}
              render={({ field }) => (
                <OptionPicker
                  options={parentOptions.map((o) => ({
                    value: o.publicId,
                    label: o.isActive ? o.name : `${o.name}${sq.adminLists.inactiveSuffix}`,
                  }))}
                  value={field.value}
                  onChange={field.onChange}
                  label={t.columns.parent}
                  helperText={field.value ? undefined : t.noParent}
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
