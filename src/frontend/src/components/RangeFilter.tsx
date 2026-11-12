import { Box, Stack, Typography } from '@mui/material'
import dayjs from 'dayjs'
import { useState } from 'react'
import { isoDateOf, type PickerValue } from '../lib/pickerValues'
import { AppDatePicker } from './AppDatePicker'

/** A "nga – deri" pair of date filters under one legend (the case list's hearing / registration dates, the closed report). */
export function RangeFilter({
  legend,
  fromLabel,
  toLabel,
  from,
  to,
  onFrom,
  onTo,
}: {
  legend: string
  fromLabel: string
  toLabel: string
  from: string
  to: string
  onFrom: (isoDate: string) => void
  onTo: (isoDate: string) => void
}) {
  return (
    <Box component="fieldset" sx={{ border: 0, m: 0, p: 0, minWidth: 0 }}>
      <Typography component="legend" variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {legend}
      </Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} useFlexGap sx={{ alignItems: { sm: 'center' } }}>
        {/* short labels; the legend names the pair (fieldset) */}
        <DateFilter label={fromLabel} value={from} maxDate={to} onChange={onFrom} />
        <Typography aria-hidden sx={{ display: { xs: 'none', sm: 'block' } }}>
          –
        </Typography>
        <DateFilter label={toLabel} value={to} minDate={from} onChange={onTo} />
      </Stack>
    </Box>
  )
}

/**
 * A date filter kept in the URL as yyyy-MM-dd. Local picker state holds a half-typed date; only a complete date (or
 * clearing it) reaches the URL, and a change of the URL (e.g. "Pastro filtrat") resets the picker.
 */
export function DateFilter({
  label,
  value,
  onChange,
  minDate,
  maxDate,
}: {
  label: string
  value: string
  onChange: (isoDate: string) => void
  minDate?: string
  maxDate?: string
}) {
  const [local, setLocal] = useState<PickerValue>(value ? dayjs(value) : null)
  const [fromUrl, setFromUrl] = useState(value)
  if (value !== fromUrl) {
    setFromUrl(value)
    setLocal(value ? dayjs(value) : null)
  }

  const change = (next: PickerValue) => {
    setLocal(next)
    if (next === null) onChange('')
    else if (isoDateOf(next)) onChange(isoDateOf(next) ?? '')
  }

  return (
    <Box sx={{ width: { xs: '100%', sm: 170 } }}>
      <AppDatePicker
        label={label}
        value={local}
        onChange={change}
        size="small"
        fullWidth
        minDate={minDate ? dayjs(minDate) : undefined}
        maxDate={maxDate ? dayjs(maxDate) : undefined}
      />
    </Box>
  )
}
