import { Stack, Tab, Tabs } from '@mui/material'
import { useSearchParams } from 'react-router'
import { PageHeader } from '../../components/ui/PageHeader'
import { sq } from '../../i18n/sq'
import { ActiveReport, ClosedReport } from './CaseReports'
import { StatisticsReport } from './StatisticsReport'

const t = sq.reports

const TABS = ['aktive', 'mbyllura', 'statistika'] as const
type ReportTab = (typeof TABS)[number]
const isTab = (value: string | null): value is ReportTab => TABS.some((x) => x === value)

/**
 * "Raporte" (task 045): the active cases report (the default tab), the closed cases report and the statistics, the tab
 * in the URL (?tab=). Each tab keeps its own filters in the URL; switching tab starts it without the other's filters.
 * The API decides the scope; the menu item itself is there only with menu.reports (Protokoll has none).
 */
export function ReportsPage() {
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')
  const tab: ReportTab = isTab(requested) ? requested : 'aktive'

  return (
    <Stack spacing={2}>
      <PageHeader title={t.title} subtitle={t.subtitle} />
      <Tabs
        value={tab}
        onChange={(_, value: ReportTab) => setParams(value === 'aktive' ? {} : { tab: value }, { replace: true })}
        variant="scrollable"
        allowScrollButtonsMobile
        aria-label={t.tabsLabel}
        sx={{ borderBottom: 1, borderColor: 'divider', displayPrint: 'none' }}
      >
        {TABS.map((x) => (
          <Tab key={x} value={x} label={t.tabs[x]} />
        ))}
      </Tabs>
      <Stack spacing={2} role="tabpanel" aria-label={t.tabs[tab]}>
        {tab === 'aktive' && <ActiveReport />}
        {tab === 'mbyllura' && <ClosedReport />}
        {tab === 'statistika' && <StatisticsReport />}
      </Stack>
    </Stack>
  )
}
