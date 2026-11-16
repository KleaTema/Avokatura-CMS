import { IconButton, Stack, TextField, Tooltip } from '@mui/material'
import dayjs from 'dayjs'
import { Lock, X } from 'lucide-react'
import { AppDatePicker } from '../../../components/AppDatePicker'
import { AppSelect } from '../../../components/ui/AppSelect'
import { FilterKind, type FilterValue, type RangeValue, type ReportFilterMeta } from '../../../features/reports/builder'
import { sq } from '../../../i18n/sq'
import { isoDateOf } from '../../../lib/pickerValues'

const t = sq.reports.builder
const FIELD = { width: { xs: '100%', sm: 240 } }

const rangeOf = (value: FilterValue | undefined): RangeValue =>
  typeof value === 'object' && value !== null ? value : {}

/**
 * One filter of the report builder, drawn as its kind says (task 053h): a choice (its options come from the API —
 * only values in the user's scope and under the parent's choice), a contained text, a date range, a number range,
 * yes / no. A child without its parent's choice still lists everything in scope; a locked filter (an institution's own
 * institution) is read-only. `onRemove`: the filter was added with "+ Shto filtër" and can be taken away again.
 */
export function FilterField({
  filter,
  value,
  onChange,
  onRemove,
  parentLabel,
}: {
  filter: ReportFilterMeta
  value: FilterValue | undefined
  onChange: (value: FilterValue) => void
  onRemove?: () => void
  /** the parent filter's label, when the parent has no choice yet (a hint under the field) */
  parentLabel?: string
}) {
  const remove = onRemove && (
    <Tooltip title={t.removeFilter(filter.label)}>
      <IconButton size="small" onClick={onRemove} aria-label={t.removeFilter(filter.label)} sx={{ mt: 1 }}>
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
            label={filter.label}
            value={typeof value === 'string' ? value : ''}
            onChange={onChange}
            disabled={filter.locked}
            options={(filter.options ?? []).map((o) => ({ value: o.value, label: o.label }))}
            helperText={
              filter.locked ? (
                <Stack component="span" direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                  <Lock size={12} aria-hidden />
                  <span>{t.locked}</span>
                </Stack>
              ) : parentLabel ? (
                t.chooseParentFirst(parentLabel)
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
            label={filter.label}
            value={value === true ? 'true' : value === false ? 'false' : ''}
            onChange={(v) => onChange(v === '' ? '' : v === 'true')}
            options={[
              { value: 'true', label: sq.reports.yes },
              { value: 'false', label: sq.reports.no },
            ]}
          />
          {remove}
        </Stack>
      )
    case FilterKind.DateRange: {
      const range = rangeOf(value)
      return (
        <Stack direction="row" spacing={1} sx={{ width: { xs: '100%', sm: 'auto' }, alignItems: 'flex-start' }} role="group" aria-label={filter.label}>
          <AppDatePicker
            label={`${filter.label} — ${t.from}`}
            value={range.from ? dayjs(range.from) : null}
            onChange={(d) => onChange({ ...range, from: isoDateOf(d) ?? '' })}
            sx={{ width: { xs: '50%', sm: 190 } }}
          />
          <AppDatePicker
            label={t.to}
            value={range.to ? dayjs(range.to) : null}
            onChange={(d) => onChange({ ...range, to: isoDateOf(d) ?? '' })}
            minDate={range.from ? dayjs(range.from) : undefined}
            sx={{ width: { xs: '50%', sm: 160 } }}
          />
          {remove}
        </Stack>
      )
    }
    case FilterKind.NumberRange: {
      const range = rangeOf(value)
      return (
        <Stack direction="row" spacing={1} sx={{ width: { xs: '100%', sm: 'auto' }, alignItems: 'flex-start' }} role="group" aria-label={filter.label}>
          <TextField
            label={`${filter.label} — ${t.min}`}
            value={range.min ?? ''}
            onChange={(e) => onChange({ ...range, min: e.target.value })}
            slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            sx={{ width: { xs: '50%', sm: 190 } }}
          />
          <TextField
            label={t.max}
            value={range.max ?? ''}
            onChange={(e) => onChange({ ...range, max: e.target.value })}
            slotProps={{ htmlInput: { inputMode: 'decimal' } }}
            sx={{ width: { xs: '50%', sm: 130 } }}
          />
          {remove}
        </Stack>
      )
    }
    default:
      return (
        <Stack direction="row" spacing={0.5} sx={FIELD}>
          <TextField
            fullWidth
            label={filter.label}
            value={typeof value === 'string' ? value : ''}
            onChange={(e) => onChange(e.target.value)}
          />
          {remove}
        </Stack>
      )
  }
}
