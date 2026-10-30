import { Box, Paper, Stack, Typography } from '@mui/material'
import type { CaseDetail } from '../../../features/cases/api'
import { sq } from '../../../i18n/sq'
import { formatDate } from '../../../lib/dates'

const t = sq.cases.page

/** Seancat — the count from the case; the list comes with task 030. */
export function HearingsTab({ detail }: { detail: CaseDetail }) {
  return <Summary text={t.hearingsCount(Number(detail.hearingsCount))} />
}

/** Dokumentet — the count from the case; the list comes with task 034. */
export function DocumentsTab({ detail }: { detail: CaseDetail }) {
  return <Summary text={t.documentsCount(Number(detail.documentsCount))} />
}

/** Vendimi — the decision summary of this level, if any; recording it comes with task 036. */
export function DecisionTab({ detail }: { detail: CaseDetail }) {
  const decision = detail.decision
  const v = t.decision
  if (!decision) return <Summary text={v.none} />
  const yesNo = (value: boolean) => (value ? t.yes : t.no)
  const rows: [string, string][] = [
    [v.result, decision.resultOtherText ? `${decision.result.name}: ${decision.resultOtherText}` : decision.result.name],
    [v.number, decision.decisionNumber ?? t.none],
    [v.date, formatDate(decision.decisionDate) || t.none],
    [v.received, yesNo(decision.decisionReceived)],
    [v.appealed, yesNo(decision.isAppealed)],
    [v.recursed, yesNo(decision.isRecursed)],
    [v.final, yesNo(decision.isFinal)],
  ]
  return (
    <Stack spacing={2}>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          {rows.map(([label, value]) => (
            <Box key={label}>
              <Typography component="dt" variant="body2" color="text.secondary">
                {label}
              </Typography>
              <Typography component="dd" sx={{ m: 0 }}>
                {value}
              </Typography>
            </Box>
          ))}
        </Box>
      </Paper>
      <Typography color="text.secondary">{t.soon}</Typography>
    </Stack>
  )
}

function Summary({ text }: { text: string }) {
  return (
    <Stack spacing={1}>
      <Typography>{text}</Typography>
      <Typography color="text.secondary">{t.soon}</Typography>
    </Stack>
  )
}
