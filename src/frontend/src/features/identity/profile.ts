import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type Profile = components['schemas']['ProfileResponse']

const profileKey = ['profile'] as const

/** The own profile (tasks 048 / 053g), read-only: nothing on it can be changed by the user. */
export const useProfile = () =>
  useQuery({ queryKey: profileKey, queryFn: () => api.GET('/api/identity/profile').then(unwrap) })
