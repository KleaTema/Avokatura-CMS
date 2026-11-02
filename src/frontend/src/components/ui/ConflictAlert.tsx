import { Alert, Button } from '@mui/material'
import { sq } from '../../i18n/sq'

/**
 * The one message for a stale row version (409 case.changed): someone else saved the case meanwhile. "Rifresko"
 * reloads the case; the form or dialog stays open with what the user typed, to copy it or to save again on top of the
 * fresh data. Used by every save of tasks 023–028 (through useSaveOutcome) — never copied.
 */
export function ConflictAlert({ onRefresh, refreshing }: { onRefresh: () => void; refreshing: boolean }) {
  return (
    <Alert
      severity="warning"
      role="alert"
      action={
        <Button color="inherit" size="small" onClick={onRefresh} disabled={refreshing}>
          {sq.conflict.refresh}
        </Button>
      }
    >
      {sq.conflict.message} {sq.conflict.keep}
    </Alert>
  )
}
