import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type Profile = components['schemas']['ProfileResponse']
export type NotificationPreference = components['schemas']['NotificationPreference']

const profileKey = ['profile'] as const

/** The own profile (task 048). */
export const useProfile = () =>
  useQuery({ queryKey: profileKey, queryFn: () => api.GET('/api/identity/profile').then(unwrap) })

/** The phone, the only editable field; empty clears it. The response is the whole profile. */
export const useUpdatePhone = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (phone: string) => api.PUT('/api/identity/profile', { body: { phone } }).then(unwrap),
    onSuccess: (profile) => qc.setQueryData(profileKey, profile),
  })
}

/** One switch; the response is the whole list, written into the profile. */
export const useSetNotification = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ type, enabled }: { type: string; enabled: boolean }) =>
      api.PUT('/api/identity/profile/notifications', { body: { [type]: enabled } }).then(unwrap),
    onSuccess: (notifications) =>
      qc.setQueryData<Profile>(profileKey, (p) => (p ? { ...p, notifications } : p)),
  })
}
