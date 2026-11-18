import type { Tone } from '../theme/tokens'

/**
 * The tint of a count card by what its number means (task 046): blue = neutral, orange = needs attention, red =
 * overdue, green = done. A card that asks for attention turns green at 0 (nothing to do).
 */
export function toneOf(code: string, value: number | null): Tone {
  const attention = ATTENTION.has(code)
  const overdue = OVERDUE.has(code)
  if ((attention || overdue) && value === 0) return 'green'
  if (overdue) return 'red'
  if (attention) return 'orange'
  if (code === 'registeredToday') return 'green'
  return 'blue'
}

const OVERDUE = new Set(['officeHearingOverdue', 'mineHearingOverdue', 'adminFailedScans'])

const ATTENTION = new Set([
  'unassigned',
  'registeredWaiting',
  'officeMissingData30',
  'officeDecisionPending',
  'mineMissingData30',
  'mineDecisionPending',
  'institutionNoJurist',
  'allMissingData6m',
  'allDecisionPending',
  // task 053n: the administration's loose ends
  'adminUsersNoRole',
  'adminLoginBlocked',
  'adminLoginNone',
  'adminOfficesNoHead',
  'adminCourtsUncovered',
])

/** Which columns a list widget shows. */
export type ListShape = 'hearings' | 'registrations' | 'activity' | 'changes' | 'imports'

export function listShapeOf(code: string): ListShape {
  switch (code) {
    case 'myRegistrations':
      return 'registrations'
    case 'mineActivity':
      return 'activity'
    case 'adminRecentChanges':
      return 'changes'
    case 'adminImportErrors':
      return 'imports'
    default:
      return 'hearings'
  }
}

/** Only an app route may be a link target (the API sends /cases… paths). */
export const isAppLink = (path: string | null | undefined): path is string => typeof path === 'string' && /^\/(?![/\\])/.test(path)
