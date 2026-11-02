import { Box, Button, Divider, IconButton, Stack, Tooltip, Typography } from '@mui/material'
import { ArrowLeftRight, UserMinus, UserPlus } from 'lucide-react'
import { AddAssistantDialog, RemoveAssistantDialog } from './AssistantDialogs'
import { Fragment, useState } from 'react'
import type { CaseDetail, CaseTeamMember } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { formatTiraneDate } from '../lib/dates'
import { TransferDialog } from './TransferDialog'
import { SectionCard } from './ui/SectionCard'
import { StatusChip } from './ui/StatusChip'
import { SuccessToast } from './ui/SuccessToast'

const t = sq.cases.team

/** Primary, secondary, legal assistants, institution jurists; within a role by assignment time (the API's order). */
const ROLE_ORDER = ['PRIMARY_LAWYER', 'SECONDARY_LAWYER', 'LEGAL_ASSISTANT', 'INSTITUTION_JURIST']
const rank = (m: CaseTeamMember) => {
  const i = ROLE_ORDER.indexOf(m.role)
  return i === -1 ? ROLE_ORDER.length : i
}

/**
 * "Ekipi": who works on the case now — name, role and since when. "Transfero" (case.transfer in the case's
 * permissions) opens the transfer dialog. The primary lawyer
 * (assistant.manage) adds legal assistants and removes them, after a confirmation (task 026).
 */
export function TeamCard({ detail }: { detail: CaseDetail }) {
  const [transferring, setTransferring] = useState(false)
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<CaseTeamMember | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const team = [...detail.team].sort((a, b) => rank(a) - rank(b))
  // permissions[] only: case.transfer exists only while ACTIVE (CaseAccessService state rules)
  const canTransfer = detail.permissions.includes('case.transfer')
  // the primary lawyer (assistant.manage, a case permission: never on a CLOSED case)
  const canManageAssistants = detail.permissions.includes('assistant.manage')

  return (
    <>
      <SectionCard
        title={t.title}
        actions={
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
            {canManageAssistants && (
              <Button variant="outlined" size="small" startIcon={<UserPlus size={18} aria-hidden />} onClick={() => setAdding(true)}>
                {sq.cases.assistants.add}
              </Button>
            )}
            {canTransfer && (
              <Button variant="outlined" size="small" startIcon={<ArrowLeftRight size={18} aria-hidden />} onClick={() => setTransferring(true)}>
                {t.transfer}
              </Button>
            )}
          </Stack>
        }
      >
        {team.length === 0 ? (
          <Typography color="text.secondary">{t.empty}</Typography>
        ) : (
          <Stack component="ul" sx={{ m: 0, p: 0, listStyle: 'none' }}>
            {team.map((m, i) => (
              <Fragment key={`${m.role}-${m.userPublicId}`}>
                {i > 0 && <Divider component="li" aria-hidden />}
                <Box
                  component="li"
                  sx={{
                    display: 'flex',
                    flexWrap: 'wrap',
                    alignItems: 'center',
                    columnGap: 2,
                    rowGap: 0.5,
                    py: 1.25,
                  }}
                >
                  <Typography sx={{ flex: '1 1 200px', minWidth: 0, fontWeight: 600 }}>{m.name}</Typography>
                  <StatusChip kind="role" value={m.role} />
                  <Typography variant="caption" color="text.secondary" sx={{ minWidth: 140, textAlign: { sm: 'right' } }}>
                    {t.assignedAt(formatTiraneDate(m.assignedAt))}
                  </Typography>
                  {/* the same width on every row, so the dates stay aligned */}
                  {canManageAssistants && (
                    <Box sx={{ width: 36, display: 'flex', justifyContent: 'center' }}>
                      {m.role === 'LEGAL_ASSISTANT' && (
                        <Tooltip title={sq.cases.assistants.remove}>
                          <IconButton
                            size="small"
                            color="error"
                            aria-label={sq.cases.assistants.removeLabel(m.name)}
                            onClick={() => setRemoving(m)}
                          >
                            <UserMinus size={18} aria-hidden />
                          </IconButton>
                        </Tooltip>
                      )}
                    </Box>
                  )}
                </Box>
              </Fragment>
            ))}
          </Stack>
        )}
      </SectionCard>

      {transferring && (
        <TransferDialog
          casePublicId={detail.header.publicId}
          onClose={() => setTransferring(false)}
          onTransferred={() => {
            setTransferring(false)
            setToast(sq.cases.transfer.success)
          }}
        />
      )}
      {adding && (
        <AddAssistantDialog
          detail={detail}
          onClose={() => setAdding(false)}
          onAdded={(name) => {
            setAdding(false)
            setToast(sq.cases.assistants.added(name))
          }}
        />
      )}
      {removing && (
        <RemoveAssistantDialog
          detail={detail}
          assistant={removing}
          onClose={() => setRemoving(null)}
          onRemoved={() => {
            setToast(sq.cases.assistants.removed(removing.name))
            setRemoving(null)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </>
  )
}
