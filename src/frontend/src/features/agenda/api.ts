import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { agendaKeys } from '../cases/api'

export type AgendaItem = components['schemas']['AgendaItem']

export interface AgendaParams {
  /** Tirane calendar dates, both included */
  from: string
  to: string
  lawyerId: string
  officeId: string
  includeCancelled: boolean
}

/**
 * GET /api/agenda for the visible range. Under agendaKeys.all, so every hearing change (afterCaseChange) reloads
 * it; the previous range stays on screen while the next one loads.
 */
export const useAgenda = (p: AgendaParams) =>
  useQuery({
    queryKey: [...agendaKeys.all, p] as const,
    placeholderData: keepPreviousData,
    queryFn: () =>
      api
        .GET('/api/agenda', {
          params: {
            query: {
              from: p.from,
              to: p.to,
              lawyerId: p.lawyerId || undefined,
              officeId: p.officeId || undefined,
              includeCancelled: p.includeCancelled || undefined,
            },
          },
        })
        .then(unwrap),
  })
