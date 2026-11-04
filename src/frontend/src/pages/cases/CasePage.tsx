import { Alert, Box, Button, CircularProgress, Link as MuiLink, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Archive, ArrowLeft, UserPlus } from 'lucide-react'
import { AssignLawyerDialog } from '../../components/AssignLawyerDialog'
import { HeaderPrioritySelect } from '../../components/caseData/HeaderPrioritySelect'
import { CaseCode } from '../../components/ui/CaseCode'
import { StatusChip } from '../../components/ui/StatusChip'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { LevelSwitcher } from '../../components/LevelSwitcher'
import { useCase } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { formatDate, formatDateTime, tiraneDateOf } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'
import { DataTab } from './tabs/DataTab'
import { DocumentsTab } from './tabs/DocumentsTab'
import { HearingsTab } from './tabs/HearingsTab'
import { DecisionTab } from './tabs/DecisionTab'
import type { FromCaseList } from './CaseList'
import { HistoryTab } from './tabs/HistoryTab'
import { PartiesTab } from './tabs/PartiesTab'

const t = sq.cases.page

const CASE_TABS =['te-dhenat', 'palet', 'seancat', 'dokumentet', 'vendimi', 'historiku'] as const
type CaseTab = (typeof CASE_TABS)[number]
const isCaseTab = (value: string | undefined): value is CaseTab => CASE_TABS.some((x) => x === value)

/**
 * The case page (task 022): /cases/:publicId/:tab. Every action button comes from `permissions` of the case
 * (GET /api/cases/{id}) — never from role names or the status. 404 never says whether the case exists.
 */
/** How many history entries back the case list is, when this page was opened from it (CaseList's FromCaseList). */
function listDepthOf(state: unknown): number | null {
  const depth = (state as Partial<FromCaseList> | null)?.listDepth
  return typeof depth === 'number' && Number.isInteger(depth) && depth > 0 ? depth : null
}

/**
 * "← Kthehu te lista e çështjeve": opened from the case list → back through the history to that list entry (its
 * filters and page in the URL, the cursors in sessionStorage); otherwise (a link, a refresh) → /cases.
 */
function BackToList({ depth }: { depth: number | null }) {
  const navigate = useNavigate()
  return (
    <Box>
      <MuiLink
        component="button"
        type="button"
        variant="body2"
        underline="hover"
        onClick={() => void (depth ? navigate(-depth) : navigate('/cases'))}
        sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}
      >
        <ArrowLeft size={18} aria-hidden />
        {t.backToList}
      </MuiLink>
    </Box>
  )
}

export function CasePage() {
  const { publicId = '', tab } = useParams()
  const location = useLocation()
  const listDepth = listDepthOf(location.state)
  // every tab or level link of this page is one more entry between here and the list
  const linkState: FromCaseList | undefined = listDepth ? { listDepth: listDepth + 1 } : undefined
  const detail = useCase(publicId)
  const [copied, setCopied] = useState(false)
  const [assigning, setAssigning] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  if (tab !== undefined && !isCaseTab(tab)) return <Navigate to={`/cases/${publicId}`} replace state={location.state} />
  const current: CaseTab = isCaseTab(tab) ? tab : 'te-dhenat'

  if (detail.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (detail.isError) {
    const notFound = detail.error instanceof ApiError && (detail.error.status === 404 || detail.error.status === 400)
    return (
      <Stack spacing={2}>
        <Alert severity={notFound ? 'warning' : 'error'}>{notFound ? t.notFound : problemMessage(detail.error)}</Alert>
        <BackToList depth={listDepth} />
      </Stack>
    )
  }

  const c = detail.data
  const { header, data } = c
  // from permissions[] only: case.assign_lawyer exists only while REGISTERED (CaseAccessService state rules)
  const canAssign = c.permissions.includes('case.assign_lawyer')
  // hearings and documents of this level: the counts follow every change (afterCaseChange reloads the case)
  const counts: Partial<Record<CaseTab, number | string>> = { seancat: c.hearingsCount, dokumentet: c.documentsCount }
  const tabLabel = (x: CaseTab) => (counts[x] === undefined ? t.tabs[x] : `${t.tabs[x]} (${counts[x]})`)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(header.caseCode)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Stack spacing={2}>
      <BackToList depth={listDepth} />
      {header.isArchived && (
        <Alert severity="info" icon={<Archive size={22} aria-hidden />} role="status">
          {sq.cases.page.decision.archivedBanner}
        </Alert>
      )}

      <Paper variant="outlined" sx={{ p: { xs: 2, md: 3 } }}>
        <Stack spacing={1.5}>
          <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <Box component="h1" sx={{ m: 0 }}>
              <CaseCode code={header.caseCode} size="large" />
            </Box>
            <Button variant="outlined" size="small" onClick={() => void copy()}>
              {sq.cases.register.success.copy}
            </Button>
            <Typography role="status" variant="body2" color="success.dark">
              {copied ? sq.cases.register.success.copied : ''}
            </Typography>
            {canAssign && (
              <Button
                variant="contained"
                startIcon={<UserPlus size={20} aria-hidden />}
                onClick={() => setAssigning(true)}
                sx={{ ml: { sm: 'auto' } }}
              >
                {sq.cases.assign.button}
              </Button>
            )}
          </Stack>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
            <StatusChip
              kind="status"
              value={header.status}
              size="medium"
              label={header.status === 'CLOSED' && data.closedAt ? sq.cases.page.decision.closedOn(formatDate(tiraneDateOf(data.closedAt))) : undefined}
            />
            {/* the Kryetar sets the priority right here (case.set_priority_any); everyone else sees the chip */}
            {c.permissions.includes('case.set_priority_any') ? (
              <HeaderPrioritySelect detail={c} />
            ) : (
              <StatusChip
                kind="priority"
                value={header.priority}
                size="medium"
                label={`${sq.cases.list.columns.priority}: ${sq.cases.priority[header.priority] ?? header.priority}`}
              />
            )}
            {header.isSuspended && <StatusChip kind="suspended" size="medium" />}
          </Stack>
          <Typography variant="body2" color="text.secondary">
            <strong>{t.court}:</strong> {data.courtName}
            {' · '}
            <strong>{t.nextHearing}:</strong>{' '}
            {data.nextHearingAt ? (
              <Box component="span" sx={data.nextHearingIsOverdue ? { color: 'error.main', fontWeight: 600 } : undefined}>
                {formatDateTime(data.nextHearingAt)}
                {data.nextHearingIsOverdue && ` (${sq.cases.hearings.overdueShort})`}
              </Box>
            ) : (
              t.none
            )}
            {' · '}
            <strong>{t.registeredAt}:</strong> {formatDateTime(data.registeredAt)}
          </Typography>
          <LevelSwitcher levels={c.levels} currentPublicId={header.publicId} tab={current} linkState={linkState} />
        </Stack>
      </Paper>

      <Tabs value={current} variant="scrollable" allowScrollButtonsMobile aria-label={t.tabsLabel}>
        {CASE_TABS.map((x) => (
          <Tab key={x} value={x} label={tabLabel(x)} component={Link} to={`/cases/${publicId}/${x}`} state={linkState} />
        ))}
      </Tabs>

      <Box role="tabpanel" aria-label={t.tabs[current]}>
        {current === 'te-dhenat' && <DataTab detail={c} />}
        {current === 'palet' && <PartiesTab detail={c} />}
        {current === 'seancat' && <HearingsTab detail={c} />}
        {current === 'dokumentet' && <DocumentsTab detail={c} />}
        {current === 'vendimi' && <DecisionTab detail={c} />}
        {current === 'historiku' && <HistoryTab casePublicId={header.publicId} />}
      </Box>

      {assigning && (
        <AssignLawyerDialog
          casePublicId={header.publicId}
          onClose={() => setAssigning(false)}
          onAssigned={(name) => {
            setAssigning(false)
            setToast(sq.cases.assign.success(name))
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}
