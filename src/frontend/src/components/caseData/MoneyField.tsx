import { InputAdornment, TextField } from '@mui/material'
import { useLayoutEffect, useRef } from 'react'
import { formatWhileTyping } from '../../lib/money'

/**
 * A text field for money or m² in Albanian notation, regrouped while typing (1.250.000,50). The value stays TEXT —
 * it is never turned into a JS number; lib/money parses it into the API's string on save. The caret stays after the
 * same digit when the dots move.
 */
export function MoneyField({
  value,
  onChange,
  onBlur,
  label,
  unit,
  error,
  helperText,
  inputRef,
}: {
  value: string
  onChange: (text: string) => void
  onBlur?: () => void
  label: string
  unit?: string
  error?: boolean
  helperText?: string
  /** a callback ref, e.g. react-hook-form's field.ref */
  inputRef?: (node: HTMLInputElement | null) => void
}) {
  const local = useRef<HTMLInputElement | null>(null)
  const caret = useRef<number | null>(null)

  useLayoutEffect(() => {
    if (caret.current !== null && local.current) {
      local.current.setSelectionRange(caret.current, caret.current)
      caret.current = null
    }
  }, [value])

  // our ref (for the caret) and the form's (focus on error) on the same input
  const setRefs = (node: HTMLInputElement | null) => {
    local.current = node
    inputRef?.(node)
  }

  return (
    <TextField
      label={label}
      value={value}
      onChange={(e) => {
        const next = formatWhileTyping(e.target.value, e.target.selectionStart ?? e.target.value.length)
        caret.current = next.caret
        onChange(next.text)
      }}
      onBlur={onBlur}
      error={error}
      helperText={helperText}
      inputRef={setRefs}
      fullWidth
      slotProps={{
        htmlInput: { inputMode: 'decimal', autoComplete: 'off', maxLength: 30 },
        input: unit ? { endAdornment: <InputAdornment position="end">{unit}</InputAdornment> } : undefined,
      }}
    />
  )
}
