import { useMediaQuery, useTheme } from '@mui/material'
import type { KeyboardEvent } from 'react'

/** Dialogs fill the screen on a phone (below the `sm` breakpoint, e.g. 390 px): no squeezed fields, no side scroll. */
export function useFullScreenDialog(): boolean {
  const theme = useTheme()
  return useMediaQuery(theme.breakpoints.down('sm'))
}

/**
 * For the <form> of a dialog: Enter in a one-line field (text, picker input) does NOT submit the form by accident —
 * the user submits with the button (or Enter on it). Multi-line fields keep Enter for a new line, and a picker's own
 * Enter (choosing the highlighted option) still works.
 */
export function preventEnterSubmit(event: KeyboardEvent<HTMLFormElement>) {
  const target = event.target
  if (event.key === 'Enter' && target instanceof HTMLInputElement && target.type !== 'submit' && target.type !== 'button') {
    event.preventDefault()
  }
}
