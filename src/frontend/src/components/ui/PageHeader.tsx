import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { formatLongDate } from '../../lib/dates'

/**
 * The top of every page: the title (h1), an optional subtitle, and actions on the right — at most one of them gold
 * (color="secondary", the page's main action). `showDate` adds today's date in Tirane ("E premte, 30 Tetor 2026").
 */
export function PageHeader({
  title,
  subtitle,
  actions,
  showDate,
}: {
  title: ReactNode
  subtitle?: ReactNode
  actions?: ReactNode
  showDate?: boolean
}) {
  return (
    <Stack
      direction={{ xs: 'column', md: 'row' }}
      spacing={2}
      useFlexGap
      sx={{ mb: 3, justifyContent: 'space-between', alignItems: { md: 'center' } }}
    >
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="h1">{title}</Typography>
        {subtitle && (
          <Typography variant="body1" color="text.secondary" sx={{ mt: 0.5 }}>
            {subtitle}
          </Typography>
        )}
      </Box>
      {(actions || showDate) && (
        <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          {showDate && (
            <Typography variant="body2" color="text.secondary">
              {formatLongDate()}
            </Typography>
          )}
          {actions}
        </Stack>
      )}
    </Stack>
  )
}
