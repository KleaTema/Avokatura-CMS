import { Chip, type ChipProps } from '@mui/material'
import { sq } from '../../i18n/sq'

type ChipColor = NonNullable<ChipProps['color']>

/** The one place that maps a case state to a colour and an Albanian label. */
const STATUS: Record<string, ChipColor> = { REGISTERED: 'primary', ACTIVE: 'success', CLOSED: 'default' }
const PRIORITY: Record<string, ChipColor> = { HIGH: 'error', MEDIUM: 'warning', LOW: 'primary', NONE: 'default' }

export type StatusChipProps =
  | { kind: 'status'; value: string }
  | { kind: 'priority'; value: string }
  | { kind: 'suspended' }
  | { kind: 'deadlineSoon' }

/**
 * Status (REGISTERED / ACTIVE / CLOSED), priority (NONE / LOW / MEDIUM / HIGH), "Pezulluar" and "Afati i afërt" as
 * tinted pill chips. Unknown codes show the raw code in neutral grey.
 */
export function StatusChip(props: StatusChipProps & { size?: 'small' | 'medium'; label?: string }) {
  const size = props.size ?? 'small'
  switch (props.kind) {
    case 'status':
      return (
        <Chip
          size={size}
          color={STATUS[props.value] ?? 'default'}
          label={props.label ?? sq.cases.status[props.value] ?? props.value}
        />
      )
    case 'priority':
      return (
        <Chip
          size={size}
          color={PRIORITY[props.value] ?? 'default'}
          variant={props.value === 'NONE' ? 'outlined' : 'filled'}
          label={props.label ?? sq.cases.priority[props.value] ?? props.value}
        />
      )
    case 'suspended':
      return <Chip size={size} color="warning" label={props.label ?? sq.ui.suspended} />
    case 'deadlineSoon':
      return <Chip size={size} color="error" label={props.label ?? sq.ui.deadlineSoon} />
  }
}
