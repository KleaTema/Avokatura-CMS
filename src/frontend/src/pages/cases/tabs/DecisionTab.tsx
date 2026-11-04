import {
  Alert,
  Box,
  Button,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { Archive, Eye, FileCheck, Gavel } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { CloseCaseDialog, type CloseCaseMode } from '../../../components/CloseCaseDialog'
import { ArchiveDossierDialog, RetrialDialog } from '../../../components/DecisionDialogs'
import { DocumentPreviewDialog } from '../../../components/documents/DocumentDialogs'
import { EmptyState } from '../../../components/ui/EmptyState'
import { SectionCard } from '../../../components/ui/SectionCard'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import { SUGGESTIONS, type CaseDecision, type CaseDetail } from '../../../features/cases/api'
import { fetchDocument, type FetchedFile } from '../../../features/documents/api'
import { sq } from '../../../i18n/sq'
import { formatDate } from '../../../lib/dates'
import { VALUE_TYPES } from '../../../lib/decisionForm'
import { formatDecimal, formatMoney } from '../../../lib/money'
import { problemMessage } from '../../../lib/problem'

const t = sq.cases.page
const v = t.decision

/** Which step the dialog opens at: "Shto vendimin e arsyetuar" goes straight to the reasoned decision. */
const REASONED_STEP = 3

/**
 * Vendimi (task 039). No decision yet → "Regjistro vendimin dhe mbyll çështjen" (decision.record). Recorded → the
 * summary, the values per type, the flags and the reasoned decision ("Shiko" the PDF); "Plotëso / Ndrysho vendimin"
 * (decision.record) and, while the reasoned decision has not arrived, a highlighted notice with "Shto vendimin e
 * arsyetuar". After saving: the API's suggestions — the retrial note, then "Arkivo çështjen?" (decision.close_final,
 * also offered here later). Every button from permissions[] only.
 */
export function DecisionTab({ detail }: { detail: CaseDetail }) {
  const can = (code: string) => detail.permissions.includes(code)
  const decision = detail.decision
  const [dialog, setDialog] = useState<{ mode: CloseCaseMode; step: number } | null>(null)
  const [retrial, setRetrial] = useState(false)
  const [archiveOffer, setArchiveOffer] = useState(false)
  const [archiving, setArchiving] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  const done = (suggestions: string[]) => {
    setToast(dialog?.mode === 'record' ? v.closed : v.saved)
    setDialog(null)
    setRetrial(suggestions.includes(SUGGESTIONS.retrial))
    setArchiveOffer(suggestions.includes(SUGGESTIONS.archive))
  }

  // the suggested archive dialog waits for the retrial note, and for the reloaded permissions (decision.close_final)
  const showArchive = archiving || (archiveOffer && !retrial && can('decision.close_final'))

  return (
    <Stack spacing={2}>
      {!decision ? (
        <SectionCard title={v.title}>
          <EmptyState
            icon={Gavel}
            title={v.none}
            action={
              can('decision.record') && (
                <Button variant="contained" color="secondary" onClick={() => setDialog({ mode: 'record', step: 0 })}>
                  {v.record}
                </Button>
              )
            }
          />
        </SectionCard>
      ) : (
        <>
          {!decision.decisionReceived && (
            <Alert
              severity="warning"
              variant="filled"
              icon={<FileCheck size={22} aria-hidden />}
              action={
                can('decision.record') && (
                  <Button color="inherit" variant="outlined" size="small" onClick={() => setDialog({ mode: 'update', step: REASONED_STEP })}>
                    {v.addReasoned}
                  </Button>
                )
              }
            >
              <strong>{v.notReceived}</strong> — {v.notReceivedHint}
            </Alert>
          )}
          <SectionCard
            title={v.title}
            actions={
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                {can('decision.record') && (
                  <Button variant="outlined" onClick={() => setDialog({ mode: 'update', step: 0 })}>
                    {v.edit}
                  </Button>
                )}
                {can('decision.close_final') && (
                  <Button variant="contained" color="secondary" startIcon={<Archive size={20} aria-hidden />} onClick={() => setArchiving(true)}>
                    {v.archive}
                  </Button>
                )}
              </Stack>
            }
          >
            <DecisionSummary decision={decision} appealKind={detail.data.appealKind} />
          </SectionCard>
          <SectionCard title={v.values}>
            <ValuesTable decision={decision} />
          </SectionCard>
        </>
      )}

      {dialog && (
        <CloseCaseDialog detail={detail} mode={dialog.mode} initialStep={dialog.step} onClose={() => setDialog(null)} onDone={done} />
      )}
      {retrial && <RetrialDialog onClose={() => setRetrial(false)} />}
      {showArchive && (
        <ArchiveDossierDialog
          detail={detail}
          onClose={() => {
            setArchiveOffer(false)
            setArchiving(false)
          }}
          onArchived={() => {
            setArchiveOffer(false)
            setArchiving(false)
            setToast(v.archived)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

function DecisionSummary({ decision, appealKind }: { decision: CaseDecision; appealKind: string | null | undefined }) {
  const [preview, setPreview] = useState<{ file: FetchedFile; url: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // the preview's blob URL is revoked on close, and when the tab is left with it open
  const previewUrl = useRef<string | null>(null)
  useEffect(
    () => () => {
      if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    },
    [],
  )
  const closePreview = () => {
    if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    previewUrl.current = null
    setPreview(null)
  }
  const open = async (documentId: string, title: string) => {
    setBusy(true)
    setError(null)
    try {
      const file = await fetchDocument(documentId, 'preview', `${title}.pdf`)
      previewUrl.current = URL.createObjectURL(file.blob)
      setPreview({ file, url: previewUrl.current })
    } catch (e) {
      setError(problemMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const yesNo = (value: boolean) => (value ? t.yes : t.no)
  const result = decision.resultOtherText ? `${decision.result.name}: ${decision.resultOtherText}` : decision.result.name
  const rows: [string, ReactNode][] = [
    [v.result, result],
    [v.number, decision.decisionNumber ?? t.none],
    [v.date, formatDate(decision.decisionDate) || t.none],
    // only the checkbox this level has (court_levels.appeal_kind)
    ...(appealKind === 'ANKIM' ? [[v.appealed, yesNo(decision.isAppealed)] as [string, string]] : []),
    ...(appealKind === 'REKURS' ? [[v.recursed, yesNo(decision.isRecursed)] as [string, string]] : []),
    [v.final, yesNo(decision.isFinal)],
    [
      v.received,
      decision.document ? (
        <Stack key="received" direction="row" spacing={1} sx={{ alignItems: 'center' }} component="span">
          <span>{yesNo(decision.decisionReceived)}</span>
          <Button
            size="small"
            startIcon={<Eye size={16} aria-hidden />}
            disabled={busy}
            onClick={() => void open(decision.document!.publicId, decision.document!.title)}
            aria-label={`${v.view}: ${decision.document.title}`}
          >
            {v.view}
          </Button>
        </Stack>
      ) : (
        yesNo(decision.decisionReceived)
      ),
    ],
  ]

  return (
    <Stack spacing={2}>
      {error && (
        <Alert severity="error" onClose={() => setError(null)}>
          {error}
        </Alert>
      )}
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
      {decision.dispositive && (
        <Box>
          <Typography variant="body2" color="text.secondary">
            {v.dispositive}
          </Typography>
          <Typography sx={{ whiteSpace: 'pre-wrap' }}>{decision.dispositive}</Typography>
        </Box>
      )}
      <Typography variant="caption" color="text.secondary">
        {v.recordedAt(formatDate(decision.recordedAt.slice(0, 10)))}
      </Typography>
      {preview && (
        <DocumentPreviewDialog title={decision.document?.title ?? v.document} file={preview.file} url={preview.url} onClose={closePreview} />
      )}
    </Stack>
  )
}

/** The amounts per value type (money and m² side by side) and the court costs; numbers right-aligned. */
function ValuesTable({ decision }: { decision: CaseDecision }) {
  const money = (value: string | null | undefined) => (value ? formatMoney(value, decision.currency) : t.none)
  const area = (value: string | null | undefined) => (value ? `${formatDecimal(value)} m²` : t.none)
  const values: Record<string, [string | null | undefined, string | null | undefined]> = {
    won: [decision.wonAmount, decision.wonAreaM2],
    lost: [decision.lostAmount, decision.lostAreaM2],
    preventedDamage: [decision.preventedDamageAmount, decision.preventedDamageAreaM2],
    causedDamage: [decision.causedDamageAmount, decision.causedDamageAreaM2],
  }
  return (
    <Table size="small" aria-label={v.values}>
      <TableHead>
        <TableRow>
          <TableCell>{v.type}</TableCell>
          <TableCell align="right">{v.amount}</TableCell>
          <TableCell align="right">{v.area}</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {VALUE_TYPES.map((type) => (
          <TableRow key={type}>
            <TableCell>{v.valueTypes[type]}</TableCell>
            <TableCell align="right">{money(values[type][0])}</TableCell>
            <TableCell align="right">{area(values[type][1])}</TableCell>
          </TableRow>
        ))}
        <TableRow>
          <TableCell>{v.courtCosts}</TableCell>
          <TableCell align="right">{money(decision.courtCostsAmount)}</TableCell>
          <TableCell align="right">{t.none}</TableCell>
        </TableRow>
      </TableBody>
    </Table>
  )
}
