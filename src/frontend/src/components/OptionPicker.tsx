import { sq } from '../i18n/sq'
import { matches } from '../lib/search'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

export interface PickerOption {
  value: string
  label: string
}

/**
 * Autocomplete for a known list of options (more than ~10), filtered accent-insensitively as the user types.
 * value '' = nothing chosen ("Të gjitha" with `filter`, "— Asnjë —" for an optional field; see AppAutocomplete).
 */
export function OptionPicker({
  options,
  value,
  onChange,
  label,
  required,
  filter,
  disabled,
  error,
  helperText,
  autoFocus,
  noOptionsText = sq.search.noOptions,
  size,
}: EmptyChoice & {
  options: PickerOption[]
  value: string
  onChange: (value: string) => void
  label: string
  disabled?: boolean
  error?: boolean
  helperText?: string
  autoFocus?: boolean
  noOptionsText?: string
  size?: 'small' | 'medium'
}) {
  return (
    <AppAutocomplete<PickerOption>
      options={options}
      value={options.find((o) => o.value === value) ?? null}
      onChange={(picked) => onChange(picked?.value ?? '')}
      getOptionLabel={(o) => o.label}
      getOptionKey={(o) => o.value}
      filterOptions={(all, input) => all.filter((o) => matches(input, o.label))}
      label={label}
      required={required}
      filter={filter}
      disabled={disabled}
      error={error}
      helperText={helperText}
      autoFocus={autoFocus}
      noOptionsText={noOptionsText}
      size={size}
    />
  )
}
