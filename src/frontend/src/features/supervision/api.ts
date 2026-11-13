import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type Supervision = components['schemas']['SupervisionItem']
export type CreateSupervision = components['schemas']['CreateSupervisionRequest']

const keys = {
  all: ['supervisions'] as const,
  list: (includeEnded: boolean) => ['supervisions', 'list', includeEnded] as const,
  people: ['supervisions', 'people'] as const,
}

/** Task 053d: the supervisions (active first; the ended ones too when asked). Monitor: supervision.manage. */
export const useSupervisions = (includeEnded: boolean) =>
  useQuery({
    queryKey: keys.list(includeEnded),
    queryFn: () => api.GET('/api/supervisions', { params: { query: { includeEnded } } }).then(unwrap),
  })

/** The active Kryetar and lawyers to pick from. */
export const useSupervisionPeople = () =>
  useQuery({
    queryKey: keys.people,
    staleTime: 60_000,
    queryFn: () => api.GET('/api/supervisions/people').then(unwrap),
  })

export const useCreateSupervision = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateSupervision) => api.POST('/api/supervisions', { body }).then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.all }),
  })
}

export const useEndSupervision = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) =>
      api.POST('/api/supervisions/{publicId}/end', { params: { path: { publicId } } }).then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.all }),
  })
}
