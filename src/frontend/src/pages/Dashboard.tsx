import { Button } from '@mui/material'
import { LayoutDashboard } from 'lucide-react'
import { Link } from 'react-router'
import { EmptyState } from '../components/ui/EmptyState'
import { PageHeader } from '../components/ui/PageHeader'
import { SectionCard } from '../components/ui/SectionCard'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'

/**
 * The dashboard. For now only the greeting, the date and the main action; the dashboard task adds the KPI cards
 * (components/ui/KpiCard) and charts from real data — no invented numbers here.
 */
export function DashboardPage() {
  const me = useMe(true)
  const canCreate = me.data?.permissions.includes('case.create') ?? false
  return (
    <>
      <PageHeader
        title={sq.dashboard.greeting(me.data?.firstName ?? '')}
        subtitle={sq.dashboard.subtitle}
        showDate
        actions={
          canCreate && (
            <Button variant="contained" color="secondary" component={Link} to="/cases/register">
              {sq.cases.registerNew}
            </Button>
          )
        }
      />
      <SectionCard>
        <EmptyState icon={LayoutDashboard} title={sq.placeholder.comingSoon} />
      </SectionCard>
    </>
  )
}
