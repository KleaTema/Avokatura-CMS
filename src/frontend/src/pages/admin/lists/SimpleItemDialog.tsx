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
  FormHelperText,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import { useSaveSimpleItem, type SimpleListItem, type SimpleListKey } from '../../../features/admin/api'
import { sq } from '../../../i18n/sq'
import { problemMessage } from '../../../lib/problem'
import { useForbiddenRedirect } from './helpers'
import { groupLabels, simpleListConfig, type SimpleListConfig } from './simpleLists'

const t = sq.adminLists
const UPPER_SNAKE = /^[A-Z0-9_]+$/

const buildSchema = (config: SimpleListConfig, isNew: boolean) =>
  z
    .object({
      // the code of an existing row is shown disabled and never sent
      code: isNew
        ? z
            .string()
            .trim()
            .min(1, t.validation.codeInvalid(config.codeMax))
            .max(config.codeMax, t.validation.codeInvalid(config.codeMax))
            .regex(UPPER_SNAKE, t.validation.codeInvalid(config.codeMax))
        : z.string(),
      name: z
        .string()
        .trim()
        .min(1, t.validation.nameRequired(config.nameMax))
        .max(config.nameMax, t.validation.nameRequired(config.nameMax)),
      sortOrder: z
        .string()
        .trim()
        .regex(/^\d{1,3}$/, t.validation.sortOrderInvalid)
        .refine((v) => Number(v) <= 255, t.validation.sortOrderInvalid),
      isOther: z.boolean(),
      group: z.string(),
      sendsBackForRetrial: z.boolean(),
    })
    .superRefine((v, ctx) => {
      if (isNew && config.group && !v.group)
        ctx.addIssue({ code: 'custom', path: ['group'], message: t.validation.groupRequired })
    })

type FormValues = z.infer<ReturnType<typeof buildSchema>>

const toFormValues = (item: SimpleListItem | null): FormValues => ({
  code: item?.code ?? '',
  name: item?.name ?? '',
  sortOrder: String(item?.sortOrder ?? 0),
  isOther: item?.isOther ?? false,
  group: item?.reportGroup ?? item?.outcomeGroup ?? '',
  sendsBackForRetrial: item?.sendsBackForRetrial ?? false,
})

/** Add (item = null) or edit one row of a simple list. Code and the group fields are create-only. */
export function SimpleItemDialog({
  list,
  item,
  onClose,
}: {
  list: SimpleListKey
  item: SimpleListItem | null
  onClose: () => void
}) {
  const config = simpleListConfig[list]
  const isNew = item === null
  const save = useSaveSimpleItem(list)
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const schema = useMemo(() => buildSchema(config, isNew), [config, isNew])
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: toFormValues(item) })

  const listName = t.lists[list]
  const labels = config.group ? groupLabels(config.group) : {}
  const groupLabel = config.group === 'report' ? t.columns.reportGroup : t.columns.outcomeGroup

  const onSubmit = handleSubmit(async (v) => {
    setFormError(null)
    try {
      await save.mutateAsync({
        code: item?.code ?? null,
        input: {
          code: v.code,
          name: v.name,
          sortOrder: Number(v.sortOrder),
          isOther: v.isOther,
          group: v.group,
          sendsBackForRetrial: v.sendsBackForRetrial,
        },
      })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="simple-item-title">
      <form onSubmit={onSubmit} noValidate>
        <DialogTitle id="simple-item-title">{isNew ? t.createTitle(listName) : t.editTitle(listName)}</DialogTitle>
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
                  helperText={errors.code?.message ?? (isNew ? undefined : t.immutableHint)}
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
            {config.sortOrder && (
              <Controller
                name="sortOrder"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    label={t.columns.sortOrder}
                    type="number"
                    error={!!errors.sortOrder}
                    helperText={errors.sortOrder?.message}
                  />
                )}
              />
            )}
            {config.isOther && (
              <Controller
                name="isOther"
                control={control}
                render={({ field }) => (
                  <FormControlLabel
                    control={<Checkbox checked={field.value} onChange={(e) => field.onChange(e.target.checked)} />}
                    label={t.columns.isOther}
                  />
                )}
              />
            )}
            {config.group && (
              <Controller
                name="group"
                control={control}
                render={({ field }) =>
                  isNew ? (
                    <TextField
                      {...field}
                      select
                      label={groupLabel}
                      required
                      error={!!errors.group}
                      helperText={errors.group?.message}
                    >
                      {Object.entries(labels).map(([code, label]) => (
                        <MenuItem key={code} value={code}>
                          {label}
                        </MenuItem>
                      ))}
                    </TextField>
                  ) : (
                    <TextField
                      label={groupLabel}
                      value={labels[field.value] ?? field.value}
                      disabled
                      helperText={t.immutableHint}
                    />
                  )
                }
              />
            )}
            {config.group === 'outcome' && (
              <Controller
                name="sendsBackForRetrial"
                control={control}
                render={({ field }) => (
                  <div>
                    <FormControlLabel
                      control={
                        <Checkbox
                          checked={field.value}
                          onChange={(e) => field.onChange(e.target.checked)}
                          disabled={!isNew}
                        />
                      }
                      label={t.columns.sendsBackForRetrial}
                    />
                    {!isNew && <FormHelperText>{t.immutableHint}</FormHelperText>}
                  </div>
                )}
              />
            )}
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
