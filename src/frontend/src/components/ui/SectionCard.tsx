import { Box, Divider, IconButton, Link as MuiLink, Menu, Paper, Stack, Typography } from '@mui/material'
import { ArrowRight, Ellipsis } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { sq } from '../../i18n/sq'

/**
 * A white card with a title (h3), an optional "⋯" menu (`menu` = MenuItems) and an optional "Shiko të gjitha →"
 * link, a divider, then the content. `component="section"` so the title heads a region.
 */
export function SectionCard({
  title,
  menu,
  viewAllTo,
  actions,
  children,
  padded = true,
}: {
  title?: ReactNode
  /** MenuItem elements of the "⋯" menu */
  menu?: ReactNode
  viewAllTo?: string
  /** buttons next to the title */
  actions?: ReactNode
  children: ReactNode
  /** false for content that brings its own padding (tables) */
  padded?: boolean
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const menuId = useId()
  const hasHeader = title !== undefined || menu || viewAllTo || actions

  return (
    <Paper variant="outlined" component="section">
      {hasHeader && (
        <>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 2.5, py: 1.75, minHeight: 56 }}>
            <Typography variant="h3" component="h2" sx={{ flex: 1, minWidth: 0 }}>
              {title}
            </Typography>
            {actions}
            {viewAllTo && (
              <MuiLink component={Link} to={viewAllTo} sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
                {sq.ui.viewAll}
                <ArrowRight size={16} aria-hidden />
              </MuiLink>
            )}
            {menu && (
              <>
                <IconButton
                  size="small"
                  aria-label={sq.ui.moreActions}
                  aria-haspopup="menu"
                  aria-controls={anchor ? menuId : undefined}
                  onClick={(e) => setAnchor(e.currentTarget)}
                >
                  <Ellipsis size={18} />
                </IconButton>
                <Menu id={menuId} anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)} onClick={() => setAnchor(null)}>
                  {menu}
                </Menu>
              </>
            )}
          </Stack>
          <Divider />
        </>
      )}
      <Box sx={padded ? { p: 2.5 } : undefined}>{children}</Box>
    </Paper>
  )
}
