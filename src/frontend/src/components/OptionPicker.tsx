import { Autocomplete, TextField } from '@mui/material'
import { sq } from '../i18n/sq'
import { matches } from '../lib/search'

export interface PickerOption {
  value: string
  label: string
}

/**
 * Autocomplete for a known list of options (more than ~10), filtered accent-insensitively as the user types.
 * value '' = nothing chosen; clearing the field sets ''.
 */
export function OptionPicker({
  options,
  value,
  onChange,
  label,
  required,
  disabled,
  error,
  helperText,
  autoFocus,
  noOptionsText = sq.search.noOptions,
}: {
  options: PickerOption[]
  value: string
  onChange: (value: string) => void
  label: string
  required?: boolean
  disabled?: boolean
  error?: boolean
  helperText?: string
  autoFocus?: boolean
  noOptionsText?: string
}) {
  const selected = options.find((o) => o.value === value) ?? null
  return (
    <Autocomplete<PickerOption>
      options={options}
      value={selected}
      onChange={(_, picked) => onChange(picked?.value ?? '')}
      getOptionLabel={(o) => o.label}
      isOptionEqualToValue={(a, b) => a.value === b.value}
      filterOptions={(all, state) => all.filter((o) => matches(state.inputValue, o.label))}
      disabled={disabled}
      noOptionsText={noOptionsText}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          required={required}
          error={error}
          helperText={helperText}
          autoFocus={autoFocus}
        />
      )}
    />
  )
}
