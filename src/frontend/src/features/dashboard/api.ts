import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type DashboardWidget = components['schemas']['DashboardWidget']
export type DashboardItem = components['schemas']['DashboardItem']

export const dashboardKeys = { all: ['dashboard'] as const }

/**
 * GET /api/dashboard (task 046). No polling: it reloads when the window gets the focus back and the data is older
 * than 60 s (staleTime), and when a live notification arrives (NotificationsProvider invalidates dashboardKeys.all).
 */
export const useDashboard = () =>
  useQuery({
    queryKey: dashboardKeys.all,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
    queryFn: () => api.GET('/api/dashboard').then(unwrap),
  })

/** The API's numbers may come as number or string (int64 in OpenAPI): always a number here. */
export const num = (value: number | string | null | undefined): number | null =>
  value === null || value === undefined ? null : Number(value)
