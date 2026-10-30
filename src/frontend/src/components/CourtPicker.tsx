import { Autocomplete, TextField } from '@mui/material'
import type { Ref } from 'react'
import type { Court } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { matches } from '../lib/search'

/**
 * The active courts, searchable (accent-insensitive, name or city) and grouped by court level in the levels' order.
 * With `levelCode` only that level's courts are offered.
 */
export function CourtPicker({
  courts,
  levels,
  levelCode,
  value,
  onChange,
  label,
  required,
  error,
  helperText,
  inputRef,
  size,
}: {
  size?: 'small' | 'medium'
  courts: Court[]
  levels: { code: string; name: string }[]
  levelCode: string
  value: string
  onChange: (court: Court | null) => void
  label: string
  required?: boolean
  error?: boolean
  helperText?: string
  inputRef?: Ref<HTMLInputElement>
}) {
  const order = new Map(levels.map((l, i) => [l.code, i]))
  const levelName = new Map(levels.map((l) => [l.code, l.name]))
  const rank = (c: Court) => order.get(c.courtLevelCode ?? '') ?? levels.length
  const options = courts
    .filter((c) => !levelCode || c.courtLevelCode === levelCode)
    .sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name, 'sq'))
  const selected = options.find((c) => String(c.id) === value) ?? null

  return (
    <Autocomplete<Court>
      options={options}
      value={selected}
      onChange={(_, court) => onChange(court)}
      groupBy={(c) => levelName.get(c.courtLevelCode ?? '') ?? ''}
      getOptionLabel={(c) => c.name}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      filterOptions={(all, state) => all.filter((c) => matches(state.inputValue, c.name, c.city))}
      noOptionsText={sq.cases.register.noCourts}
      size={size}
      renderInput={(params) => (
        <TextField
          {...params}
          inputRef={inputRef}
          label={label}
          required={required}
          error={error}
          helperText={helperText}
        />
      )}
    />
  )
}
