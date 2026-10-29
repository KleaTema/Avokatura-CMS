import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { useCreateCodeFormat } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminCodeFormat

const ALLOWED_TOKENS = ['{YYYY}', '{RAND}']
const MAX_CODE_LENGTH = 30

// Same rules as the backend; the server stays the real check.
const isValidTemplate = (template: string) => {
  if (!template || template.length > 40) return false
  const tokens = template.match(/\{[^}]*\}/g) ?? []
  return tokens.every((x) => ALLOWED_TOKENS.includes(x)) && tokens.filter((x) => x === '{RAND}').length === 1
}

/** Length of a generated code: {YYYY} is 4 characters, {RAND} is the chosen number of digits. */
const expandedLength = (template: string, digits: number) =>
  template.replaceAll('{YYYY}', '0000').replaceAll('{RAND}', '0'.repeat(digits)).length

const schema = z
  .object({
    template: z.string().trim().refine(isValidTemplate, t.validation.templateInvalid),
    randomDigits: z
      .string()
      .trim()
      .regex(/^\d{1,2}$/, t.validation.digitsInvalid)
      .refine((v) => Number(v) >= 4 && Number(v) <= 12, t.validation.digitsInvalid),
    note: z.string().trim().max(300, t.validation.noteTooLong),
  })
  .superRefine((v, ctx) => {
    if (expandedLength(v.template, Number(v.randomDigits)) > MAX_CODE_LENGTH)
      ctx.addIssue({ code: 'custom', path: ['template'], message: t.validation.codeTooLong })
  })

type FormValues = z.infer<typeof schema>

const randomDigitString = (length: number) =>
  Array.from({ length }, () => Math.floor(Math.random() * 10)).join('')

/** What a new dossier code would look like: current year and random digits of the chosen length, e.g. ASH-2026-483915. */
function useExample(template: string, digitsText: string) {
  const digits = Number(digitsText)
  const validDigits = Number.isInteger(digits) && digits >= 4 && digits <= 12 ? digits : null
  const random = useMemo(() => (validDigits ? randomDigitString(validDigits) : ''), [validDigits])
  const [year] = useState(() => String(new Date().getFullYear()))
  if (!validDigits || !isValidTemplate(template.trim())) return null
  return template
    .trim()
    .replaceAll('{YYYY}', year)
    .replaceAll('{RAND}', random)
}

export function CodeFormatCreateDialog({ onClose }: { onClose: () => void }) {
  const create = useCreateCodeFormat()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { template: 'ASH-{YYYY}-{RAND}', randomDigits: '6', note: '' },
  })
  const [template, randomDigits] = useWatch({ control, name: ['template', 'randomDigits'] })
  const example = useExample(template, randomDigits)

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await create.mutateAsync({ template: v.template, randomDigits: Number(v.randomDigits), note: v.note || null })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="code-format-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="code-format-title">{t.createTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            {formError && <Alert severity="error">{formError}</Alert>}
            <Typography variant="body2" color="text.secondary" id="code-format-tokens">
              {t.tokensHelp}
            </Typography>
            <Controller
              name="template"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.template}
                  required
                  autoFocus
                  error={!!errors.template}
                  helperText={errors.template?.message ?? (example ? t.example(example) : ' ')}
                  slotProps={{
                    htmlInput: { style: { fontFamily: 'monospace' }, 'aria-describedby': 'code-format-tokens' },
                    formHelperText: { 'aria-live': 'polite' },
                  }}
                />
              )}
            />
            <Controller
              name="randomDigits"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.randomDigits}
                  type="number"
                  required
                  slotProps={{ htmlInput: { min: 4, max: 12 } }}
                  error={!!errors.randomDigits}
                  helperText={errors.randomDigits?.message}
                />
              )}
            />
            <Controller
              name="note"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label={t.columns.note}
                  multiline
                  minRows={2}
                  error={!!errors.note}
                  helperText={errors.note?.message}
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
