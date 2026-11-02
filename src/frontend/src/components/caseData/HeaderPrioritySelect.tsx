import { MenuItem, Stack, TextField } from '@mui/material'
import { useSetPriority, type CaseDetail } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { useSaveOutcome } from './useSaveOutcome'

/**
 * The Kryetar's priority in the case header (case.set_priority_any): saved as soon as it is chosen, with the case's
 * latest rowVersion; a 409 shows the message with "Rifresko".
 */
export function HeaderPrioritySelect({ detail }: { detail: CaseDetail }) {
  const caseId = detail.header.publicId
  const save = useSetPriority(caseId)
  const outcome = useSaveOutcome(caseId)

  const change = async (priority: string) => {
    if (save.isPending || priority === detail.header.priority) return
    outcome.reset()
    try {
      await save.mutateAsync({ priority, rowVersion: Number(detail.header.rowVersion) })
    } catch (error) {
      outcome.fail(error)
    }
  }

  return (
    <Stack spacing={1}>
      <TextField
        select
        size="small"
        label={sq.cases.edit.priority.label}
        value={detail.header.priority}
        onChange={(ev) => void change(ev.target.value)}
        disabled={save.isPending || outcome.refreshing}
        sx={{ width: 180 }}
      >
        {Object.entries(sq.cases.priority).map(([code, name]) => (
          <MenuItem key={code} value={code}>
            {name}
          </MenuItem>
        ))}
      </TextField>
      {outcome.feedback}
    </Stack>
  )
}
