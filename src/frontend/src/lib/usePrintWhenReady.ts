import { useEffect } from 'react'

/**
 * Task 053n — prints once the printout is on the page (after React has rendered it), then tells the page it is done
 * (the browser's afterprint).
 */
export function usePrintWhenReady(printout: unknown, onDone: () => void) {
  useEffect(() => {
    if (!printout) return
    const after = () => onDone()
    window.addEventListener('afterprint', after, { once: true })
    const id = window.setTimeout(() => window.print(), 0)
    return () => {
      window.clearTimeout(id)
      window.removeEventListener('afterprint', after)
    }
  }, [printout, onDone])
}
