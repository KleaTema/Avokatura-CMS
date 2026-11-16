import { Stack, Tab, Tabs } from '@mui/material'
import { useSearchParams } from 'react-router'
import { PageHeader } from '../../components/ui/PageHeader'
import { sq } from '../../i18n/sq'
import { ReportBuilder } from './builder/ReportBuilder'
import { SavedReportsTab } from './builder/SavedReportsTab'

const t = sq.reports

const TABS = ['tabela1', 'tabela3', 'tabela5', 'tabela6', 'mine'] as const
type ReportTab = (typeof TABS)[number]
const isTab = (value: string | null): value is ReportTab => TABS.some((x) => x === value)
const TABLE_OF = { tabela1: 1, tabela3: 3, tabela5: 5, tabela6: 6 } as const

/**
 * "Raporte" (tasks 045, 053h): the report builder for Tabela 1, 3, 5 and 6 and "Raportet e mia", the tab in the URL
 * (?tab=). Each table keeps its applied filters and columns in the URL (?f= ?c= ?run=1) — a link that can be shared;
 * switching the tab starts the other table without them. The API decides the scope; the menu item itself is there only
 * with menu.reports (Protokoll has none).
 */
export function ReportsPage() {
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')
  const tab: ReportTab = isTab(requested) ? requested : 'tabela1'

  return (
    <Stack spacing={2}>
      <PageHeader title={t.title} subtitle={t.subtitle} />
      <Tabs
        value={tab}
        onChange={(_, value: ReportTab) => setParams(value === 'tabela1' ? {} : { tab: value }, { replace: true })}
        variant="scrollable"
        allowScrollButtonsMobile
        aria-label={t.tabsLabel}
        sx={{ borderBottom: 1, borderColor: 'divider', displayPrint: 'none' }}
      >
        {TABS.map((x) => (
          <Tab key={x} value={x} label={t.builder.tabs[x]} />
        ))}
      </Tabs>
      <Stack spacing={2} role="tabpanel" aria-label={t.builder.tabs[tab]}>
        {/* the key restarts the builder when the URL's table changes (a saved report, another tab) */}
        {tab === 'mine' ? (
          <SavedReportsTab />
        ) : (
          <ReportBuilder key={`${tab}|${params.get('saved') ?? ''}|${params.get('f') ?? ''}`} table={TABLE_OF[tab]} />
        )}
      </Stack>
    </Stack>
  )
}
