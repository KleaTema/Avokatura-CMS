import { Button, Dialog, DialogActions, DialogContent, DialogContentText } from '@mui/material'
import { useBlocker } from 'react-router'
import { sq } from '../../i18n/sq'
import { blocksLeaving } from './urlFilters'

/**
 * Task 053m — the user added a filter but has not used it yet (no value, or "Gjenero" not pressed) and leaves the page:
 * "Keni krijuar një filtër që nuk e keni përdorur. Dëshironi të dilni nga faqja?" — Po leaves (and the added filter goes
 * with the page), Anulo stays. Only a change of page asks; the page's own filter changes (the query) never do.
 */
export function UnusedFilterGuard({ unused }: { unused: boolean }) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    blocksLeaving(unused, currentLocation.pathname, nextLocation.pathname),
  )
  return (
    <Dialog open={blocker.state === 'blocked'} onClose={() => blocker.reset?.()} aria-describedby="unused-filter-text">
      <DialogContent>
        <DialogContentText id="unused-filter-text">{sq.filters.unusedLeave}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => blocker.reset?.()} autoFocus>
          {sq.filters.stay}
        </Button>
        <Button color="error" onClick={() => blocker.proceed?.()}>
          {sq.filters.leave}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
