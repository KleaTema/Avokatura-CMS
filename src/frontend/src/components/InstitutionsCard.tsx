import { Box, Button, Divider, Stack, Typography } from '@mui/material'
import { ArrowRightLeft, Undo2, UserCog } from 'lucide-react'
import { Fragment, useState } from 'react'
import type { CaseDetail, CaseInstitutionView } from '../features/cases/api'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'
import { AssignJuristDialog } from './AssignJuristDialog'
import { DelegateDialog, UndelegateDialog } from './DelegateDialog'
import { SectionCard } from './ui/SectionCard'
import { StatusChip } from './ui/StatusChip'
import { SuccessToast } from './ui/SuccessToast'

const t = sq.cases.institutions

/**
 * "Institucionet" (task 027): each institution linked to the case with its mode (Monitorim, or Delegim/Relatim — the
 * case's category tells which) and its jurist. Actions come from the case's permissions only:
 * - institution.assign_jurist → "Cakto juristin" next to the user's OWN institution;
 * - case.delegate (the primary lawyer) → "Delego / Relato" while nothing is delegated, "Anulo delegimin" after.
 */
export function InstitutionsCard({ detail }: { detail: CaseDetail }) {
  const me = useMe(true).data
  const [delegating, setDelegating] = useState(false)
  const [undelegating, setUndelegating] = useState<CaseInstitutionView | null>(null)
  const [jurist, setJurist] = useState<CaseInstitutionView | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const can = (code: string) => detail.permissions.includes(code)
  const delegated = detail.institutions.find((i) => i.mode === 'DELEGATED')
  // permissions[] only: case.delegate exists only while ACTIVE (CaseAccessService state rules); which of the two
  // buttons follows from the data: an active DELEGATED link means "Anulo delegimin"
  const canDelegate = can('case.delegate')
  const modeOf = (i: CaseInstitutionView) =>
    i.mode === 'DELEGATED' ? (detail.data.category?.code === 'RELATIM' ? 'RELATIM' : 'DELEGIM') : 'MONITORING'
  const isMine = (i: CaseInstitutionView) => me?.institution?.publicId === i.institutionPublicId

  if (detail.institutions.length === 0) return null

  return (
    <>
      <SectionCard
        title={t.title}
        actions={
          canDelegate &&
          (delegated ? (
            <Button variant="outlined" size="small" startIcon={<Undo2 size={18} aria-hidden />} onClick={() => setUndelegating(delegated)}>
              {t.undelegate}
            </Button>
          ) : (
            detail.institutions.some((i) => i.mode === 'MONITORING') && (
              <Button variant="outlined" size="small" startIcon={<ArrowRightLeft size={18} aria-hidden />} onClick={() => setDelegating(true)}>
                {t.delegate}
              </Button>
            )
          ))
        }
      >
        <Stack component="ul" sx={{ m: 0, p: 0, listStyle: 'none' }}>
          {detail.institutions.map((i, index) => (
            <Fragment key={i.institutionPublicId}>
              {index > 0 && <Divider component="li" aria-hidden />}
              <Box
                component="li"
                sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', columnGap: 2, rowGap: 0.75, py: 1.25 }}
              >
                <Box sx={{ flex: '1 1 260px', minWidth: 0 }}>
                  <Typography sx={{ fontWeight: 600 }}>{i.name}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    {i.juristNames.length > 0 ? `${t.jurist}: ${i.juristNames.join(', ')}` : t.noJurist}
                  </Typography>
                </Box>
                <StatusChip kind="link" value={modeOf(i)} />
                {can('institution.assign_jurist') && isMine(i) && (
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<UserCog size={18} aria-hidden />}
                    onClick={() => setJurist(i)}
                    aria-label={t.assignJuristFor(i.name)}
                  >
                    {t.assignJurist}
                  </Button>
                )}
              </Box>
            </Fragment>
          ))}
        </Stack>
      </SectionCard>

      {delegating && (
        <DelegateDialog
          detail={detail}
          onClose={() => setDelegating(false)}
          onDone={(name) => {
            setDelegating(false)
            setToast(t.delegated(name))
          }}
        />
      )}
      {undelegating && (
        <UndelegateDialog
          detail={detail}
          institutionName={undelegating.name}
          onClose={() => setUndelegating(null)}
          onDone={() => {
            setUndelegating(null)
            setToast(t.undelegated)
          }}
        />
      )}
      {jurist && (
        <AssignJuristDialog
          detail={detail}
          institutionName={jurist.name}
          onClose={() => setJurist(null)}
          onDone={(name) => {
            setJurist(null)
            setToast(t.juristSaved(name))
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </>
  )
}
