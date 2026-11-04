import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import { decisionDefaults, decisionRequest, decisionSchema, fieldOfServer, type DecisionFormValues } from './decisionForm'

const ctx = { isOther: (code: string) => code === 'TJETER', actDate: '2026-03-10', today: '2026-11-04' }
const base = (): DecisionFormValues => ({ ...decisionDefaults(null), resultCode: 'HUMBUR' })
const errorsOf = (values: DecisionFormValues) => {
  const result = decisionSchema(ctx).safeParse(values)
  return result.success ? [] : result.error.issues.map((i) => String(i.path[0]))
}

describe('decision form (task 039)', () => {
  it('sends money and m² as exact decimal strings and empty fields as null', () => {
    const request = decisionRequest(
      { ...base(), wonAmount: '9.999.999.999.999.999,99', wonAreaM2: '120,5', courtCostsAmount: '0', currency: 'EUR' },
      7,
    )
    expect(request.wonAmount).toBe('9999999999999999.99')
    expect(request.wonAreaM2).toBe('120.50')
    expect(request.courtCostsAmount).toBe('0.00')
    expect(request.lostAmount).toBeNull()
    expect(request.currency).toBe('EUR')
    expect(request.rowVersion).toBe(7)
    expect(request.cancelOpenHearings).toBe(false)
  })

  it('links the document only when the reasoned decision is marked received', () => {
    expect(decisionRequest({ ...base(), documentPublicId: 'd1' }, 0).decisionDocumentPublicId).toBeNull()
    expect(decisionRequest({ ...base(), decisionReceived: true, documentPublicId: 'd1' }, 0).decisionDocumentPublicId).toBe('d1')
    expect(decisionRequest({ ...base(), decisionDate: dayjs('2026-05-02') }, 0, true)).toMatchObject({
      decisionDate: '2026-05-02',
      cancelOpenHearings: true,
    })
  })

  it('checks the result, "Tjetër", the date, the amounts and the document', () => {
    expect(errorsOf(base())).toEqual([])
    expect(errorsOf({ ...base(), resultCode: '' })).toContain('resultCode')
    expect(errorsOf({ ...base(), resultCode: 'TJETER' })).toContain('resultOtherText')
    expect(errorsOf({ ...base(), decisionDate: dayjs('2026-11-05') })).toContain('decisionDate')
    expect(errorsOf({ ...base(), decisionDate: dayjs('2026-03-09') })).toContain('decisionDate')
    expect(errorsOf({ ...base(), wonAmount: '-1' })).toContain('wonAmount')
    expect(errorsOf({ ...base(), lostAreaM2: '1.234.567.890.123' })).toContain('lostAreaM2')
    expect(errorsOf({ ...base(), decisionReceived: true })).toContain('documentPublicId')
  })

  it('fills the form from a recorded decision', () => {
    const values = decisionDefaults({
      result: { code: 'TJETER', name: 'Tjetër' },
      resultOtherText: 'Pajtim',
      decisionNumber: 'V-1',
      decisionDate: '2026-05-02',
      decisionReceived: true,
      isAppealed: true,
      isRecursed: false,
      isFinal: false,
      dispositive: null,
      currency: 'EUR',
      wonAmount: '1250000.50',
      wonAreaM2: null,
      lostAmount: null,
      lostAreaM2: null,
      preventedDamageAmount: null,
      preventedDamageAreaM2: null,
      causedDamageAmount: null,
      causedDamageAreaM2: null,
      courtCostsAmount: null,
      decisionReceivedAt: '2026-05-03T10:00:00Z',
      document: { publicId: 'doc-1', title: 'Vendimi' },
      sendsBackForRetrial: false,
      recordedAt: '2026-05-02T10:00:00Z',
      rowVersion: 3,
    })
    expect(values).toMatchObject({ resultCode: 'TJETER', wonAmount: '1.250.000,50', documentPublicId: 'doc-1', currency: 'EUR' })
    expect(values.decisionDate?.format('YYYY-MM-DD')).toBe('2026-05-02')
  })

  it('maps a server field to the step that shows it', () => {
    expect(fieldOfServer('resultOtherText')).toEqual({ field: 'resultOtherText', step: 0 })
    expect(fieldOfServer('wonAmount')).toEqual({ field: 'wonAmount', step: 1 })
    expect(fieldOfServer('isFinal')).toEqual({ field: 'isFinal', step: 2 })
    expect(fieldOfServer('decisionDocumentPublicId')).toEqual({ field: 'documentPublicId', step: 3 })
    expect(fieldOfServer('rowVersion')).toBeNull()
  })
})
