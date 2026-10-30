import { Construction } from 'lucide-react'
import { EmptyState } from '../components/ui/EmptyState'
import { PageHeader } from '../components/ui/PageHeader'
import { SectionCard } from '../components/ui/SectionCard'
import { sq } from '../i18n/sq'

export function PlaceholderPage({ menu }: { menu: string }) {
  return (
    <>
      <PageHeader title={sq.menu[menu] ?? menu} />
      <SectionCard>
        <EmptyState icon={Construction} title={sq.placeholder.comingSoon} />
      </SectionCard>
    </>
  )
}
