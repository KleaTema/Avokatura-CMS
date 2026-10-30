import { useRef, useState, type MouseEvent } from 'react'

/**
 * Controlled open state for a date/time picker that also opens on a mouse click or tap anywhere in its field
 * (MUI only opens from the icon). Keyboard focus (Tab) never opens it, so typing stays possible.
 * Put `anchorRef` and `onClick` on an element that wraps the picker's field.
 */
export function useClickToOpen() {
  const [open, setOpen] = useState(false)
  const anchorRef = useRef<HTMLDivElement>(null)

  const onClick = (e: MouseEvent) => {
    // detail = 0 for clicks synthesized by the keyboard; clicks in the popup bubble here through the React
    // portal but are not inside the field's DOM, so choosing a date never reopens the picker
    if (e.detail > 0 && e.target instanceof Node && anchorRef.current?.contains(e.target)) setOpen(true)
  }

  return {
    anchorRef,
    onClick,
    pickerProps: { open, onOpen: () => setOpen(true), onClose: () => setOpen(false), closeOnSelect: true },
  }
}
