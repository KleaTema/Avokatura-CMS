import { describe, expect, it } from 'vitest'
import { isAppLink, listShapeOf, toneOf } from './dashboard'

describe('toneOf (dashboard cards, task 046)', () => {
  it('colours by meaning: neutral blue, attention orange, overdue red, done green', () => {
    expect(toneOf('mineOpen', 12)).toBe('blue')
    expect(toneOf('unassigned', 3)).toBe('orange')
    expect(toneOf('officeHearingOverdue', 2)).toBe('red')
    expect(toneOf('registeredToday', 4)).toBe('green')
  })

  it('turns attention and overdue cards green when there is nothing to do', () => {
    expect(toneOf('unassigned', 0)).toBe('green')
    expect(toneOf('mineHearingOverdue', 0)).toBe('green')
    expect(toneOf('mineOpen', 0)).toBe('blue')
  })
})

describe('listShapeOf', () => {
  it('picks the columns of each list widget', () => {
    expect(listShapeOf('officeHearingsThisWeek')).toBe('hearings')
    expect(listShapeOf('institutionUpcoming')).toBe('hearings')
    expect(listShapeOf('myRegistrations')).toBe('registrations')
    expect(listShapeOf('mineActivity')).toBe('activity')
    expect(listShapeOf('adminJobs')).toBe('jobs')
    expect(listShapeOf('adminPartitions')).toBe('partitions')
  })
})

describe('isAppLink', () => {
  it('accepts app routes only', () => {
    expect(isAppLink('/cases?tab=mine')).toBe(true)
    expect(isAppLink('//evil.example')).toBe(false)
    expect(isAppLink('https://evil.example')).toBe(false)
    expect(isAppLink(null)).toBe(false)
  })
})
