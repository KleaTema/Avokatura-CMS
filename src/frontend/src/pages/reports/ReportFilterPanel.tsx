import { Badge, Box, Button, Chip, Collapse, Paper, Stack } from '@mui/material'
import { useId, useState, type ReactNode } from 'react'
import { sq } from '../../i18n/sq'

const t = sq.reports

export interface FilterChip {
  key: string
  label: string
  onDelete: () => void
}

/**
 * The shared filter layout of the reports: the always-visible row (`main`, the export at the end), the "Filtra" panel
 * (`panel`, collapsed; on a phone everything goes there), then the chips of the active filters and "Pastro filtrat".
 */
export function ReportFilterPanel({
  main,
  panel,
  action,
  chips,
  onClear,
}: {
  main?: ReactNode
  panel?: ReactNode
  /** the export button: always visible beside the filters */
  action: ReactNode
  chips: FilterChip[]
  onClear: () => void
}) {
  const [open, setOpen] = useState(false)
  const panelId = useId()

  return (
    <Stack spacing={1.5} sx={{ displayPrint: 'none' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ alignItems: { md: 'flex-start' } }}>
        <Stack direction="row" spacing={2} useFlexGap sx={{ flex: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          {main}
          {panel && (
            <Badge badgeContent={chips.length} color="primary">
              <Button variant={open ? 'contained' : 'outlined'} onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-controls={panelId}>
                {t.moreFilters}
              </Button>
            </Badge>
          )}
        </Stack>
        <Box>{action}</Box>
      </Stack>
      {panel && (
        <Collapse in={open} id={panelId}>
          <Paper variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: { md: 'flex-end' } }}>
              {panel}
            </Stack>
          </Paper>
        </Collapse>
      )}
      {chips.length > 0 && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }} aria-label={t.activeFilters}>
          {chips.map((chip) => (
            <Chip key={chip.key} label={chip.label} onDelete={chip.onDelete} />
          ))}
          <Button size="small" onClick={onClear}>
            {t.clearFilters}
          </Button>
        </Stack>
      )}
    </Stack>
  )
}
