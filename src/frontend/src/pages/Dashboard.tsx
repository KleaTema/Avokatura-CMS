import { LayoutDashboard } from 'lucide-react'
import { EmptyState } from '../components/ui/EmptyState'
import { PageHeader } from '../components/ui/PageHeader'
import { SectionCard } from '../components/ui/SectionCard'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'

/**
 * The dashboard. For now only the greeting and the date; the dashboard task adds the KPI cards
 * (components/ui/KpiCard) and charts from real data — no invented numbers here. "Regjistro çështje të re" lives on
 * the Çështje page only.
 */
export function DashboardPage() {
  const me = useMe(true)
  return (
    <>
      <PageHeader title={sq.dashboard.greeting(me.data?.firstName ?? '')} subtitle={sq.dashboard.subtitle} showDate />
      <SectionCard>
        <EmptyState icon={LayoutDashboard} title={sq.placeholder.comingSoon} />
      </SectionCard>
    </>
  )
}
