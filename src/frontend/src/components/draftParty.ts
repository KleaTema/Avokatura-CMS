import { z } from 'zod'
import type { PartyRequest } from '../features/cases/api'
import { sq } from '../i18n/sq'
import type { PickedInstitution } from './InstitutionPicker'

const t = sq.partyEditor

export const PARTY_KINDS = ['NATURAL', 'LEGAL', 'INSTITUTION'] as const
export type PartyKind = (typeof PARTY_KINDS)[number] | ''

/** A party typed in the form before the case exists (DRAFT mode): sent with the registration request. */
export interface DraftParty {
  key: string
  partyRoleCode: string
  kind: PartyKind
  displayName: string
  identifier: string
  institution: PickedInstitution | null
}

export type DraftPartyField = 'partyRoleCode' | 'kind' | 'displayName' | 'identifier' | 'institution'
export type DraftPartyErrors = Partial<Record<DraftPartyField, string>>

export const newDraftParty = (): DraftParty => ({
  key: crypto.randomUUID(),
  partyRoleCode: '',
  kind: '',
  displayName: '',
  identifier: '',
  institution: null,
})

const NIPT = /^[A-Z]\d{8}[A-Z]$/i

/** Same rules as the server (PartyValidator): role and kind; a name for persons, NIPT format for legal entities. */
export const draftPartySchema = z
  .object({
    key: z.string(),
    partyRoleCode: z.string().min(1, t.validation.role),
    kind: z.enum(['', ...PARTY_KINDS]),
    displayName: z.string(),
    identifier: z.string(),
    institution: z.object({ publicId: z.string(), name: z.string() }).nullable(),
  })
  .superRefine((p, ctx) => {
    if (p.kind === '') ctx.addIssue({ code: 'custom', path: ['kind'], message: t.validation.kind })
    if (p.kind === 'INSTITUTION') {
      if (!p.institution) ctx.addIssue({ code: 'custom', path: ['institution'], message: t.validation.institution })
      return
    }
    const name = p.displayName.trim()
    if (p.kind !== '' && (name.length === 0 || name.length > 300)) {
      ctx.addIssue({ code: 'custom', path: ['displayName'], message: t.validation.name })
    }
    const nipt = p.identifier.trim()
    if (p.kind === 'LEGAL' && nipt !== '' && !NIPT.test(nipt)) {
      ctx.addIssue({ code: 'custom', path: ['identifier'], message: t.validation.nipt })
    }
  })

export const draftPartiesSchema = z.array(draftPartySchema).min(1, t.validation.required)

export function toPartyRequest(p: DraftParty): PartyRequest {
  if (p.kind === 'INSTITUTION') {
    return {
      partyRoleCode: p.partyRoleCode,
      kind: p.kind,
      institutionPublicId: p.institution?.publicId ?? null,
      displayName: null,
      identifier: null,
    }
  }
  return {
    partyRoleCode: p.partyRoleCode,
    kind: p.kind || null,
    institutionPublicId: null,
    displayName: p.displayName.trim(),
    identifier: p.kind === 'LEGAL' && p.identifier.trim() ? p.identifier.trim().toUpperCase() : null,
  }
}

const SERVER_PARTY_FIELDS: Record<string, DraftPartyField> = {
  partyRoleCode: 'partyRoleCode',
  kind: 'kind',
  displayName: 'displayName',
  identifier: 'identifier',
  institutionPublicId: 'institution',
}

/** Server field name inside a party (parties[i].<name>) → the editor's field. */
export const partyFieldFromServer = (name: string): DraftPartyField | null => SERVER_PARTY_FIELDS[name] ?? null
