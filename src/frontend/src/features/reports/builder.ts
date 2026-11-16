import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type ReportFilterMeta = components['schemas']['ReportFilterMeta']
export type ReportFiltersResponse = components['schemas']['ReportFiltersResponse']
export type ReportColumnMeta = components['schemas']['ReportColumnMeta']
export type ReportTotals = components['schemas']['ReportTotals']
export type SavedReportView = components['schemas']['SavedReportView']

/** The builder's four tables (task 053h), by the client's numbers. */
export const BUILDER_TABLES = [1, 3, 5, 6] as const
export type BuilderTable = (typeof BUILDER_TABLES)[number]
export const isBuilderTable = (value: number): value is BuilderTable => BUILDER_TABLES.some((t) => t === value)
export const isStatisticsTable = (table: BuilderTable) => table === 5 || table === 6

/** The value of one filter: a choice / text, yes-no, or a range (dates yyyy-MM-dd, numbers as typed). */
export type RangeValue = { from?: string; to?: string; min?: string; max?: string }
export type FilterValue = string | boolean | RangeValue
export type FilterValues = Record<string, FilterValue>

/** The filter kinds of the API (ReportFilterKinds). */
export const FilterKind = {
  Select: 'select',
  Text: 'text',
  DateRange: 'dateRange',
  NumberRange: 'numberRange',
  YesNo: 'yesNo',
} as const

/** Only the filters that hold a value (an empty text, an empty range or "—" is not a filter). */
export function cleanValues(values: FilterValues): FilterValues {
  const out: FilterValues = {}
  for (const [key, value] of Object.entries(values)) {
    if (typeof value === 'string') {
      if (value.trim() !== '') out[key] = value.trim()
    } else if (typeof value === 'boolean') {
      out[key] = value
    } else {
      const range = Object.fromEntries(Object.entries(value).filter(([, v]) => typeof v === 'string' && v.trim() !== ''))
      if (Object.keys(range).length > 0) out[key] = range
    }
  }
  return out
}

/** The filter values as the API's ?filters= (a JSON object; always sorted so equal filters share a cache entry). */
export const filtersParam = (values: FilterValues) =>
  JSON.stringify(Object.fromEntries(Object.entries(cleanValues(values)).sort(([a], [b]) => a.localeCompare(b))))

/** A saved / shared JSON object read back as filter values (anything else is dropped). */
export function parseValues(input: unknown): FilterValues {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) return {}
  const out: FilterValues = {}
  for (const [key, value] of Object.entries(input)) {
    if (typeof value === 'string' || typeof value === 'boolean') out[key] = value
    else if (typeof value === 'number') out[key] = String(value)
    else if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      const range: RangeValue = {}
      for (const part of ['from', 'to', 'min', 'max'] as const) {
        const v: unknown = (value as Record<string, unknown>)[part]
        if (typeof v === 'string' || typeof v === 'number') range[part] = String(v)
      }
      out[key] = range
    }
  }
  return out
}

/** The filter keys under `key` (its children, their children…): cleared when `key` changes. */
export function descendants(filters: ReportFilterMeta[], key: string): string[] {
  const children = filters.filter((f) => f.parent === key).map((f) => f.key)
  return children.flatMap((child) => [child, ...descendants(filters, child)])
}

export const builderKeys = {
  all: ['reports', 'builder'] as const,
  filters: (table: BuilderTable, filters: string) => ['reports', 'builder', 'filters', table, filters] as const,
  rows: (table: BuilderTable, filters: string, cursor: string | undefined, size: number) =>
    ['reports', 'builder', 'rows', table, filters, cursor ?? null, size] as const,
  statistics: (table: BuilderTable, filters: string) => ['reports', 'builder', 'statistics', table, filters] as const,
  saved: ['reports', 'saved'] as const,
}

/** The filter metadata of a table: the filters, their options under the current choices, the columns. */
export const useBuilderFilters = (table: BuilderTable, values: FilterValues) => {
  const filters = filtersParam(values)
  return useQuery({
    queryKey: builderKeys.filters(table, filters),
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: () =>
      api.GET('/api/reports/builder/{table}/filters', { params: { path: { table }, query: { filters } } }).then(unwrap),
  })
}

/** Tabela 1 / 3: one page (keyset) and, on the first page, the totals row. Runs only after "Gjenero" (`filters` set). */
export const useBuilderRows = (table: 1 | 3, filters: string | null, cursor: string | undefined, size: number) =>
  useQuery({
    queryKey: builderKeys.rows(table, filters ?? '', cursor, size),
    enabled: filters !== null,
    placeholderData: keepPreviousData,
    retry: false,
    queryFn: async () => {
      const query = { Filters: filters ?? '{}', After: cursor, Limit: size }
      return table === 1
        ? api.GET('/api/reports/builder/active', { params: { query } }).then(unwrap)
        : api.GET('/api/reports/builder/closed', { params: { query } }).then(unwrap)
    },
  })

/** Tabela 5 / 6: the statistics of the filters. Runs only after "Gjenero". */
export const useBuilderStatistics = (table: 5 | 6, filters: string | null) =>
  useQuery({
    queryKey: builderKeys.statistics(table, filters ?? ''),
    enabled: filters !== null,
    retry: false,
    queryFn: () =>
      api
        .GET('/api/reports/builder/statistics/{table}', { params: { path: { table }, query: { filters: filters ?? '{}' } } })
        .then(unwrap),
  })

/** The export URL of the builder (the download goes through lib/download with the auth header). */
export const exportUrl = (table: BuilderTable, format: 'xlsx' | 'csv' | 'pdf', filters: string, columns: string[]) => {
  const query = new URLSearchParams({ format, filters })
  if (columns.length > 0) query.set('columns', columns.join(','))
  return `/api/reports/builder/${table}/export?${query.toString()}`
}

// ---------------------------------------------------------------------------------------------- "Raportet e mia"

export const useSavedReports = (enabled = true) =>
  useQuery({
    queryKey: builderKeys.saved,
    enabled,
    queryFn: () => api.GET('/api/reports/saved').then(unwrap),
  })

export interface SaveReportInput {
  id?: string
  name: string
  table: BuilderTable
  filters: FilterValues
  columns: string[]
}

export const useSaveReport = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: SaveReportInput) => {
      const body = { name: input.name, table: input.table, filters: cleanValues(input.filters), columns: input.columns }
      return input.id
        ? api.PUT('/api/reports/saved/{id}', { params: { path: { id: input.id } }, body }).then(unwrap)
        : api.POST('/api/reports/saved', { body }).then(unwrap)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: builderKeys.saved }),
  })
}

export const useDeleteSavedReport = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.DELETE('/api/reports/saved/{id}', { params: { path: { id } } }).then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: builderKeys.saved }),
  })
}
