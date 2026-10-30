import { LivePartyEditor } from '../../../components/PartyEditor'
import type { CaseDetail } from '../../../features/cases/api'

/** Palët — LIVE editing; the buttons come only from the case's permissions (party.add / party.edit / party.delete). */
export function PartiesTab({ detail }: { detail: CaseDetail }) {
  return (
    <LivePartyEditor casePublicId={detail.header.publicId} parties={detail.parties} permissions={detail.permissions} />
  )
}
