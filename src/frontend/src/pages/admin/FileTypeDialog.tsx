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
import { useSaveFileType, type AdminFileType } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { tokens } from '../../theme/tokens'
import { problemMessage } from '../../lib/problem'
import { BYTES_PER_MB, MAX_SIZE_MB, megabytesToBytes, parseMegabytes } from './fileTypeSize'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminFileTypes

// Mirrors the backend checks; the server stays the real check (e.g. blocked extensions come back as a 400 title).
const schema = z.object({
  extension: z.string().trim().regex(/^[a-z0-9]+$/, t.validation.extensionInvalid),
  contentType: z
    .string()
    .trim()
    .regex(/^[A-Za-z0-9!#$%&'*+.^_`|~-]+\/[A-Za-z0-9!#$%&'*+.^_`|~-]+$/, t.validation.contentTypeInvalid),
  magicHex: z
    .string()
    .trim()
    .regex(/^[0-9a-fA-F]+$/, t.validation.magicHexInvalid)
    .refine((v) => v.length % 2 === 0 && v.length <= 32, t.validation.magicHexInvalid),
  maxSizeMb: z.string().refine((v) => {
    const mb = parseMegabytes(v)
    return Number.isFinite(mb) && mb > 0 && mb <= MAX_SIZE_MB && megabytesToBytes(mb) >= 1
  }, t.validation.maxSizeInvalid),
  appliesTo: z.string().min(1, t.validation.appliesToRequired),
  isActive: z.boolean(),
})

type FormValues = z.infer<typeof schema>

/** Add (fileType = null) or edit an allowed file type. Extension and applies-to cannot change after creation. */
export function FileTypeDialog({ fileType, onClose }: { fileType: AdminFileType | null; onClose: () => void }) {
  const isNew = fileType === null
  const save = useSaveFileType()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      extension: fileType?.extension ?? '',
      contentType: fileType?.contentType ?? '',
      magicHex: fileType?.magicHex ?? '',
      // full precision, so an unchanged value converts back to the same byte count
      maxSizeMb: fileType ? String(Number(fileType.maxSizeBytes) / BYTES_PER_MB) : '',
      appliesTo: fileType?.appliesTo ?? '',
      isActive: fileType?.isActive ?? true,
    },
  })

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        id: fileType ? Number(fileType.id) : null,
        body: {
          extension: v.extension,
          contentType: v.contentType,
          magicHex: v.magicHex,
          maxSizeBytes: megabytesToBytes(parseMegabytes(v.maxSizeMb)),
          appliesTo: v.appliesTo,
          isActive: v.isActive,
        },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="file-type-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="file-type-title">{isNew ? t.createTitle : t.editTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {formError && <Alert severity="error">{formError}</Alert>}
            <Controller
              name="extension"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  onChange={(e) => field.onChange(e.target.value.toLowerCase())}
                  label={t.columns.extension}
                  required={isNew}
                  disabled={!isNew}
                  autoFocus={isNew}
                  error={!!errors.extension}
                  helperText={errors.extension?.message ?? (isNew ? t.extensionHint : t.immutableHint)}
                />
              )}
            />
            <Controller
              name="appliesTo"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  select
                  label={t.columns.appliesTo}
                  required={isNew}
                  disabled={!isNew}
                  error={!!errors.appliesTo}
                  helperText={errors.appliesTo?.message ?? (isNew ? undefined : t.immutableHint)}
                >
                  {Object.entries(t.appliesToValues).map(([code, label]) => (
                    <MenuItem key={code} value={code}>
                      {label}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="contentType"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.contentType}
                  required
                  autoFocus={!isNew}
                  error={!!errors.contentType}
                  helperText={errors.contentType?.message}
                />
              )}
            />
            <Controller
              name="magicHex"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.magicHex}
                  required
                  error={!!errors.magicHex}
                  helperText={errors.magicHex?.message ?? t.magicHexHint}
                  slotProps={{ htmlInput: { style: { fontFamily: tokens.font.mono } } }}
                />
              )}
            />
            <Controller
              name="maxSizeMb"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.maxSizeMb}
                  required
                  slotProps={{ htmlInput: { inputMode: 'decimal' } }}
                  error={!!errors.maxSizeMb}
                  helperText={errors.maxSizeMb?.message}
                />
              )}
            />
            <Controller
              name="isActive"
              control={control}
              render={({ field }) => (
                <FormControlLabel
                  control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                  label={t.isActive}
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
