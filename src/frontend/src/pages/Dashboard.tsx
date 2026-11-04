import { Alert, Box, Button, Stack } from '@mui/material'
import {
  ChartWidget,
  CountCard,
  CountCardSkeleton,
  ListWidget,
  SectionSkeleton,
} from '../components/dashboard/DashboardWidgets'
import { PageHeader } from '../components/ui/PageHeader'
import { useDashboard } from '../features/dashboard/api'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'

const t = sq.dashboard

/** KPI columns: 1 on a phone, 2 on a tablet, 4 from 1200 px. */
const KPI_COLUMNS = { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }

/**
 * The dashboard (task 046): the widgets of the user's roles from GET /api/dashboard — a row of count cards, then the
 * lists and charts. Every card and row opens the filtered list or the case. A widget that failed shows its own small
 * error with "Provo përsëri"; the rest of the page is unaffected. No "Regjistro" here: it lives on the Çështje page.
 */
export function DashboardPage() {
  const me = useMe(true)
  const dashboard = useDashboard()
  const widgets = dashboard.data?.widgets ?? []
  const counts = widgets.filter((w) => w.kind === 'count')
  const sections = widgets.filter((w) => w.kind !== 'count')
  const retry = () => void dashboard.refetch()

  return (
    <>
      <PageHeader title={t.greeting(me.data?.firstName ?? '')} subtitle={t.subtitle} showDate />
      {dashboard.isError && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={retry}>
              {t.retry}
            </Button>
          }
        >
          {problemMessage(dashboard.error)}
        </Alert>
      )}

      {dashboard.isPending ? (
        <Stack spacing={2.5}>
          <Box sx={{ display: 'grid', gridTemplateColumns: KPI_COLUMNS, gap: 2 }}>
            {Array.from({ length: 4 }, (_, i) => (
              <CountCardSkeleton key={i} />
            ))}
          </Box>
          <SectionSkeleton />
          <SectionSkeleton />
        </Stack>
      ) : (
        <Stack spacing={2.5}>
          {counts.length > 0 && (
            <Box component="section" aria-label={t.subtitle} sx={{ display: 'grid', gridTemplateColumns: KPI_COLUMNS, gap: 2 }}>
              {counts.map((w) => (
                <CountCard key={w.code} widget={w} onRetry={retry} />
              ))}
            </Box>
          )}
          {sections.map((w) =>
            w.kind === 'chart' ? (
              <ChartWidget key={w.code} widget={w} onRetry={retry} />
            ) : (
              <ListWidget key={w.code} widget={w} onRetry={retry} />
            ),
          )}
        </Stack>
      )}
    </>
  )
}
