import { Alert, AlertTitle, Box, Button, CircularProgress, Divider, Stack, Typography } from '@mui/material'
import { CalendarCheck, CalendarPlus, Pencil } from 'lucide-react'
import { Fragment, useState } from 'react'
import { AddHearingDialog, CompleteHearingDialog, EditHearingDialog, type HearingSaved } from '../../../components/HearingDialogs'
import { EmptyState } from '../../../components/ui/EmptyState'
import { SectionCard } from '../../../components/ui/SectionCard'
import { StatusChip } from '../../../components/ui/StatusChip'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import {
  hearingTypeName,
  useHearings,
  type CaseDetail,
  type HearingView,
  type HearingWarning,
} from '../../../features/cases/api'
import { sq } from '../../../i18n/sq'
import { formatDateTime } from '../../../lib/dates'
import { problemMessage } from '../../../lib/problem'

const t = sq.cases.hearings

type Open = { kind: 'add' } | { kind: 'complete' | 'edit'; hearingId: string } | null

/**
 * Seancat (task 030): the next hearing on top (red banner when it has passed without an outcome), then every hearing
 * of this level, newest first. Actions from the case's permissions only: hearing.add → "Shto seancë",
 * hearing.edit → "Përfundo seancën" and "Ndrysho".
 */
export function HearingsTab({ detail }: { detail: CaseDetail }) {
  const caseId = detail.header.publicId
  const hearings = useHearings(caseId)
  const [open, setOpen] = useState<Open>(null)
  const [toast, setToast] = useState<string | null>(null)
  const [warnings, setWarnings] = useState<HearingWarning[]>([])

  const canAdd = detail.permissions.includes('hearing.add')
  const canEdit = detail.permissions.includes('hearing.edit')

  if (hearings.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (hearings.isError) return <Alert severity="error">{problemMessage(hearings.error)}</Alert>

  const list = hearings.data
  // the next hearing is the earliest still SCHEDULED (the case's next_hearing_at, also when it has passed)
  const next = list
    .filter((h) => h.status === 'SCHEDULED')
    .reduce<HearingView | null>((first, h) => (first === null || h.scheduledAt < first.scheduledAt ? h : first), null)

  const done = (saved: HearingSaved) => {
    setOpen(null)
    setToast(saved.message)
    setWarnings(saved.warnings)
  }
  const completeButton = (hearing: HearingView) => (
    <Button
      variant="contained"
      color="secondary"
      startIcon={<CalendarCheck size={20} aria-hidden />}
      onClick={() => setOpen({ kind: 'complete', hearingId: hearing.publicId })}
      sx={{ flexShrink: 0 }}
    >
      {t.complete}
    </Button>
  )

  return (
    <Stack spacing={2}>
      {warnings.length > 0 && (
        <Alert severity="warning" onClose={() => setWarnings([])}>
          <AlertTitle>{t.warningsTitle}</AlertTitle>
          {warnings.map((w) => (
            <Typography key={w.code} variant="body2">
              {w.message}
            </Typography>
          ))}
        </Alert>
      )}

      <SectionCard title={t.nextTitle} actions={next && canEdit && !next.isOverdue ? completeButton(next) : undefined}>
        {next ? (
          <Stack spacing={2}>
            {next.isOverdue && (
              <Alert
                severity="error"
                action={canEdit ? completeButton(next) : undefined}
                sx={{ alignItems: 'center', flexWrap: 'wrap', '& .MuiAlert-action': { pt: 0, ml: { xs: 0, sm: 'auto' } } }}
              >
                {t.overdue(formatDateTime(next.scheduledAt))}
              </Alert>
            )}
            <Box
              component="dl"
              sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' }, gap: 2 }}
            >
              <Fact label={t.when} value={formatDateTime(next.scheduledAt)} strong error={next.isOverdue} />
              <Fact label={t.type} value={hearingTypeName(next)} />
              <Fact label={t.court} value={detail.data.courtName} />
            </Box>
          </Stack>
        ) : (
          <Typography color="text.secondary">{t.noNext}</Typography>
        )}
      </SectionCard>

      <SectionCard
        title={t.timelineTitle}
        actions={
          canAdd && (
            <Button variant="outlined" size="small" startIcon={<CalendarPlus size={18} aria-hidden />} onClick={() => setOpen({ kind: 'add' })}>
              {t.add}
            </Button>
          )
        }
      >
        {list.length === 0 ? (
          <EmptyState title={t.empty} />
        ) : (
          <Stack component="ol" sx={{ m: 0, p: 0, listStyle: 'none' }}>
            {list.map((h, index) => (
              <Fragment key={h.publicId}>
                {index > 0 && <Divider component="li" aria-hidden />}
                <HearingRow hearing={h} canEdit={canEdit} onEdit={() => setOpen({ kind: 'edit', hearingId: h.publicId })} />
              </Fragment>
            ))}
          </Stack>
        )}
      </SectionCard>

      {open?.kind === 'add' && <AddHearingDialog caseId={caseId} onClose={() => setOpen(null)} onDone={done} />}
      {open?.kind === 'complete' && (
        <CompleteHearingDialog caseId={caseId} hearingId={open.hearingId} onClose={() => setOpen(null)} onDone={done} />
      )}
      {open?.kind === 'edit' && (
        <EditHearingDialog caseId={caseId} hearingId={open.hearingId} onClose={() => setOpen(null)} onDone={done} />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

/** One hearing of the timeline; a cancelled one is struck through and muted (the chip still says "U anulua"). */
function HearingRow({ hearing, canEdit, onEdit }: { hearing: HearingView; canEdit: boolean; onEdit: () => void }) {
  const cancelled = hearing.status === 'CANCELLED'
  const when = formatDateTime(hearing.scheduledAt)
  const who = [
    hearing.createdBy ? t.createdBy(hearing.createdBy) : null,
    hearing.updatedBy ? t.updatedBy(hearing.updatedBy) : null,
  ]
    .filter((x) => x !== null)
    .join(' · ')
  return (
    <Box component="li" sx={{ display: 'flex', alignItems: 'flex-start', gap: 2, py: 1.5 }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography
            sx={{
              fontWeight: 600,
              color: cancelled ? 'text.secondary' : hearing.isOverdue ? 'error.main' : 'text.primary',
              textDecoration: cancelled ? 'line-through' : 'none',
            }}
          >
            {when}
          </Typography>
          <StatusChip kind="hearing" value={hearing.status} />
          {hearing.isOverdue && <StatusChip kind="deadlineSoon" label={t.overdueShort} />}
        </Stack>
        <Typography
          variant="body2"
          sx={{ color: cancelled ? 'text.secondary' : 'text.primary', textDecoration: cancelled ? 'line-through' : 'none' }}
        >
          {hearingTypeName(hearing)}
        </Typography>
        <Typography variant="body2" color={hearing.description ? (cancelled ? 'text.secondary' : 'text.primary') : 'text.secondary'} sx={{ mt: 0.5, whiteSpace: 'pre-line' }}>
          {hearing.description ?? t.noDescription}
        </Typography>
        {who && (
          <Typography variant="caption" color="text.secondary">
            {who}
          </Typography>
        )}
      </Box>
      {canEdit && (
        <Button size="small" startIcon={<Pencil size={16} aria-hidden />} onClick={onEdit} aria-label={t.editFor(when)} sx={{ flexShrink: 0 }}>
          {t.edit}
        </Button>
      )}
    </Box>
  )
}

function Fact({ label, value, strong, error }: { label: string; value: string; strong?: boolean; error?: boolean }) {
  return (
    <Box>
      <Typography component="dt" variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, fontWeight: strong ? 600 : undefined, color: error ? 'error.main' : undefined }}>
        {value}
        {error && ` (${t.overdueShort})`}
      </Typography>
    </Box>
  )
}
