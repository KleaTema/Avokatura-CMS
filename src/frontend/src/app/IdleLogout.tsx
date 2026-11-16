import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { sq } from '../i18n/sq'

/** 30 minutes without activity, then a 60-second warning (task 051). TODO(client-q-idle-timeout): the client confirms. */
const IDLE_MS = 30 * 60_000
const WARNING_S = 60
const ACTIVITY = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'wheel'] as const

const t = sq.idle

/**
 * Idle logout (task 051): after 30 minutes without mouse, keyboard, scroll or touch, a warning counts down 60 seconds;
 * "Vazhdo" keeps the session, otherwise `onLogout` — which ends the provider's session too. Activity while the warning
 * is open does not cancel it (only the button does): a moved mouse is not a decision.
 */
export function IdleLogout({ onLogout }: { onLogout: () => void }) {
  const [left, setLeft] = useState<number | null>(null)
  // 0 until the effect below starts the clock (Date.now() is not called during render)
  const lastActivity = useRef(0)
  const warning = left !== null
  // the listeners read the warning through a ref (they are registered once)
  const warningRef = useRef(false)
  useEffect(() => {
    warningRef.current = warning
  }, [warning])

  useEffect(() => {
    lastActivity.current = Date.now()
    const touch = () => {
      if (!warningRef.current) lastActivity.current = Date.now()
    }
    for (const event of ACTIVITY) window.addEventListener(event, touch, { passive: true })
    const timer = window.setInterval(() => {
      if (!warningRef.current && Date.now() - lastActivity.current >= IDLE_MS) setLeft(WARNING_S)
    }, 1_000)
    return () => {
      for (const event of ACTIVITY) window.removeEventListener(event, touch)
      window.clearInterval(timer)
    }
  }, [])

  useEffect(() => {
    if (left === null) return
    if (left <= 0) {
      onLogout()
      return
    }
    const tick = window.setTimeout(() => setLeft((s) => (s === null ? null : s - 1)), 1_000)
    return () => window.clearTimeout(tick)
  }, [left, onLogout])

  const stay = () => {
    lastActivity.current = Date.now()
    setLeft(null)
  }

  return (
    <Dialog open={warning} onClose={stay} aria-labelledby="idle-title" aria-describedby="idle-text">
      <DialogTitle id="idle-title">{t.title}</DialogTitle>
      <DialogContent>
        <DialogContentText id="idle-text" role="timer" aria-live="polite">
          {t.text(left ?? 0)}
        </DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onLogout}>{t.logout}</Button>
        <Button variant="contained" onClick={stay} autoFocus>
          {t.stay}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
