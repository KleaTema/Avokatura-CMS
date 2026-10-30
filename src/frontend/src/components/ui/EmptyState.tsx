import { Box, Stack, Typography } from '@mui/material'
import { Inbox, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { sq } from '../../i18n/sq'

/** Nothing to show: an icon, a title, an optional explanation and an optional action (e.g. "Pastro kërkimin"). */
export function EmptyState({
  title = sq.ui.emptyTitle,
  description,
  action,
  icon: Icon = Inbox,
}: {
  title?: ReactNode
  description?: ReactNode
  action?: ReactNode
  icon?: LucideIcon
}) {
  return (
    <Stack spacing={1} sx={{ alignItems: 'center', textAlign: 'center', py: 5, px: 2 }}>
      <Box aria-hidden sx={{ color: 'text.secondary' }}>
        <Icon size={36} strokeWidth={1.5} />
      </Box>
      <Typography variant="h3" component="p">
        {title}
      </Typography>
      {description && (
        <Typography variant="body2" color="text.secondary">
          {description}
        </Typography>
      )}
      {action}
    </Stack>
  )
}
