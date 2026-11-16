import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import type { PageRequest } from '../cases/api'

export type ActiveCaseRow = components['schemas']['ActiveCaseRow']
export type ClosedCaseRow = components['schemas']['ClosedCaseRow']
export type ReportColumn = components['schemas']['ReportColumn']
export type StatisticsResponse = components['schemas']['StatisticsResponse']
export type StatisticsMeasure = components['schemas']['StatisticsMeasure']
export type StatisticsCourtRow = components['schemas']['StatisticsCourtRow']
export type StatisticsCaseRow = components['schemas']['StatisticsCaseRow']

/** Filters of the active cases report (public ids and codes, as in the URL). Empty = not set. */
export interface ActiveFilters {
  officeId: string
  lawyerId: string
  courtId: string
  categoryCode: string
  courtLevelCode: string
  priority: string
  suspended: string
  q: string
}

export interface ClosedFilters {
  closedFrom: string
  closedTo: string
  resultCode: string
  outcomeGroup: string
  courtId: string
  lawyerId: string
  officeId: string
}

export interface StatisticsFilters {
  year: string
  table: string
  officeId: string
  lawyerId: string
  institutionId: string
  ministryId: string
  /** task 053h: the builder's court level and court (the statistics tab leaves them out) */
  courtLevelCode?: string
  filterCourtId?: string
}

/** Only the set filters, as query parameters (the API reads an empty value as "not set" anyway). */
export const queryOf = (filters: object): Record<string, string> =>
  Object.fromEntries(Object.entries(filters).filter(([, v]) => typeof v === 'string' && v !== '')) as Record<string, string>

export const searchOf = (filters: object, extra: Record<string, string> = {}) =>
  new URLSearchParams({ ...queryOf(filters), ...extra }).toString()

export const reportKeys = {
  all: ['reports'] as const,
  active: (f: ActiveFilters, p: PageRequest) => ['reports', 'active', f, p.cursor ?? null, p.size] as const,
  closed: (f: ClosedFilters, p: PageRequest) => ['reports', 'closed', f, p.cursor ?? null, p.size] as const,
  statistics: (f: StatisticsFilters) => ['reports', 'statistics', f] as const,
  drill: (f: StatisticsFilters, cell: DrillCell, cursor: string | undefined) => ['reports', 'drill', f, cell, cursor ?? null] as const,
}

/** One page of Tabela 1 (keyset, with the total and the columns' Albanian headers). */
export const useActiveReport = (f: ActiveFilters, p: PageRequest) =>
  useQuery({
    queryKey: reportKeys.active(f, p),
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: () =>
      api
        .GET('/api/reports/active-cases', {
          params: {
            // the API documents these as PascalCase (bound from a record; the binding itself ignores case)
            query: {
              OfficeId: f.officeId || undefined,
              LawyerId: f.lawyerId || undefined,
              CourtId: f.courtId ? Number(f.courtId) : undefined,
              CategoryCode: f.categoryCode || undefined,
              CourtLevelCode: f.courtLevelCode || undefined,
              Priority: f.priority || undefined,
              Suspended: f.suspended ? f.suspended === 'true' : undefined,
              Q: f.q || undefined,
              After: p.cursor,
              Limit: p.size,
            },
          },
        })
        .then(unwrap),
  })

/** One page of Tabela 3: one row per closed level, ordered by dossier then level. */
export const useClosedReport = (f: ClosedFilters, p: PageRequest) =>
  useQuery({
    queryKey: reportKeys.closed(f, p),
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: () =>
      api
        .GET('/api/reports/closed-cases', {
          params: {
            query: {
              ClosedFrom: f.closedFrom || undefined,
              ClosedTo: f.closedTo || undefined,
              ResultCode: f.resultCode || undefined,
              OutcomeGroup: f.outcomeGroup || undefined,
              CourtId: f.courtId ? Number(f.courtId) : undefined,
              LawyerId: f.lawyerId || undefined,
              OfficeId: f.officeId || undefined,
              After: p.cursor,
              Limit: p.size,
            },
          },
        })
        .then(unwrap),
  })

const statisticsQuery = (f: StatisticsFilters) => ({
  Year: f.year ? Number(f.year) : undefined,
  Table: f.table ? Number(f.table) : undefined,
  OfficeId: f.officeId || undefined,
  LawyerId: f.lawyerId || undefined,
  InstitutionId: f.institutionId || undefined,
  MinistryId: f.ministryId || undefined,
  // task 053h: the builder's court level and court
  CourtLevelCode: f.courtLevelCode || undefined,
  FilterCourtId: f.filterCourtId ? Number(f.filterCourtId) : undefined,
})

/** Tabela 5 / 6 of a year. Tabela 6 waits for an institution or a ministry. */
export const useStatistics = (f: StatisticsFilters, enabled: boolean) =>
  useQuery({
    queryKey: reportKeys.statistics(f),
    enabled,
    retry: false,
    placeholderData: keepPreviousData,
    queryFn: () => api.GET('/api/reports/statistics', { params: { query: statisticsQuery(f) } }).then(unwrap),
  })

/** The cell of a number: its measure and what narrows it (court, currency, result, the lawyer of a "Në dorë" row). */
export interface DrillCell {
  measure: string
  courtId?: number
  resultCode?: string
  rowLawyerId?: string
}

/** The cases behind one number (keyset, 10 a page). */
export const useDrillDown = (f: StatisticsFilters, cell: DrillCell | null, cursor: string | undefined) =>
  useQuery({
    queryKey: cell ? reportKeys.drill(f, cell, cursor) : ['reports', 'drill', 'none'],
    enabled: cell !== null,
    retry: false,
    placeholderData: keepPreviousData,
    queryFn: () =>
      api
        .GET('/api/reports/statistics/cases', {
          params: {
            query: {
              ...statisticsQuery(f),
              Measure: cell?.measure,
              CourtId: cell?.courtId,
              ResultCode: cell?.resultCode,
              RowLawyerId: cell?.rowLawyerId,
              After: cursor,
              Limit: 10,
            },
          },
        })
        .then(unwrap),
  })

/** A decimal from the API (number or string) as the canonical decimal string lib/money.ts formats. */
export const decimalText = (value: number | string | null | undefined): string | null =>
  value === null || value === undefined ? null : typeof value === 'number' ? value.toFixed(2) : value
