import { Box, Paper, Stack, Typography } from '@mui/material'
import { TrendingDown, TrendingUp, type LucideIcon } from 'lucide-react'
import { tones, type Tone } from '../../theme/tokens'

/**
 * A key number on a tinted card (blue / green / orange / red / purple): icon in a circle, title, big number and an
 * optional trend row. For the dashboard task — no page shows invented numbers.
 */
export function KpiCard({
  tone,
  icon: Icon,
  title,
  value,
  trend,
}: {
  tone: Tone
  icon: LucideIcon
  title: string
  value: string | number
  trend?: { direction: 'up' | 'down'; text: string }
}) {
  const color = tones[tone]
  const Trend = trend?.direction === 'down' ? TrendingDown : TrendingUp
  return (
    <Paper variant="outlined" sx={{ p: 2.5, bgcolor: color.bg }}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Box
          aria-hidden
          sx={{
            width: 48,
            height: 48,
            borderRadius: '50%',
            display: 'grid',
            placeItems: 'center',
            bgcolor: 'background.paper',
            color: color.fg,
            flexShrink: 0,
          }}
        >
          <Icon size={24} />
        </Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" color="text.secondary">
            {title}
          </Typography>
          <Typography variant="h1" component="p">
            {value}
          </Typography>
        </Box>
      </Stack>
      {trend && (
        <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', mt: 1.5, color: color.fg }}>
          <Trend size={16} aria-hidden />
          <Typography variant="caption">{trend.text}</Typography>
        </Stack>
      )}
    </Paper>
  )
}
