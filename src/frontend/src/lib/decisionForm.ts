import dayjs from 'dayjs'
import { z } from 'zod'
import type { CaseDecision, DecisionRequest } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { AREA_INTEGER_DIGITS, MONEY_INTEGER_DIGITS, parseAlbanianMoneyInput, toAlbanianInput } from './money'
import { isFilled, isoDateOf, isPickerValue, type PickerValue } from './pickerValues'

const t = sq.cases.page.closeDialog

export const MAX_OTHER_TEXT = 300
export const MAX_NUMBER = 50
export const MAX_DISPOSITIVE = 8000

/** The four value types of a decision (task 036): money + m² each. */
export const VALUE_TYPES = ['won', 'lost', 'preventedDamage', 'causedDamage'] as const
export type ValueType = (typeof VALUE_TYPES)[number]

type AmountField = `${ValueType}Amount`
type AreaField = `${ValueType}AreaM2`

/** The close / complete dialog's form. Money and m² as the user types them (Albanian notation), never numbers. */
export type DecisionFormValues = {
  resultCode: string
  resultOtherText: string
  decisionNumber: string
  decisionDate: PickerValue
  dispositive: string
  currency: string
  courtCostsAmount: string
  isAppealed: boolean
  isRecursed: boolean
  isFinal: boolean
  decisionReceived: boolean
  documentPublicId: string
} & Record<AmountField | AreaField, string>

export const amountField = (type: ValueType): AmountField => `${type}Amount`
export const areaField = (type: ValueType): AreaField => `${type}AreaM2`

/** The fields of each step of the stepper: a step's "Vazhdo" validates these; a server error opens its step. */
export const STEP_FIELDS: (keyof DecisionFormValues)[][] = [
  ['resultCode', 'resultOtherText', 'decisionNumber', 'decisionDate', 'dispositive'],
  ['currency', 'courtCostsAmount', ...VALUE_TYPES.flatMap((v) => [amountField(v), areaField(v)])],
  ['isAppealed', 'isRecursed', 'isFinal'],
  ['decisionReceived', 'documentPublicId'],
]

/** The API's field names that differ from the form's. */
const SERVER_FIELDS: Record<string, keyof DecisionFormValues> = { decisionDocumentPublicId: 'documentPublicId' }

/** A server field error → the form field and its step (null when the field is not one of the form's). */
export function fieldOfServer(key: string): { field: keyof DecisionFormValues; step: number } | null {
  const field = (SERVER_FIELDS[key] ?? key) as keyof DecisionFormValues
  const step = STEP_FIELDS.findIndex((fields) => fields.includes(field))
  return step < 0 ? null : { field, step }
}

/** Empty form for a new decision; the decision's values (as the inputs show them) when completing it later. */
export function decisionDefaults(decision: CaseDecision | null | undefined): DecisionFormValues {
  const values: DecisionFormValues = {
    resultCode: decision?.result.code ?? '',
    resultOtherText: decision?.resultOtherText ?? '',
    decisionNumber: decision?.decisionNumber ?? '',
    decisionDate: decision?.decisionDate ? dayjs(decision.decisionDate) : null,
    dispositive: decision?.dispositive ?? '',
    currency: decision?.currency ?? 'ALL',
    courtCostsAmount: toAlbanianInput(decision?.courtCostsAmount),
    isAppealed: decision?.isAppealed ?? false,
    isRecursed: decision?.isRecursed ?? false,
    isFinal: decision?.isFinal ?? false,
    decisionReceived: decision?.decisionReceived ?? false,
    documentPublicId: decision?.document?.publicId ?? '',
    wonAmount: toAlbanianInput(decision?.wonAmount),
    wonAreaM2: toAlbanianInput(decision?.wonAreaM2),
    lostAmount: toAlbanianInput(decision?.lostAmount),
    lostAreaM2: toAlbanianInput(decision?.lostAreaM2),
    preventedDamageAmount: toAlbanianInput(decision?.preventedDamageAmount),
    preventedDamageAreaM2: toAlbanianInput(decision?.preventedDamageAreaM2),
    causedDamageAmount: toAlbanianInput(decision?.causedDamageAmount),
    causedDamageAreaM2: toAlbanianInput(decision?.causedDamageAreaM2),
  }
  return values
}

/** "" → null; otherwise the API's decimal string ("1.250.000,5" → "1250000.50"). Valid input assumed (validated first). */
const money = (text: string, digits = MONEY_INTEGER_DIGITS) => parseAlbanianMoneyInput(text, digits) || null
const clean = (text: string) => text.trim() || null

/** The form → the API's request (POST or PUT; rowVersion of the case or of the decision). */
export function decisionRequest(v: DecisionFormValues, rowVersion: number, cancelOpenHearings = false): DecisionRequest {
  return {
    resultCode: v.resultCode,
    resultOtherText: clean(v.resultOtherText),
    decisionNumber: clean(v.decisionNumber),
    decisionDate: isoDateOf(v.decisionDate),
    dispositive: clean(v.dispositive),
    currency: v.currency,
    wonAmount: money(v.wonAmount),
    wonAreaM2: money(v.wonAreaM2, AREA_INTEGER_DIGITS),
    lostAmount: money(v.lostAmount),
    lostAreaM2: money(v.lostAreaM2, AREA_INTEGER_DIGITS),
    preventedDamageAmount: money(v.preventedDamageAmount),
    preventedDamageAreaM2: money(v.preventedDamageAreaM2, AREA_INTEGER_DIGITS),
    causedDamageAmount: money(v.causedDamageAmount),
    causedDamageAreaM2: money(v.causedDamageAreaM2, AREA_INTEGER_DIGITS),
    courtCostsAmount: money(v.courtCostsAmount),
    isAppealed: v.isAppealed,
    isRecursed: v.isRecursed,
    isFinal: v.isFinal,
    decisionReceived: v.decisionReceived,
    decisionDocumentPublicId: v.decisionReceived && v.documentPublicId ? v.documentPublicId : null,
    cancelOpenHearings,
    rowVersion,
  }
}

/**
 * The form's rules (the API checks them again): the result, "Tjetër" with its text, lengths, the date (not after
 * today, not before the act date), money and m² in Albanian notation, the document when received.
 */
export function decisionSchema(ctx: { isOther: (code: string) => boolean; actDate: string | null; today: string }) {
  const text = (max: number) => z.string().refine((s) => s.trim().length <= max, t.tooLong(max))
  const amount = z.string().refine((s) => parseAlbanianMoneyInput(s, MONEY_INTEGER_DIGITS) !== null, t.amountInvalid)
  const area = z.string().refine((s) => parseAlbanianMoneyInput(s, AREA_INTEGER_DIGITS) !== null, t.areaInvalid)
  return z
    .object({
      resultCode: z.string().min(1, t.resultRequired),
      resultOtherText: text(MAX_OTHER_TEXT),
      decisionNumber: text(MAX_NUMBER),
      decisionDate: z.custom<PickerValue>(isPickerValue),
      dispositive: text(MAX_DISPOSITIVE),
      currency: z.string().regex(/^[A-Z]{3}$/),
      courtCostsAmount: amount,
      wonAmount: amount,
      wonAreaM2: area,
      lostAmount: amount,
      lostAreaM2: area,
      preventedDamageAmount: amount,
      preventedDamageAreaM2: area,
      causedDamageAmount: amount,
      causedDamageAreaM2: area,
      isAppealed: z.boolean(),
      isRecursed: z.boolean(),
      isFinal: z.boolean(),
      decisionReceived: z.boolean(),
      documentPublicId: z.string(),
    })
    .superRefine((v, issue) => {
      const add = (path: keyof DecisionFormValues, message: string) => issue.addIssue({ code: 'custom', path: [path], message })
      if (v.resultCode && ctx.isOther(v.resultCode) && !v.resultOtherText.trim()) add('resultOtherText', t.otherRequired)
      const date = isoDateOf(v.decisionDate)
      if (v.decisionDate !== null && !isFilled(v.decisionDate)) add('decisionDate', sq.cases.register.validation.dateInvalid)
      if (date && date > ctx.today) add('decisionDate', t.dateFuture)
      if (date && ctx.actDate && date < ctx.actDate) add('decisionDate', t.dateBeforeAct)
      if (v.decisionReceived && !v.documentPublicId) add('documentPublicId', t.documentRequired)
    })
}
