import { Box, Paper, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import type { CaseDetail } from '../../../features/cases/api'
import { sq } from '../../../i18n/sq'
import { formatDate, formatDateTime } from '../../../lib/dates'

const t = sq.cases.page
const d = t.data

const money = (amount: number | string, currency: string | null) =>
  new Intl.NumberFormat('sq-AL', { style: 'currency', currency: currency ?? 'ALL' }).format(Number(amount))

/** Të dhënat — read-only (editing comes with task 025). DateOnly fields with formatDate, moments with formatDateTime. */
export function DataTab({ detail }: { detail: CaseDetail }) {
  const { data, team, institutions } = detail
  const claim = data.claim
  const claimText = claim
    ? [
        claim.amount !== null ? money(claim.amount, claim.currency) : null,
        claim.areaM2 !== null ? d.area(new Intl.NumberFormat('sq-AL').format(Number(claim.areaM2))) : null,
      ]
        .filter(Boolean)
        .join(' · ')
    : null

  return (
    <Stack spacing={2}>
      <Section title={d.summons}>
        <Field label={d.summonsNumber} value={data.summonsNumber} />
        <Field label={d.summonsType} value={data.summonsType?.name} />
        <Field label={d.actNumber} value={data.actNumber} />
        <Field label={d.actDate} value={formatDate(data.actDate)} />
        <Field label={d.receivedDate} value={formatDate(data.receivedDate)} />
        <Field label={d.courtLevel} value={data.courtLevel?.name} />
        <Field label={d.court} value={data.courtName} />
        <Field label={d.judge} value={data.judgeName} />
      </Section>

      <Section title={d.object}>
        <Typography sx={{ whiteSpace: 'pre-wrap', gridColumn: '1 / -1' }}>{data.objectText}</Typography>
      </Section>

      <Section title={d.classification}>
        <Field label={d.category} value={data.category?.name} />
        <Field label={d.subcategory} value={data.subcategory?.name} />
        <Field label={d.claim} value={claimText} />
        <Field label={d.securityMeasure} value={data.hasSecurityMeasure ? t.yes : t.no} />
      </Section>

      <Section title={d.dates}>
        <Field label={t.nextHearing} value={data.nextHearingAt ? formatDateTime(data.nextHearingAt) : null} />
        <Field label={t.registeredAt} value={formatDateTime(data.registeredAt)} />
        <Field label={d.registeredBy} value={data.registeredBy?.name} />
        <Field label={d.closedAt} value={data.closedAt ? formatDateTime(data.closedAt) : null} />
      </Section>

      <Section title={d.team}>
        {team.length === 0 ? (
          <Typography color="text.secondary" sx={{ gridColumn: '1 / -1' }}>
            {d.noTeam}
          </Typography>
        ) : (
          team.map((member) => (
            <Field key={`${member.role}-${member.userPublicId}`} label={d.roles[member.role] ?? member.role} value={member.name} />
          ))
        )}
      </Section>

      {institutions.length > 0 && (
        <Section title={d.institutions}>
          {institutions.map((i) => (
            <Field
              key={i.institutionPublicId}
              label={i.name}
              value={[d.modes[i.mode] ?? i.mode, i.juristNames.length > 0 ? `${d.jurists}: ${i.juristNames.join(', ')}` : null]
                .filter(Boolean)
                .join(' · ')}
            />
          ))}
        </Section>
      )}
    </Stack>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Paper variant="outlined" component="section" sx={{ p: 2 }}>
      <Typography variant="h6" component="h2" gutterBottom>
        {title}
      </Typography>
      <Box
        component="dl"
        sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}
      >
        {children}
      </Box>
    </Paper>
  )
}

function Field({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <Box>
      <Typography component="dt" variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0 }}>
        {value || t.none}
      </Typography>
    </Box>
  )
}
