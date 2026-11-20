import { Alert, Box, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import { useRegistrationSummary } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { useFullScreenDialog } from './ui/dialog'
import { StatusChip } from './ui/StatusChip'

const t = sq.cases.registrationSummary

/**
 * Task 054d: the registration data of a case the Protokoll registered and can no longer open (assigned by the Kryetar,
 * or closed) — read-only, registration level only (GET /api/cases/{id}/registration-summary).
 */
export function RegistrationSummaryDialog({ casePublicId, onClose }: { casePublicId: string; onClose: () => void }) {
  const summary = useRegistrationSummary(casePublicId)
  const fullScreen = useFullScreenDialog()
  const data = summary.data

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="registration-summary-title">
      <DialogTitle id="registration-summary-title">{data ? t.title(data.caseCode) : t.titleLoading}</DialogTitle>
      <DialogContent>
        {summary.isPending ? (
          <CircularProgress aria-label={sq.errors.loading} />
        ) : summary.isError ? (
          <Alert severity="error">{problemMessage(summary.error)}</Alert>
        ) : data ? (
          <Stack spacing={2}>
            <Alert severity="info">{t.note}</Alert>
            <Box>
              <StatusChip kind="status" value={data.status} />
            </Box>
            <Typography>
              {data.courtLevelName} · {data.courtName}
              {data.actNumber ? ` · ${t.act} ${data.actNumber}` : ''}
            </Typography>
            <Box>
              <Typography variant="h5" component="h3">
                {t.object}
              </Typography>
              <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{data.objectText}</Typography>
            </Box>
            <Box>
              <Typography variant="h5" component="h3">
                {t.parties}
              </Typography>
              {data.parties.length === 0 ? (
                <Typography color="text.secondary">{t.noParties}</Typography>
              ) : (
                <Box component="ul" sx={{ m: 0, pl: 3 }}>
                  {data.parties.map((p, i) => (
                    <li key={`${p.displayName}-${i}`}>
                      {p.role} — {p.displayName} ({sq.partyEditor.kinds[p.kind] ?? p.kind}
                      {p.identifier ? `, ${sq.partyEditor.nipt} ${p.identifier}` : ''})
                    </li>
                  ))}
                </Box>
              )}
            </Box>
          </Stack>
        ) : null}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} autoFocus>
          {sq.common.close}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
