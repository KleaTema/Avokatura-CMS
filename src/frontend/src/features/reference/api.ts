import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'

/** Public reference lists (pickers). Keys follow the API path: ['reference', 'hearing-types'], ['reference', 'courts'], … */
export const referenceKeys = {
  all: ['reference'] as const,
  list: (name: string) => ['reference', name] as const,
}

export const useCourtLevels = () =>
  useQuery({
    queryKey: referenceKeys.list('court-levels'),
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/reference/court-levels').then(unwrap),
  })
