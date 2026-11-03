import { MenuItem, TextField, type SxProps, type Theme } from '@mui/material'
import type { FocusEventHandler, ReactNode, Ref } from 'react'
import { sq } from '../../i18n/sq'
import type { EmptyChoice } from './AppAutocomplete'

export interface SelectOption {
  value: string
  label: ReactNode
  disabled?: boolean
}

/**
 * The only plain select of the app (short fixed lists; longer ones use AppAutocomplete). value '' = nothing chosen.
 * - `filter`: the first option "Të gjitha" ('' = no filter), shown as the value when nothing is chosen.
 * - `required`: no empty option; until something is chosen only the label shows.
 * - neither (optional field): the first option "— Asnjë —" sets ''.
 * A value that is not among the options (e.g. a list still loading) shows as empty instead of an MUI warning.
 */
export function AppSelect({
  options,
  value,
  onChange,
  label,
  required,
  filter,
  disabled,
  error,
  helperText,
  name,
  inputRef,
  onBlur,
  size,
  fullWidth,
  autoFocus,
  sx,
  id,
  hideRequiredMark,
}: EmptyChoice & {
  /** `required` for a select that always has a value but is not a form field (a list's status, the page size): no "*" */
  hideRequiredMark?: boolean
  options: SelectOption[]
  value: string
  onChange: (value: string) => void
  label: string
  disabled?: boolean
  error?: boolean
  helperText?: ReactNode
  name?: string
  inputRef?: Ref<HTMLInputElement>
  onBlur?: FocusEventHandler<HTMLInputElement | HTMLTextAreaElement>
  size?: 'small' | 'medium'
  fullWidth?: boolean
  autoFocus?: boolean
  sx?: SxProps<Theme>
  id?: string
}) {
  const known = value === '' || options.some((o) => o.value === value)
  return (
    <TextField
      select
      id={id}
      name={name}
      inputRef={inputRef}
      onBlur={onBlur}
      label={label}
      value={known ? value : ''}
      onChange={(e) => onChange(e.target.value)}
      required={required}
      disabled={disabled}
      error={error}
      helperText={helperText}
      size={size}
      fullWidth={fullWidth}
      autoFocus={autoFocus}
      sx={sx}
      // a filter shows "Të gjitha" as its value
      slotProps={{
        select: filter ? { displayEmpty: true } : undefined,
        inputLabel: { ...(filter ? { shrink: true } : {}), ...(hideRequiredMark ? { required: false } : {}) },
      }}
    >
      {!required && (
        <MenuItem value="">{filter ? sq.common.all : sq.common.none}</MenuItem>
      )}
      {options.map((o) => (
        <MenuItem key={o.value} value={o.value} disabled={o.disabled}>
          {o.label}
        </MenuItem>
      ))}
    </TextField>
  )
}
