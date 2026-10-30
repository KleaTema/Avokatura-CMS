import { Box, Button, Stack, Tooltip, Typography } from '@mui/material'
import { Link } from 'react-router'
import type { CaseDetail } from '../features/cases/api'
import { sq } from '../i18n/sq'

const t = sq.cases.page

/**
 * The levels of the dossier (X/1, X/2 …). The current one is highlighted; a level the user cannot view is shown
 * disabled with a tooltip (its code, court level and status are public within the dossier, its id is not sent).
 * `tab` keeps the same tab when switching.
 */
export function LevelSwitcher({
  levels,
  currentPublicId,
  tab,
}: {
  levels: CaseDetail['levels']
  currentPublicId: string
  tab: string
}) {
  if (levels.length < 2) return null
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} role="navigation" aria-label={t.levels}>
      <Typography variant="body2" color="text.secondary">
        {t.levels}:
      </Typography>
      {levels.map((level) => {
        const status = sq.cases.status[level.status] ?? level.status
        const label = t.levelLabel(level.caseCode, level.courtLevelName, status)
        const current = level.publicId === currentPublicId
        if (!level.canOpen || !level.publicId) {
          return (
            <Tooltip key={level.caseCode} title={t.levelNoAccess}>
              {/* a disabled button gets no pointer events: the wrapper carries the tooltip */}
              <Box component="span">
                <Button size="small" variant="outlined" disabled aria-label={`${label}. ${t.levelNoAccess}`}>
                  {level.caseCode}
                </Button>
              </Box>
            </Tooltip>
          )
        }
        return (
          <Tooltip key={level.caseCode} title={label}>
            <Button
              size="small"
              variant={current ? 'contained' : 'outlined'}
              component={Link}
              to={`/cases/${level.publicId}/${tab}`}
              aria-current={current ? 'page' : undefined}
              aria-label={label}
            >
              {level.caseCode}
            </Button>
          </Tooltip>
        )
      })}
    </Stack>
  )
}
