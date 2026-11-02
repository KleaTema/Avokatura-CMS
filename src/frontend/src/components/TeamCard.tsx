import { Box, Button, Divider, Stack, Typography } from '@mui/material'
import { ArrowLeftRight } from 'lucide-react'
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
 * permissions) opens the transfer dialog; the API refuses it for a case that is not ACTIVE.
 */
export function TeamCard({ detail }: { detail: CaseDetail }) {
  const [transferring, setTransferring] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const team = [...detail.team].sort((a, b) => rank(a) - rank(b))
  const canTransfer = detail.permissions.includes('case.transfer') && detail.header.status === 'ACTIVE'

  return (
    <>
      <SectionCard
        title={t.title}
        actions={
          canTransfer && (
            <Button variant="outlined" size="small" startIcon={<ArrowLeftRight size={18} aria-hidden />} onClick={() => setTransferring(true)}>
              {t.transfer}
            </Button>
          )
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
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </>
  )
}
