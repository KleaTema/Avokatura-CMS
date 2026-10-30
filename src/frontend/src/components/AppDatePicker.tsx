import { Box, type SxProps, type Theme } from '@mui/material'
import { DatePicker } from '@mui/x-date-pickers'
import type { Ref } from 'react'
import type { PickerValue } from '../lib/pickerValues'
import { useClickToOpen } from '../lib/useClickToOpen'

/**
 * The app's date picker: DD.MM.YYYY (Albanian locale from DatesProvider), calendar popup on desktop and dialog on
 * mobile. A click anywhere in the field opens it; Tab only focuses the field for typing; Escape closes it and keeps
 * the focus in the field; choosing a date closes it. `minDate`/`maxDate` limit the calendar — the form gives the
 * message.
 */
export function AppDatePicker({
  value,
  onChange,
  label,
  required,
  error,
  helperText,
  inputRef,
  autoFocus,
  minDate,
  maxDate,
  size,
  fullWidth,
  sx,
}: {
  /** layout of the picker's box in its parent (e.g. full width on a phone, fixed width in a row) */
  sx?: SxProps<Theme>
  size?: 'small' | 'medium'
  /** fill the parent's width (e.g. inside a sized filter box) */
  fullWidth?: boolean
  value: PickerValue
  onChange: (value: PickerValue) => void
  label: string
  required?: boolean
  error?: boolean
  helperText?: string
  inputRef?: Ref<HTMLInputElement>
  autoFocus?: boolean
  minDate?: PickerValue
  maxDate?: PickerValue
}) {
  const { anchorRef, onClick, pickerProps } = useClickToOpen()
  return (
    <Box ref={anchorRef} onClick={onClick} sx={[fullWidth ? { width: '100%' } : {}, ...(Array.isArray(sx) ? sx : [sx])]}>
      <DatePicker
        {...pickerProps}
        value={value}
        onChange={onChange}
        label={label}
        format="DD.MM.YYYY"
        minDate={minDate?.isValid() ? minDate : undefined}
        maxDate={maxDate?.isValid() ? maxDate : undefined}
        slotProps={{ textField: { required, error, helperText, inputRef, autoFocus, size, fullWidth } }}
      />
    </Box>
  )
}
