import { Box, type SxProps, type Theme } from '@mui/material'
import { TimePicker } from '@mui/x-date-pickers'
import { renderMultiSectionDigitalClockTimeView } from '@mui/x-date-pickers/timeViewRenderers'
import type { Ref } from 'react'
import { TIME_REFERENCE, type PickerValue } from '../lib/pickerValues'
import { useClickToOpen } from '../lib/useClickToOpen'

/**
 * The app's time picker: 24h HH:mm, hour and minute lists (minutes in steps of 5) instead of the analog clock;
 * a typed minute outside the steps is still accepted. Opens like AppDatePicker: a click anywhere in the field, not
 * on Tab; Escape closes it; choosing the minute closes it. Values are built on TIME_REFERENCE (only HH:mm counts).
 */
export function AppTimePicker({
  value,
  onChange,
  label,
  required,
  error,
  helperText,
  inputRef,
  autoFocus,
  minTime,
  maxTime,
  fullWidth,
  sx,
}: {
  /** fill the parent's width */
  fullWidth?: boolean
  /** layout of the picker's box in its parent (e.g. full width on a phone, fixed width in a row) */
  sx?: SxProps<Theme>
  value: PickerValue
  onChange: (value: PickerValue) => void
  label: string
  required?: boolean
  error?: boolean
  helperText?: string
  inputRef?: Ref<HTMLInputElement>
  autoFocus?: boolean
  minTime?: PickerValue
  maxTime?: PickerValue
}) {
  const { anchorRef, onClick, pickerProps } = useClickToOpen()
  return (
    <Box ref={anchorRef} onClick={onClick} sx={[fullWidth ? { width: '100%' } : {}, ...(Array.isArray(sx) ? sx : [sx])]}>
      <TimePicker
        {...pickerProps}
        value={value}
        onChange={onChange}
        label={label}
        ampm={false}
        format="HH:mm"
        views={['hours', 'minutes']}
        timeSteps={{ minutes: 5 }}
        referenceDate={TIME_REFERENCE}
        minTime={minTime?.isValid() ? minTime : undefined}
        maxTime={maxTime?.isValid() ? maxTime : undefined}
        viewRenderers={{ hours: renderMultiSectionDigitalClockTimeView, minutes: renderMultiSectionDigitalClockTimeView }}
        slotProps={{ textField: { required, error, helperText, inputRef, autoFocus, fullWidth } }}
      />
    </Box>
  )
}
