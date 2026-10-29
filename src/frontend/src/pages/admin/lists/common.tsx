import { Chip } from '@mui/material'
import { sq } from '../../../i18n/sq'

const t = sq.adminLists

export function StatusChip({ active }: { active: boolean }) {
  return <Chip size="small" color={active ? 'success' : 'default'} label={active ? t.active : t.inactive} />
}
