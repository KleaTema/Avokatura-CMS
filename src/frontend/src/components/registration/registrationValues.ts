import type { Path } from 'react-hook-form'
import { z } from 'zod'
import type { RegisterCaseRequest } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { todayInTirane } from '../../lib/dates'
import {
  isFilled,
  isoDateOf,
  isPickerValue,
  todayValue,
  utcIsoOf,
  type PickerValue,
} from '../../lib/pickerValues'
import { draftPartiesSchema, newDraftParty, partyFieldFromServer, toPartyRequest, type DraftParty } from '../draftParty'
import type { PickedJudge } from '../JudgePicker'

const v = sq.cases.register.validation

/** new = a new case (task 016); next = the next level of a closed case (task 018: object and parties are copied). */
export type RegistrationMode = 'new' | 'next'

const DEFAULT_HEARING_TYPE = 'PERGATITORE'

const pickerValue = z.custom<PickerValue>(isPickerValue)

/**
 * The hearing's date and time pickers: both required, and the time must exist on that day in Tirane (not skipped
 * by the change to summer time). Shared by the registration form and the "notify the lawyer" dialog.
 */
export function checkHearing(
  date: PickerValue,
  time: PickerValue,
  addIssue: (path: 'hearingDate' | 'hearingTime', message: string) => void,
) {
  if (!isFilled(date)) addIssue('hearingDate', v.hearingDateRequired)
  if (!isFilled(time)) addIssue('hearingTime', v.hearingTimeInvalid)
  else if (isFilled(date) && utcIsoOf(date, time) === null) addIssue('hearingTime', v.hearingTimeMissing)
}

/** Declared, not inferred: the schema's inferred type turns nullable objects into optional ones. */
export interface RegistrationValues {
  summonsNumber: string
  summonsTypeCode: string
  actNumber: string
  /** calendar dates from the date pickers */
  actDate: PickerValue
  receivedDate: PickerValue
  courtLevelCode: string
  courtId: string
  judge: PickedJudge | null
  showAllJudges: boolean
  hearingDate: PickerValue
  /** wall-clock time in Tirane (only HH:mm is used) */
  hearingTime: PickerValue
  hearingTypeCode: string
  hearingOtherText: string
  /** new mode only */
  objectText: string
  /** new mode only */
  parties: DraftParty[]
}

const requiredText = (max: number, message: string) =>
  z.string().refine((s) => s.trim().length > 0 && s.trim().length <= max, message)

export const buildRegistrationSchema = (mode: RegistrationMode, isOtherHearingType: (code: string) => boolean) =>
  z
    .object({
      summonsNumber: requiredText(50, v.summonsNumberRequired),
      summonsTypeCode: z.string(),
      actNumber: requiredText(50, v.actNumberRequired),
      actDate: pickerValue,
      receivedDate: pickerValue,
      courtLevelCode: z.string().min(1, v.courtLevelRequired),
      courtId: z.string().min(1, v.courtRequired),
      judge: z
        .object({ id: z.string(), name: z.string(), courtId: z.string().nullable() })
        .nullable()
        .refine((j) => j !== null, v.judgeRequired),
      showAllJudges: z.boolean(),
      hearingDate: pickerValue,
      hearingTime: pickerValue,
      hearingTypeCode: z.string().min(1, v.hearingTypeRequired),
      hearingOtherText: z.string(),
      // the next level copies the object and the parties from the previous one
      objectText: mode === 'new' ? requiredText(4000, v.objectRequired) : z.string(),
      parties: mode === 'new' ? draftPartiesSchema : z.array(z.unknown()),
    })
    .superRefine((f, ctx) => {
      const issue = (path: string, message: string) => ctx.addIssue({ code: 'custom', path: [path], message })

      // received: required, not in the future (Tirane); act: optional, not after the received date
      const received = isoDateOf(f.receivedDate)
      if (received === null) issue('receivedDate', v.receivedDateRequired)
      else if (received > todayInTirane()) issue('receivedDate', v.receivedDateFuture)
      if (f.actDate !== null) {
        const act = isoDateOf(f.actDate)
        if (act === null) issue('actDate', v.dateInvalid)
        else if (received !== null && act > received) issue('actDate', v.actDateAfterReceived)
      }

      checkHearing(f.hearingDate, f.hearingTime, issue)
      const other = f.hearingOtherText.trim()
      if (isOtherHearingType(f.hearingTypeCode) && (other.length === 0 || other.length > 200)) {
        ctx.addIssue({ code: 'custom', path: ['hearingOtherText'], message: v.hearingOtherRequired })
      }
    })

export type KeptValues = Pick<RegistrationValues, 'courtLevelCode' | 'courtId' | 'receivedDate'>

export const emptyRegistrationValues = (keep?: KeptValues): RegistrationValues => ({
  summonsNumber: '',
  summonsTypeCode: '',
  actNumber: '',
  actDate: null,
  receivedDate: keep?.receivedDate ?? todayValue(),
  courtLevelCode: keep?.courtLevelCode ?? '',
  courtId: keep?.courtId ?? '',
  judge: null,
  showAllJudges: false,
  hearingDate: null,
  hearingTime: null,
  hearingTypeCode: DEFAULT_HEARING_TYPE,
  hearingOtherText: '',
  objectText: '',
  parties: [newDraftParty()],
})

/** The registration request (task 016 shape). The next level sends the same minus object text and parties. */
export function toRegisterRequest(f: RegistrationValues, isOtherHearing: boolean, confirm: boolean): RegisterCaseRequest {
  return {
    summonsNumber: f.summonsNumber.trim(),
    summonsTypeCode: f.summonsTypeCode || null,
    actNumber: f.actNumber.trim(),
    actDate: isoDateOf(f.actDate),
    receivedDate: isoDateOf(f.receivedDate),
    courtLevelCode: f.courtLevelCode,
    courtId: Number(f.courtId),
    judgeId: f.judge ? Number(f.judge.id) : null,
    objectText: f.objectText.trim(),
    firstHearing: {
      at: utcIsoOf(f.hearingDate, f.hearingTime),
      hearingTypeCode: f.hearingTypeCode,
      otherTypeText: isOtherHearing ? f.hearingOtherText.trim() : null,
    },
    parties: f.parties.map(toPartyRequest),
    confirmPossibleDuplicate: confirm,
  }
}

/** Server field names (ProblemDetails.errors keys) → form fields. parties[i].x is handled separately. */
const SERVER_FIELDS: Record<string, Path<RegistrationValues>> = {
  summonsNumber: 'summonsNumber',
  summonsTypeCode: 'summonsTypeCode',
  actNumber: 'actNumber',
  actDate: 'actDate',
  receivedDate: 'receivedDate',
  courtLevelCode: 'courtLevelCode',
  courtId: 'courtId',
  judgeId: 'judge',
  objectText: 'objectText',
  firstHearing: 'hearingDate',
  'firstHearing.at': 'hearingDate',
  'firstHearing.hearingTypeCode': 'hearingTypeCode',
  'firstHearing.otherTypeText': 'hearingOtherText',
  parties: 'parties',
}

export function toFormPath(serverKey: string): Path<RegistrationValues> | null {
  const party = /^parties\[(\d+)\]\.(\w+)$/.exec(serverKey)
  if (party) {
    const field = partyFieldFromServer(party[2])
    return field ? (`parties.${Number(party[1])}.${field}` as Path<RegistrationValues>) : null
  }
  return SERVER_FIELDS[serverKey] ?? null
}

/** Scrolls to (and focuses) the first invalid field or error message of the forms in document order. */
export const scrollToFirstError = () =>
  requestAnimationFrame(() => {
    // pickers mark the invalid field on a wrapper (.Mui-error) rather than on an <input>
    const target = document.querySelector<HTMLElement>(
      'form [aria-invalid="true"], form .MuiPickersInputBase-root.Mui-error, form [data-form-error]',
    )
    target?.scrollIntoView({ block: 'center', behavior: 'smooth' })
    target?.focus({ preventScroll: true })
  })
