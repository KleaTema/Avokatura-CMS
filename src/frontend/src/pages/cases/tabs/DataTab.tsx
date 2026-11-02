import { Alert, Box, Stack, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import { EditableSection } from '../../../components/caseData/EditableSection'
import {
  CategoryEditor,
  ClaimEditor,
  PriorityEditor,
  SecurityMeasureEditor,
  SuspensionEditor,
} from '../../../components/caseData/editors'
import { TeamCard } from '../../../components/TeamCard'
import { SectionCard } from '../../../components/ui/SectionCard'
import { StatusChip } from '../../../components/ui/StatusChip'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import type { CaseDetail } from '../../../features/cases/api'
import { useCaseCategories } from '../../../features/reference/api'
import { sq } from '../../../i18n/sq'
import { formatDate, formatDateTime } from '../../../lib/dates'
import { formatDecimal, formatMoney } from '../../../lib/money'

const t = sq.cases.page
const d = t.data
const e = sq.cases.edit

/**
 * Të dhënat (task 025): read-only cards; a card with an action in the case's permissions gets "Ndrysho" and edits
 * in place. Every save puts the returned rowVersion into the case cache, so the next save uses it.
 * DateOnly fields with formatDate, moments with formatDateTime; money as strings (lib/money), never JS numbers.
 */
export function DataTab({ detail }: { detail: CaseDetail }) {
  const { data, institutions, permissions } = detail
  const can = (code: string) => permissions.includes(code)
  const categories = useCaseCategories().data ?? []
  const [toast, setToast] = useState<string | null>(null)
  const saved = (close: () => void) => () => {
    close()
    setToast(e.saved)
  }

  // DELEGIM/RELATIM is set and changed only through the delegation (task 027)
  const delegated = categories.some((c) => c.code === data.category?.code && c.isDelegation)
  const claim = data.claim

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

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
        <EditableSection
          title={e.claim.title}
          canEdit={can('case.edit_data')}
          view={
            <Fields>
              <Field label={e.claim.amount} value={claim?.amount ? formatMoney(claim.amount, claim.currency) : null} />
              <Field label={e.claim.area} value={claim?.areaM2 ? d.area(formatDecimal(claim.areaM2)) : null} />
            </Fields>
          }
          editor={(close) => <ClaimEditor detail={detail} onSaved={saved(close)} onCancel={close} />}
        />

        <EditableSection
          title={e.category.title}
          canEdit={can('case.set_category') && !delegated}
          view={
            <Stack spacing={1.5}>
              <Fields>
                <Field label={e.category.category} value={data.category?.name} />
                <Field label={e.category.subcategory} value={data.subcategory?.name} />
              </Fields>
              {delegated && <Alert severity="info">{e.category.delegatedHint}</Alert>}
            </Stack>
          }
          editor={(close) => <CategoryEditor detail={detail} onSaved={saved(close)} onCancel={close} />}
        />

        <EditableSection
          title={e.status.title}
          canEdit={can('case.set_status')}
          view={
            detail.header.isSuspended ? (
              <Stack direction="row" spacing={1.5} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <StatusChip kind="suspended" />
                {data.suspendedAt && <Typography variant="body2">{e.status.since(formatDateTime(data.suspendedAt))}</Typography>}
              </Stack>
            ) : (
              <Typography color="text.secondary">{e.status.running}</Typography>
            )
          }
          editor={(close) => <SuspensionEditor detail={detail} onSaved={saved(close)} onCancel={close} />}
        />

        <EditableSection
          title={e.measure.title}
          canEdit={can('case.edit_data')}
          view={
            data.hasSecurityMeasure ? (
              <Fields>
                <Field label={e.measure.decisionNumber} value={data.securityMeasure?.decisionNumber} />
                <Field label={e.measure.decisionDate} value={formatDate(data.securityMeasure?.decisionDate)} />
                <Box sx={{ gridColumn: '1 / -1' }}>
                  <Field label={e.measure.dispositive} value={data.securityMeasure?.dispositive} />
                </Box>
              </Fields>
            ) : (
              <Typography color="text.secondary">{e.measure.none}</Typography>
            )
          }
          editor={(close) => <SecurityMeasureEditor detail={detail} onSaved={saved(close)} onCancel={close} />}
        />

        <EditableSection
          title={e.priority.title}
          canEdit={can('case.set_priority') || can('case.set_priority_any')}
          view={<StatusChip kind="priority" value={detail.header.priority} />}
          editor={(close) => <PriorityEditor detail={detail} onSaved={saved(close)} onCancel={close} />}
        />
      </Box>

      <Section title={d.dates}>
        <Field label={t.nextHearing} value={data.nextHearingAt ? formatDateTime(data.nextHearingAt) : null} />
        <Field label={t.registeredAt} value={formatDateTime(data.registeredAt)} />
        <Field label={d.registeredBy} value={data.registeredBy?.name} />
        <Field label={d.closedAt} value={data.closedAt ? formatDateTime(data.closedAt) : null} />
      </Section>

      <TeamCard detail={detail} />

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

      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <SectionCard title={title}>
      <Fields>{children}</Fields>
    </SectionCard>
  )
}

function Fields({ children }: { children: ReactNode }) {
  return (
    <Box
      component="dl"
      sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}
    >
      {children}
    </Box>
  )
}

function Field({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <Box>
      <Typography component="dt" variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, whiteSpace: 'pre-wrap' }}>
        {value || t.none}
      </Typography>
    </Box>
  )
}
