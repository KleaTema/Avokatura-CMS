import { Stack } from '@mui/material'
import { useState } from 'react'
import { useSetPriority, type CaseDetail } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { AppSelect } from '../ui/AppSelect'
import { SuccessToast } from '../ui/SuccessToast'
import { useSaveOutcome } from './useSaveOutcome'

/**
 * The Kryetar's priority in the case header (case.set_priority_any): saved as soon as it is chosen, with the case's
 * latest rowVersion; the outcome like every other save (toast, or ConflictAlert with "Rifresko").
 */
export function HeaderPrioritySelect({ detail }: { detail: CaseDetail }) {
  const caseId = detail.header.publicId
  const save = useSetPriority(caseId)
  const outcome = useSaveOutcome(caseId)
  const [toast, setToast] = useState<string | null>(null)

  const change = async (priority: string) => {
    if (save.isPending || priority === detail.header.priority) return
    outcome.reset()
    try {
      await save.mutateAsync({ priority, rowVersion: Number(detail.header.rowVersion) })
      setToast(sq.cases.edit.priority.saved(sq.cases.priority[priority] ?? priority))
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Stack spacing={1}>
      {/* always one of the priorities ("Pa prioritet" included): no empty option */}
      <AppSelect
        required
        size="small"
        label={sq.cases.edit.priority.label}
        value={detail.header.priority}
        onChange={(value) => void change(value)}
        disabled={save.isPending || outcome.refreshing}
        options={Object.entries(sq.cases.priority).map(([code, name]) => ({ value: code, label: name }))}
        sx={{ width: 180 }}
      />
      {outcome.feedback}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}
