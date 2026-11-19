import { IconButton, Stack, TextField, Tooltip } from '@mui/material'
import dayjs from 'dayjs'
import { Lock, X } from 'lucide-react'
import { sq } from '../../i18n/sq'
import { isoDateOf } from '../../lib/pickerValues'
import { AppDatePicker } from '../AppDatePicker'
import { AppSelect } from '../ui/AppSelect'
import { SearchField } from '../ui/SearchField'
import { FilterKind, type FilterDef, type FilterOptionDef, type FilterValue, type RangeValue } from './filterTypes'
import { RangeGroup } from './RangeGroup'

const t = sq.filters
const FIELD = { width: { xs: '100%', sm: 240 } }

const rangeOf = (value: FilterValue | undefined): RangeValue => (typeof value === 'object' && value !== null ? value : {})

/**
 * One filter, drawn as its kind says (tasks 053h / 053m): a choice (the options the server gives — only values in the
 * user's scope, under the parent's choice), a contained text, a date range, a number range, yes / no. A locked filter
 * (an institution's own institution) is read-only. `onRemove`: added with "+ Shto filtër", it can be taken away (×).
 */
export function FilterField({
  filter,
  value,
  options,
  onChange,
  onRemove,
  autoFocus,
  busy = false,
}: {
  filter: FilterDef
  value: FilterValue | undefined
  /** the select's options under the parent's choice (default: all of them) */
  options?: readonly FilterOptionDef[]
  onChange: (value: FilterValue) => void
  onRemove?: () => void
  autoFocus?: boolean
  /** a text filter: the list is loading its rows */
  busy?: boolean
}) {
  const remove = onRemove && (
    <Tooltip title={t.remove(filter.label)}>
      <IconButton size="small" onClick={onRemove} aria-label={t.remove(filter.label)} sx={{ mt: 1 }}>
        <X size={16} aria-hidden />
      </IconButton>
    </Tooltip>
  )

  switch (filter.kind) {
    case FilterKind.Select:
      return (
        <Stack direction="row" spacing={0.5} sx={FIELD}>
          <AppSelect
            filter={!filter.required}
            required={filter.required}
            hideRequiredMark
            fullWidth
            size="small"
            label={filter.label}
            value={typeof value === 'string' ? value : ''}
            onChange={onChange}
            disabled={filter.locked}
            options={(options ?? filter.options ?? []).map((o) => ({ value: o.value, label: o.label }))}
            helperText={
              filter.locked ? (
                <Stack component="span" direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                  <Lock size={12} aria-hidden />
                  <span>{t.locked}</span>
                </Stack>
              ) : undefined
            }
          />
          {remove}
        </Stack>
      )
    case FilterKind.YesNo:
      return (
        <Stack direction="row" spacing={0.5} sx={FIELD}>
          <AppSelect
            filter
            fullWidth
            size="small"
            label={filter.label}
            value={value === true ? 'true' : value === false ? 'false' : ''}
            onChange={(v) => onChange(v === '' ? '' : v === 'true')}
            options={[
              { value: 'true', label: t.yes },
              { value: 'false', label: t.no },
            ]}
          />
          {remove}
        </Stack>
      )
    case FilterKind.DateRange: {
      const range = rangeOf(value)
      // task 053o: the filter's label on the border, "Nga" and "Deri" inside
      return (
        <Stack direction="row" spacing={0.5} sx={{ width: { xs: '100%', sm: 'auto' }, alignItems: 'flex-start' }}>
          <RangeGroup label={filter.label}>
            <AppDatePicker
              label={t.from}
              size="small"
              value={range.from ? dayjs(range.from) : null}
              onChange={(d) => onChange({ ...range, from: isoDateOf(d) ?? '' })}
              sx={{ width: { xs: '50%', sm: 160 } }}
            />
            <AppDatePicker
              label={t.to}
              size="small"
              value={range.to ? dayjs(range.to) : null}
              onChange={(d) => onChange({ ...range, to: isoDateOf(d) ?? '' })}
              minDate={range.from ? dayjs(range.from) : undefined}
              sx={{ width: { xs: '50%', sm: 160 } }}
            />
          </RangeGroup>
          {remove}
        </Stack>
      )
    }
    case FilterKind.NumberRange: {
      const range = rangeOf(value)
      return (
        <Stack direction="row" spacing={0.5} sx={{ width: { xs: '100%', sm: 'auto' }, alignItems: 'flex-start' }}>
          <RangeGroup label={filter.label}>
            <TextField
              size="small"
              label={t.min}
              value={range.min ?? ''}
              onChange={(e) => onChange({ ...range, min: e.target.value })}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
              sx={{ width: { xs: '50%', sm: 120 } }}
            />
            <TextField
              size="small"
              label={t.max}
              value={range.max ?? ''}
              onChange={(e) => onChange({ ...range, max: e.target.value })}
              slotProps={{ htmlInput: { inputMode: 'decimal' } }}
              sx={{ width: { xs: '50%', sm: 120 } }}
            />
          </RangeGroup>
          {remove}
        </Stack>
      )
    }
    default:
      // live search: a contained text is the shared SearchField (X to clear, a spinner while the rows load)
      return (
        <Stack direction="row" spacing={0.5} sx={FIELD}>
          <SearchField
            label={filter.label}
            value={typeof value === 'string' ? value : ''}
            onChange={onChange}
            autoFocus={autoFocus}
            busy={busy}
          />
          {remove}
        </Stack>
      )
  }
}
