import { Box } from '@mui/material'
import type { ReactNode } from 'react'
import { tokens } from '../../theme/tokens'

/** Short technical values (office code, NIPT, code template, file signature) in the code font. */
export function CodeText({ children }: { children: ReactNode }) {
  return (
    <Box component="span" sx={{ fontFamily: tokens.font.mono }}>
      {children}
    </Box>
  )
}
