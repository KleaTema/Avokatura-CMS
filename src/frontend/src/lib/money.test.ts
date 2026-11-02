import { describe, expect, it } from 'vitest'
import {
  AREA_INTEGER_DIGITS,
  formatDecimal,
  formatMoney,
  formatWhileTyping,
  parseAlbanianMoneyInput,
  toAlbanianInput,
} from './money'

describe('parseAlbanianMoneyInput', () => {
  it.each([
    ['1.250.000,50', '1250000.50'],
    ['1.250.000,5', '1250000.50'],
    ['1250000', '1250000.00'],
    ['1 250 000,50', '1250000.50'],
    ['0', '0.00'],
    ['0,05', '0.05'],
    ['007', '7.00'],
    ['12,', '12.00'],
  ])('%s → %s', (text, expected) => {
    expect(parseAlbanianMoneyInput(text)).toBe(expected)
  })

  it('keeps 18-digit values exactly (no JS number on the way)', () => {
    expect(parseAlbanianMoneyInput('9.999.999.999.999.999,99')).toBe('9999999999999999.99')
    expect(parseAlbanianMoneyInput('1234567890123456,78')).toBe('1234567890123456.78')
  })

  it('refuses more than 16 digits before the comma (DECIMAL(18,2))', () => {
    expect(parseAlbanianMoneyInput('10.000.000.000.000.000')).toBeNull()
  })

  it.each(['12,345', '-1', '1,2,3', 'abc', '1e5', '+5'])('refuses %s', (text) => {
    expect(parseAlbanianMoneyInput(text)).toBeNull()
  })

  it('is empty for an empty field', () => {
    expect(parseAlbanianMoneyInput('  ')).toBe('')
  })

  it('limits m² to 12 digits before the comma (DECIMAL(14,2))', () => {
    expect(parseAlbanianMoneyInput('999.999.999.999,99', AREA_INTEGER_DIGITS)).toBe('999999999999.99')
    expect(parseAlbanianMoneyInput('1.000.000.000.000', AREA_INTEGER_DIGITS)).toBeNull()
  })
})

describe('formatting', () => {
  it('formats the API string in Albanian notation', () => {
    expect(formatDecimal('1250000.50')).toBe('1.250.000,50')
    expect(formatDecimal('1250000.5')).toBe('1.250.000,50')
    expect(formatDecimal('999')).toBe('999,00')
    expect(formatDecimal('9999999999999999.99')).toBe('9.999.999.999.999.999,99')
  })

  it('adds the currency, ALL by default', () => {
    expect(formatMoney('2500000.00', 'EUR')).toBe('2.500.000,00 EUR')
    expect(formatMoney('2500000.00', null)).toBe('2.500.000,00 ALL')
  })

  it('round-trips the API value through the input', () => {
    expect(parseAlbanianMoneyInput(toAlbanianInput('9999999999999999.99'))).toBe('9999999999999999.99')
    expect(toAlbanianInput(null)).toBe('')
  })
})

describe('formatWhileTyping', () => {
  it('regroups the thousands and keeps one comma with 2 decimals', () => {
    expect(formatWhileTyping('1250000', 7).text).toBe('1.250.000')
    expect(formatWhileTyping('1.250.000,567', 13).text).toBe('1.250.000,56')
    expect(formatWhileTyping('12,3,4', 6).text).toBe('12,34')
    expect(formatWhileTyping('12a3', 4).text).toBe('123')
  })

  it('keeps the caret after the same digit', () => {
    // "1.250.00|0" — typing a 9 before the last 0: "1.250.0090" with the caret after the 9
    const result = formatWhileTyping('1.250.0090', 9)
    expect(result.text).toBe('12.500.090')
    expect(result.text.slice(0, result.caret).replace(/\D/g, '')).toBe('1250009')
  })
})
