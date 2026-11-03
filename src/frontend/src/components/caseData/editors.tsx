import { zodResolver } from '@hookform/resolvers/zod'
import { Box, FormControlLabel, Stack, Switch, TextField } from '@mui/material'
import dayjs from 'dayjs'
import { Controller, useForm, useWatch, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import {
  useSetCategory,
  useSetPriority,
  useSetSecurityMeasure,
  useSetSuspension,
  useUpdateClaim,
  type CaseDetail,
} from '../../features/cases/api'
import { useCaseCategories, useCaseSubcategories } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { AREA_INTEGER_DIGITS, parseAlbanianMoneyInput, toAlbanianInput } from '../../lib/money'
import { isFilled, isoDateOf, isPickerValue, type PickerValue } from '../../lib/pickerValues'
import { AppDatePicker } from '../AppDatePicker'
import { AppSelect } from '../ui/AppSelect'
import { EditorActions } from './EditorActions'
import { MoneyField } from './MoneyField'
import { useSaveOutcome } from './useSaveOutcome'

const e = sq.cases.edit

/** The currencies offered (claim_currency is ISO 4217 text; there is no currency table). */
const CURRENCIES = ['ALL', 'EUR', 'USD']

interface EditorProps {
  detail: CaseDetail
  /** after a successful save */
  onSaved: () => void
  onCancel: () => void
}

/** The row version the next save sends: always the latest one in the cache (updated by every save). */
const rowVersionOf = (detail: CaseDetail) => Number(detail.header.rowVersion)

// ---------------------------------------------------------------- Vlera e padisë

interface ClaimValues {
  amount: string
  currency: string
  area: string
}

const claimSchema = z.object({
  amount: z.string().refine((s) => parseAlbanianMoneyInput(s) !== null, e.claim.amountInvalid),
  currency: z.string(),
  area: z.string().refine((s) => parseAlbanianMoneyInput(s, AREA_INTEGER_DIGITS) !== null, e.claim.areaInvalid),
})

/** Amount (Albanian notation, kept as text) with its currency, and m² as a separate field. */
export function ClaimEditor({ detail, onSaved, onCancel }: EditorProps) {
  const caseId = detail.header.publicId
  const save = useUpdateClaim(caseId)
  const claim = detail.data.claim
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ClaimValues>({
    resolver: zodResolver(claimSchema) as Resolver<ClaimValues>,
    defaultValues: {
      amount: toAlbanianInput(claim?.amount),
      currency: claim?.currency ?? 'ALL',
      area: toAlbanianInput(claim?.areaM2),
    },
  })
  const outcome = useSaveOutcome(caseId, setError, { claimAmount: 'amount', claimCurrency: 'currency', claimAreaM2: 'area' })
  const currencies = claim?.currency && !CURRENCIES.includes(claim.currency) ? [...CURRENCIES, claim.currency] : CURRENCIES

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    // '' (empty field) → null; the strings go to the API unchanged
    const amount = parseAlbanianMoneyInput(x.amount) || null
    const area = parseAlbanianMoneyInput(x.area, AREA_INTEGER_DIGITS) || null
    try {
      await save.mutateAsync({
        claimAmount: amount,
        claimCurrency: amount ? x.currency : null,
        claimAreaM2: area,
        rowVersion: rowVersionOf(detail),
      })
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <Controller
          name="amount"
          control={control}
          render={({ field }) => (
            <MoneyField
              label={e.claim.amount}
              value={field.value}
              onChange={field.onChange}
              onBlur={field.onBlur}
              inputRef={field.ref}
              error={!!errors.amount}
              helperText={errors.amount?.message ?? e.claim.amountHint}
            />
          )}
        />
        <Controller
          name="currency"
          control={control}
          render={({ field }) => (
            <AppSelect
              required
              label={e.claim.currency}
              value={field.value}
              onChange={field.onChange}
              inputRef={field.ref}
              error={!!errors.currency}
              helperText={errors.currency?.message}
              options={currencies.map((c) => ({ value: c, label: c }))}
              sx={{ minWidth: 120 }}
            />
          )}
        />
      </Stack>
      <Box sx={{ maxWidth: { sm: 320 } }}>
        <Controller
          name="area"
          control={control}
          render={({ field }) => (
            <MoneyField
              label={e.claim.area}
              unit="m²"
              value={field.value}
              onChange={field.onChange}
              onBlur={field.onBlur}
              inputRef={field.ref}
              error={!!errors.area}
              helperText={errors.area?.message}
            />
          )}
        />
      </Box>
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing} />
    </Stack>
  )
}

// ---------------------------------------------------------------- Kategoria

interface CategoryValues {
  category: string
  subcategory: string
}

/** Only the categories that are not a delegation (Ndjekje) with its subcategory; delegation is task 027. */
export function CategoryEditor({ detail, onSaved, onCancel }: EditorProps) {
  const caseId = detail.header.publicId
  const save = useSetCategory(caseId)
  const categories = (useCaseCategories().data ?? []).filter((c) => !c.isDelegation)
  const allSubcategories = useCaseSubcategories().data ?? []
  const {
    control,
    handleSubmit,
    setError,
    setValue,
    formState: { errors },
  } = useForm<CategoryValues>({
    defaultValues: {
      category: detail.data.category?.code ?? 'NDJEKJE',
      subcategory: detail.data.subcategory?.code ?? '',
    },
  })
  const category = useWatch({ control, name: 'category' })
  const subcategories = allSubcategories.filter((s) => s.categoryCode === category)
  const outcome = useSaveOutcome(caseId, setError, { categoryCode: 'category', subcategoryCode: 'subcategory' })

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    if (subcategories.length > 0 && !x.subcategory) {
      setError('subcategory', { type: 'required', message: e.category.subcategoryRequired })
      return
    }
    try {
      await save.mutateAsync({
        categoryCode: x.category,
        subcategoryCode: x.subcategory || null,
        rowVersion: rowVersionOf(detail),
      })
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <Controller
          name="category"
          control={control}
          render={({ field }) => (
            <AppSelect
              required
              label={e.category.category}
              value={field.value}
              onChange={(value) => {
                field.onChange(value)
                setValue('subcategory', '')
              }}
              inputRef={field.ref}
              error={!!errors.category}
              helperText={errors.category?.message}
              options={categories.map((c) => ({ value: c.code, label: c.name }))}
              sx={{ minWidth: 200 }}
            />
          )}
        />
        {subcategories.length > 0 && (
          <Controller
            name="subcategory"
            control={control}
            render={({ field }) => (
              <AppSelect
                required
                label={e.category.subcategory}
                value={field.value}
                onChange={field.onChange}
                inputRef={field.ref}
                error={!!errors.subcategory}
                helperText={errors.subcategory?.message}
                options={subcategories.map((s) => ({ value: s.code, label: s.name }))}
                sx={{ minWidth: 200 }}
              />
            )}
          />
        )}
      </Stack>
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing} />
    </Stack>
  )
}

// ---------------------------------------------------------------- Statusi (pezullimi)

interface SuspensionValues {
  suspended: boolean
  reason: string
}

const suspensionSchema = z.object({
  suspended: z.boolean(),
  reason: z.string().refine((s) => s.trim().length <= 500, e.status.reasonTooLong),
})

export function SuspensionEditor({ detail, onSaved, onCancel }: EditorProps) {
  const caseId = detail.header.publicId
  const save = useSetSuspension(caseId)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<SuspensionValues>({
    resolver: zodResolver(suspensionSchema) as Resolver<SuspensionValues>,
    defaultValues: { suspended: detail.header.isSuspended, reason: '' },
  })
  const suspended = useWatch({ control, name: 'suspended' })
  const outcome = useSaveOutcome(caseId, setError, { suspended: 'suspended', reason: 'reason' })

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    try {
      await save.mutateAsync({ suspended: x.suspended, reason: x.reason.trim() || null, rowVersion: rowVersionOf(detail) })
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Controller
        name="suspended"
        control={control}
        render={({ field }) => (
          <FormControlLabel
            control={<Switch checked={field.value} onChange={(ev) => field.onChange(ev.target.checked)} />}
            label={e.status.suspended}
          />
        )}
      />
      <Controller
        name="reason"
        control={control}
        render={({ field }) => (
          <TextField
            {...field}
            inputRef={field.ref}
            label={e.status.reason}
            multiline
            minRows={2}
            error={!!errors.reason}
            helperText={errors.reason?.message}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
        )}
      />
      {/* nothing to save while the switch is where it was */}
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing || suspended === detail.header.isSuspended} />
    </Stack>
  )
}

// ---------------------------------------------------------------- Masa e sigurimit

interface MeasureValues {
  active: boolean
  decisionNumber: string
  decisionDate: PickerValue
  dispositive: string
}

const measureSchema = z
  .object({
    active: z.boolean(),
    decisionNumber: z.string(),
    decisionDate: z.custom<PickerValue>(isPickerValue),
    dispositive: z.string(),
  })
  .superRefine((x, ctx) => {
    if (!x.active) return
    const number = x.decisionNumber.trim()
    if (number.length === 0 || number.length > 50) {
      ctx.addIssue({ code: 'custom', path: ['decisionNumber'], message: e.measure.numberRequired })
    }
    if (!isFilled(x.decisionDate)) {
      ctx.addIssue({ code: 'custom', path: ['decisionDate'], message: e.measure.dateRequired })
    }
  })

export function SecurityMeasureEditor({ detail, onSaved, onCancel }: EditorProps) {
  const caseId = detail.header.publicId
  const save = useSetSecurityMeasure(caseId)
  const measure = detail.data.securityMeasure
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<MeasureValues>({
    resolver: zodResolver(measureSchema) as Resolver<MeasureValues>,
    defaultValues: {
      active: detail.data.hasSecurityMeasure,
      decisionNumber: measure?.decisionNumber ?? '',
      decisionDate: measure ? dayjs(measure.decisionDate) : null,
      dispositive: measure?.dispositive ?? '',
    },
  })
  const active = useWatch({ control, name: 'active' })
  const outcome = useSaveOutcome(caseId, setError, {
    active: 'active',
    decisionNumber: 'decisionNumber',
    decisionDate: 'decisionDate',
    dispositive: 'dispositive',
  })

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    try {
      await save.mutateAsync(
        x.active
          ? {
              active: true,
              decisionNumber: x.decisionNumber.trim(),
              decisionDate: isoDateOf(x.decisionDate),
              dispositive: x.dispositive.trim() || null,
              rowVersion: rowVersionOf(detail),
            }
          : { active: false, decisionNumber: null, decisionDate: null, dispositive: null, rowVersion: rowVersionOf(detail) },
      )
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Controller
        name="active"
        control={control}
        render={({ field }) => (
          <FormControlLabel
            control={<Switch checked={field.value} onChange={(ev) => field.onChange(ev.target.checked)} />}
            label={e.measure.active}
          />
        )}
      />
      {active && (
        <>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <Controller
              name="decisionNumber"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  inputRef={field.ref}
                  label={e.measure.decisionNumber}
                  required
                  error={!!errors.decisionNumber}
                  helperText={errors.decisionNumber?.message}
                  slotProps={{ htmlInput: { maxLength: 50 } }}
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
                  label={e.measure.decisionDate}
                  required
                  error={!!errors.decisionDate}
                  helperText={errors.decisionDate?.message}
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
                label={e.measure.dispositive}
                multiline
                minRows={3}
                error={!!errors.dispositive}
                helperText={errors.dispositive?.message}
                slotProps={{ htmlInput: { maxLength: 10000 } }}
              />
            )}
          />
        </>
      )}
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing} />
    </Stack>
  )
}

// ---------------------------------------------------------------- Prioriteti

interface PriorityValues {
  priority: string
}

export function PriorityEditor({ detail, onSaved, onCancel }: EditorProps) {
  const caseId = detail.header.publicId
  const save = useSetPriority(caseId)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<PriorityValues>({ defaultValues: { priority: detail.header.priority } })
  const priority = useWatch({ control, name: 'priority' })
  const outcome = useSaveOutcome(caseId, setError, { priority: 'priority' })

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    try {
      await save.mutateAsync({ priority: x.priority, rowVersion: rowVersionOf(detail) })
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Controller
        name="priority"
        control={control}
        render={({ field }) => (
          <AppSelect
            required
            label={e.priority.label}
            value={field.value}
            onChange={field.onChange}
            inputRef={field.ref}
            error={!!errors.priority}
            helperText={errors.priority?.message}
            options={Object.entries(sq.cases.priority).map(([code, name]) => ({ value: code, label: name }))}
            sx={{ maxWidth: 240 }}
          />
        )}
      />
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing || priority === detail.header.priority} />
    </Stack>
  )
}
