import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type OfficeAssistant = components['schemas']['OfficeAssistantView']
export type AssistantLink = components['schemas']['AssistantLinkView']

const key = ['assistant-links'] as const

/** Task 053e: the office's assistants with their lawyers, and the lawyers to pick (Kryetar: assistant.manage). */
export const useAssistantLinks = () =>
  useQuery({ queryKey: key, queryFn: () => api.GET('/api/assistant-links').then(unwrap) })

export const useLinkAssistant = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { assistantUserPublicId: string; lawyerUserPublicId: string }) =>
      api.POST('/api/assistant-links', { body }).then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: key }),
  })
}

export const useUnlinkAssistant = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (linkPublicId: string) => {
      const { error, response } = await api.DELETE('/api/assistant-links/{linkPublicId}', {
        params: { path: { linkPublicId } },
      })
      if (!response.ok) unwrap({ data: undefined, error, response })
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: key }),
  })
}
