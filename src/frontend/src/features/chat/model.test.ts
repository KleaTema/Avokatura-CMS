import { describe, expect, it } from 'vitest'
import { capOldest, confirmSent, dayLabel, linkify, prependMessages, threadItems, upsertMessage, type ThreadMessage } from './model'

const me = { id: 'me', name: 'Unë' }
const other = { id: 'other', name: 'Tjetri' }
const msg = (id: string, createdAt: string, sender = other, body = id): ThreadMessage => ({
  id,
  type: 'TEXT',
  sender,
  body,
  isDeleted: false,
  createdAt,
})

// 10:00 in Tirane on 15 Nov 2026 (UTC+1)
const now = new Date('2026-11-15T09:00:00Z')

describe('day separators (Europe/Tirane)', () => {
  it('says Sot, Dje, or the date', () => {
    expect(dayLabel('2026-11-15T07:00:00Z', now)).toBe('Sot')
    expect(dayLabel('2026-11-14T22:30:00Z', now)).toBe('Dje')
    expect(dayLabel('2026-11-12T10:00:00Z', now)).toBe('12.11.2026')
  })

  it('uses the Tirane day, not the UTC day: 23:30Z on the 14th is already the 15th in Tirane', () => {
    expect(dayLabel('2026-11-14T23:30:00Z', now)).toBe('Sot')
  })

  it('puts a separator before each day and groups one sender within 5 minutes', () => {
    const items = threadItems(
      [
        msg('a', '2026-11-14T10:00:00Z'),
        msg('b', '2026-11-15T08:00:00Z'),
        msg('c', '2026-11-15T08:02:00Z'),
        msg('d', '2026-11-15T08:03:00Z', me),
        msg('e', '2026-11-15T08:20:00Z', me),
      ],
      now,
    )
    expect(items.map((i) => (i.kind === 'day' ? i.label : `${i.message.id}${i.firstOfGroup ? '*' : ''}`))).toEqual([
      'Dje',
      'a*',
      'Sot',
      'b*',
      'c',
      'd*',
      'e*',
    ])
  })
})

describe('linkify (plain text only)', () => {
  it('links http and https, without the sentence full stop', () => {
    expect(linkify('Shih https://akshi.gov.al/x?y=1. Faleminderit')).toEqual([
      { text: 'Shih ' },
      { text: 'https://akshi.gov.al/x?y=1', href: 'https://akshi.gov.al/x?y=1' },
      { text: '. Faleminderit' },
    ])
  })

  it('never links javascript:, data: or bare www', () => {
    for (const text of ['javascript:alert(1)', 'data:text/html,<b>x</b>', 'www.example.com']) {
      expect(linkify(text).every((p) => p.href === undefined)).toBe(true)
    }
  })

  it('keeps HTML as text', () => {
    expect(linkify('<img src=x onerror=alert(1)>')).toEqual([{ text: '<img src=x onerror=alert(1)>' }])
  })
})

describe('optimistic messages', () => {
  const pending: ThreadMessage = { ...msg('temp-1', '2026-11-15T08:00:00Z', me, 'Tung'), tempId: 'temp-1', status: 'sending' }
  const server = msg('srv-1', '2026-11-15T08:00:01Z', me, 'Tung')

  it('the response replaces the optimistic copy', () => {
    expect(confirmSent([pending], 'temp-1', server)).toEqual([server])
  })

  it('the hub echo arriving first replaces it, and the late response does not add a second one', () => {
    const afterEcho = upsertMessage([pending], server, 'me')
    expect(afterEcho.map((m) => m.id)).toEqual(['srv-1'])
    expect(confirmSent(afterEcho, 'temp-1', server).map((m) => m.id)).toEqual(['srv-1'])
  })

  it("another person's identical text is a separate message", () => {
    const theirs = msg('srv-2', '2026-11-15T08:00:02Z', other, 'Tung')
    expect(upsertMessage([pending], theirs, 'me').map((m) => m.id)).toEqual(['temp-1', 'srv-2'])
  })

  it('an edit replaces in place; older pages are prepended without duplicates; the view keeps the newest', () => {
    const edited = { ...server, body: 'Tung!', editedAt: '2026-11-15T08:01:00Z' }
    expect(upsertMessage([server], edited)).toEqual([edited])
    const older = msg('old', '2026-11-15T07:00:00Z')
    expect(prependMessages([server], [older, server]).map((m) => m.id)).toEqual(['old', 'srv-1'])
    const many = Array.from({ length: 5 }, (_, i) => msg(`m${i}`, `2026-11-15T08:0${i}:00Z`))
    expect(capOldest(many, 3).map((m) => m.id)).toEqual(['m2', 'm3', 'm4'])
  })
})
