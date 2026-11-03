import type { Ref } from 'react'
import type { Court } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { matches } from '../lib/search'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

/**
 * The active courts, searchable (accent-insensitive, name or city) and grouped by court level in the levels' order.
 * With `levelCode` only that level's courts are offered. `filter` / `required`: see AppAutocomplete.
 */
export function CourtPicker({
  courts,
  levels,
  levelCode,
  value,
  onChange,
  label,
  required,
  filter,
  error,
  helperText,
  inputRef,
  size,
}: EmptyChoice & {
  size?: 'small' | 'medium'
  courts: Court[]
  levels: { code: string; name: string }[]
  levelCode: string
  value: string
  onChange: (court: Court | null) => void
  label: string
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

  return (
    <AppAutocomplete<Court>
      options={options}
      value={options.find((c) => String(c.id) === value) ?? null}
      onChange={onChange}
      groupBy={(c) => levelName.get(c.courtLevelCode ?? '') ?? ''}
      getOptionLabel={(c) => c.name}
      getOptionKey={(c) => String(c.id)}
      filterOptions={(all, input) => all.filter((c) => matches(input, c.name, c.city))}
      noOptionsText={sq.cases.register.noCourts}
      size={size}
      inputRef={inputRef}
      label={label}
      required={required}
      filter={filter}
      error={error}
      helperText={helperText}
    />
  )
}
