import { Box } from '@mui/material'
import type { ReactNode } from 'react'
import { tokens } from '../../theme/tokens'

/**
 * Task 053o — a range under ONE label: the label sits on the border like an outlined field's ("Data e mbylljes"), and
 * inside are the two fields ("Nga", "Deri"). Every date range (and number range) of the system uses it.
 */
export function RangeGroup({ label, children, width }: { label: string; children: ReactNode; width?: object }) {
  return (
    <Box
      component="fieldset"
      aria-label={label}
      sx={{
        m: 0,
        px: 1,
        pt: 0.75,
        pb: 1,
        minWidth: 0,
        display: 'flex',
        gap: 1,
        alignItems: 'flex-start',
        border: `1px solid ${tokens.color.cardBorder}`,
        borderRadius: `${tokens.radius.control}px`,
        bgcolor: 'background.paper',
        width: width ?? { xs: '100%', sm: 'auto' },
        '&:hover': { borderColor: tokens.color.primaryLight },
        '&:focus-within': { borderColor: 'primary.main', borderWidth: 2, px: 'calc(8px - 1px)' },
      }}
    >
      <Box
        component="legend"
        sx={{ px: 0.5, fontSize: '0.75rem', lineHeight: 1, color: 'text.secondary', '[fieldset]:focus-within > &': { color: 'primary.main' } }}
      >
        {label}
      </Box>
      {children}
    </Box>
  )
}
