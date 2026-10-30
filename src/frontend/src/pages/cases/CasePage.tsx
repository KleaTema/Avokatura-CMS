import { Alert, Box, Button, Chip, CircularProgress, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { useState } from 'react'
import { Link, Navigate, useParams } from 'react-router'
import { ApiError } from '../../api/client'
import { LevelSwitcher } from '../../components/LevelSwitcher'
import { useCase } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'
import { DataTab } from './tabs/DataTab'
import { DecisionTab, DocumentsTab, HearingsTab } from './tabs/SummaryTabs'
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
export function CasePage() {
  const { publicId = '', tab } = useParams()
  const detail = useCase(publicId)
  const [copied, setCopied] = useState(false)

  if (tab !== undefined && !isCaseTab(tab)) return <Navigate to={`/cases/${publicId}`} replace />
  const current: CaseTab = isCaseTab(tab) ? tab : 'te-dhenat'

  if (detail.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (detail.isError) {
    const notFound = detail.error instanceof ApiError && (detail.error.status === 404 || detail.error.status === 400)
    return (
      <Stack spacing={2}>
        <Alert severity={notFound ? 'warning' : 'error'}>{notFound ? t.notFound : problemMessage(detail.error)}</Alert>
        <Box>
          <Button component={Link} to="/cases">
            {t.backToList}
          </Button>
        </Box>
      </Stack>
    )
  }

  const c = detail.data
  const { header, data } = c
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
      <Box>
        <Button component={Link} to="/cases" size="small">
          {t.backToList}
        </Button>
      </Box>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack spacing={1}>
          <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography variant="h3" component="h1" sx={{ fontWeight: 700, fontFamily: 'monospace', wordBreak: 'break-all' }}>
              {header.caseCode}
            </Typography>
            <Button variant="outlined" size="small" onClick={() => void copy()}>
              {sq.cases.register.success.copy}
            </Button>
            <Typography role="status" variant="body2" color="success.main">
              {copied ? sq.cases.register.success.copied : ''}
            </Typography>
          </Stack>
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
            <Chip
              label={sq.cases.status[header.status] ?? header.status}
              color={header.status === 'ACTIVE' ? 'success' : header.status === 'REGISTERED' ? 'info' : 'default'}
            />
            <Chip
              label={`${sq.cases.list.columns.priority}: ${sq.cases.priority[header.priority] ?? header.priority}`}
              color={header.priority === 'HIGH' ? 'error' : header.priority === 'MEDIUM' ? 'warning' : 'default'}
              variant={header.priority === 'NONE' ? 'outlined' : 'filled'}
            />
            {header.isSuspended && <Chip label={t.suspended} color="warning" />}
          </Stack>
          <Typography>
            <strong>{t.court}:</strong> {data.courtName}
            {' · '}
            <strong>{t.nextHearing}:</strong> {data.nextHearingAt ? formatDateTime(data.nextHearingAt) : t.none}
            {' · '}
            <strong>{t.registeredAt}:</strong> {formatDateTime(data.registeredAt)}
          </Typography>
          <LevelSwitcher levels={c.levels} currentPublicId={header.publicId} tab={current} />
        </Stack>
      </Paper>

      <Tabs value={current} variant="scrollable" allowScrollButtonsMobile aria-label={t.tabsLabel}>
        {CASE_TABS.map((x) => (
          <Tab key={x} value={x} label={t.tabs[x]} component={Link} to={`/cases/${publicId}/${x}`} />
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
    </Stack>
  )
}
