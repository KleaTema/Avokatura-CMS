import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'

export const useMe = (enabled: boolean) =>
  useQuery({
    queryKey: ['me'],
    enabled,
    retry: false,
    queryFn: () => api.GET('/api/identity/me').then(unwrap),
  })
