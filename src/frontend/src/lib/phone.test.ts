import { describe, expect, it } from 'vitest'
import { formatPhone } from './phone'

describe('formatPhone (profile, task 048)', () => {
  it('groups an Albanian mobile', () => {
    expect(formatPhone('+355691234567')).toBe('+355 69 123 4567')
  })

  it('groups an Albanian landline', () => {
    expect(formatPhone('+35542223333')).toBe('+355 4 222 3333')
  })

  it('leaves a foreign number as stored', () => {
    expect(formatPhone('+393331234567')).toBe('+393331234567')
  })
})
