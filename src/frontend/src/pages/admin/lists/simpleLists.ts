import type { SimpleListKey } from '../../../features/admin/api'
import { sq } from '../../../i18n/sq'

export interface SimpleListConfig {
  sortOrder: boolean
  isOther: boolean
  /** Create-only group column: party roles have a report group, decision results an outcome group. */
  group: 'report' | 'outcome' | null
  codeMax: number
  nameMax: number
}

/** Mirrors the backend columns and limits of each list. */
export const simpleListConfig: Record<SimpleListKey, SimpleListConfig> = {
  'hearing-types': { sortOrder: true, isOther: true, group: null, codeMax: 30, nameMax: 100 },
  'party-roles': { sortOrder: true, isOther: false, group: 'report', codeMax: 30, nameMax: 100 },
  'summons-types': { sortOrder: false, isOther: false, group: null, codeMax: 30, nameMax: 100 },
  'document-types': { sortOrder: true, isOther: true, group: null, codeMax: 30, nameMax: 100 },
  'decision-results': { sortOrder: true, isOther: true, group: 'outcome', codeMax: 40, nameMax: 150 },
}

export const groupLabels = (group: 'report' | 'outcome') =>
  group === 'report' ? sq.adminLists.reportGroups : sq.adminLists.outcomeGroups
