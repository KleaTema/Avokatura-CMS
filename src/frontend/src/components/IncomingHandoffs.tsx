import { Alert, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material'
import { Inbox } from 'lucide-react'
import { Link } from 'react-router'
import { useIncomingHandoffs } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { formatDate, formatDateTime } from '../lib/dates'
import { problemMessage } from '../lib/problem'
import { CaseCode } from './ui/CaseCode'
import { EmptyState } from './ui/EmptyState'

const t = sq.handoff.incoming

/** The next-level form, pre-filled with the destination's level and court (ContinueCase reads them). */
export const continueLink = (casePublicId: string, levelCode: string, courtId: number) =>
  `/cases/continue?case=${casePublicId}&level=${encodeURIComponent(levelCode)}&court=${courtId}`

/**
 * "Çështjet e ardhura" (task 053c): cases another office handed off to the Protokoll's office. Registration-level
 * data only — code, parties, object, court, result, decision date; no documents, no chat. "Vazhdo" opens the
 * next-level form with the destination's court level and court; once X/n+1 is registered the case leaves the list.
 */
export function IncomingHandoffs() {
  const incoming = useIncomingHandoffs(true)
  const items = incoming.data?.items ?? []

  if (incoming.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (incoming.isError) return <Alert severity="error">{problemMessage(incoming.error)}</Alert>

  return (
    <Stack spacing={2}>
      <Typography color="text.secondary">{t.intro}</Typography>
      {items.length === 0 ? (
        <EmptyState icon={Inbox} title={t.empty} />
      ) : (
        <Stack spacing={2} component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }} aria-label={sq.cases.list.tabs.incoming}>
          {items.map((h) => (
            <Paper key={h.handoffPublicId} component="li" variant="outlined" sx={{ p: 2 }}>
              <Stack spacing={1}>
                <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                  <Typography variant="h3" component="h2">
                    <CaseCode code={h.caseCode} />
                  </Typography>
                  <Typography color="text.secondary">
                    {t.handedOffAt} {formatDateTime(h.handedOffAt)}
                  </Typography>
                </Stack>
                <Typography sx={{ fontWeight: 600 }}>{t.destination(h.courtLevelName, h.courtName)}</Typography>
                <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'max-content 1fr' }, columnGap: 2, rowGap: 0.5 }}>
                  <Row label={t.fromCourt} value={h.fromCourtName} />
                  <Row label={t.result} value={h.resultName ?? sq.cases.page.none} />
                  <Row label={t.decisionDate} value={h.decisionDate ? formatDate(h.decisionDate) : sq.cases.page.none} />
                  <Row label={t.parties} value={h.partyNames.length > 0 ? h.partyNames.join(', ') : sq.cases.page.none} />
                  <Row label={t.object} value={h.objectText} />
                </Box>
                <Box>
                  <Button
                    variant="contained"
                    component={Link}
                    to={continueLink(h.casePublicId, h.courtLevelCode, h.courtId)}
                    aria-label={t.continueFor(h.caseCode)}
                  >
                    {t.continue}
                  </Button>
                </Box>
              </Stack>
            </Paper>
          ))}
        </Stack>
      )}
      {Number(incoming.data?.total ?? 0) > items.length && (
        <Typography variant="body2" color="text.secondary">
          {t.more(items.length)}
        </Typography>
      )}
    </Stack>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <>
      <Typography component="dt" color="text.secondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, whiteSpace: 'pre-wrap' }}>
        {value}
      </Typography>
    </>
  )
}
