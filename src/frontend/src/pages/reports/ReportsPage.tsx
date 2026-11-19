import { Stack, Tab, Tabs } from '@mui/material'
import { useSearchParams } from 'react-router'
import { PageHeader } from '../../components/ui/PageHeader'
import { sq } from '../../i18n/sq'
import { ReportBuilder } from './builder/ReportBuilder'
import { SavedReportsTab } from './builder/SavedReportsTab'
import { isReportTab, MAIN_TABS, mainOf, REPORT_TABS, TABLE_OF, tabFromParam, type MainTab, type ReportTab } from './reportTabs'

const t = sq.reports

/**
 * "Raporte & Statistika" (tasks 045, 053h, 053o): two main tabs — "Raportet" (Tabela 1 — Aktive, Tabela 2 — Të mbyllura,
 * "Raportet e mia") and "Statistikat" (Tabela 3 — Çështje aktive, Tabela 4 — Institucionale) — the sub-tab in the URL
 * (?tab=). Each table keeps its filters and columns in the URL (?f= ?c=) — a link that can be shared; switching the tab
 * starts the other table without them. The API decides the scope; the menu item itself is there only with menu.reports.
 */
export function ReportsPage() {
  const [params, setParams] = useSearchParams()
  const tab: ReportTab = tabFromParam(params.get('tab'))
  const main: MainTab = mainOf(tab)
  const go = (next: ReportTab) => setParams(next === 'aktive' ? {} : { tab: next }, { replace: true })

  return (
    <Stack spacing={2}>
      <PageHeader title={t.title} subtitle={t.subtitle} />
      <Tabs
        value={main}
        onChange={(_, value: MainTab) => go(REPORT_TABS.find((x) => mainOf(x) === value) ?? 'aktive')}
        aria-label={t.builder.mainTabsLabel}
        sx={{ borderBottom: 1, borderColor: 'divider', displayPrint: 'none' }}
      >
        {MAIN_TABS.map((x) => (
          <Tab key={x} value={x} label={t.builder.mainTabs[x]} />
        ))}
      </Tabs>
      <Tabs
        value={tab}
        onChange={(_, value: string) => isReportTab(value) && go(value)}
        variant="scrollable"
        allowScrollButtonsMobile
        aria-label={t.builder.subTabsLabel}
        sx={{ displayPrint: 'none', minHeight: 40, '& .MuiTab-root': { minHeight: 40, textTransform: 'none' } }}
      >
        {REPORT_TABS.filter((x) => mainOf(x) === main).map((x) => (
          <Tab key={x} value={x} label={t.builder.tabs[x]} />
        ))}
      </Tabs>
      <Stack spacing={2} role="tabpanel" aria-label={t.builder.tabs[tab]}>
        {/* the key restarts the builder when the tab or a saved report changes (the URL's filters then come from it) */}
        {tab === 'mine' ? <SavedReportsTab /> : <ReportBuilder key={`${tab}|${params.get('saved') ?? ''}`} table={TABLE_OF[tab]} />}
      </Stack>
    </Stack>
  )
}
