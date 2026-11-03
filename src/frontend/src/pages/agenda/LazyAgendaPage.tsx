import { CircularProgress } from '@mui/material'
import { lazy, Suspense } from 'react'
import { sq } from '../../i18n/sq'

// The agenda brings FullCalendar (~290 kB): loaded only when the page is opened.
const AgendaPage = lazy(() => import('./AgendaPage').then((m) => ({ default: m.AgendaPage })))

export function LazyAgendaPage() {
  return (
    <Suspense fallback={<CircularProgress aria-label={sq.errors.loading} />}>
      <AgendaPage />
    </Suspense>
  )
}
