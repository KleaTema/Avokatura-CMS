import { Box, Link as MuiLink, Typography } from '@mui/material'
import { Link } from 'react-router'
import { tokens } from '../../theme/tokens'

/**
 * A case code (e.g. 2026-483915/2) in the code font: a blue link to the case when `publicId` is given, plain text
 * otherwise (e.g. inside a row that is itself the link). `size="large"` for the case page header and the
 * registration result.
 */
export function CaseCode({
  code,
  publicId,
  size = 'normal',
}: {
  code: string
  publicId?: string | null
  size?: 'normal' | 'large'
}) {
  const sx = { fontFamily: tokens.font.mono, fontWeight: 600, wordBreak: 'break-all' } as const
  if (size === 'large') {
    return (
      <Typography variant="h1" component="span" sx={{ ...sx, color: 'primary.main' }}>
        {code}
      </Typography>
    )
  }
  return publicId ? (
    <MuiLink component={Link} to={`/cases/${publicId}`} sx={sx}>
      {code}
    </MuiLink>
  ) : (
    <Box component="span" sx={{ ...sx, color: 'primary.main' }}>
      {code}
    </Box>
  )
}
