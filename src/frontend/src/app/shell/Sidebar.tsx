import { Badge, Box, Drawer, List, ListItemButton, Tooltip, Typography } from '@mui/material'
import { NavLink } from 'react-router'
import { sq } from '../../i18n/sq'
import { tokens } from '../../theme/tokens'
import { menuPath } from '../menu'
import { BrandImage } from './BrandImage'
import { navIcon } from './navIcons'

const c = tokens.color
const l = tokens.layout

/**
 * The navy sidebar: one item per code of me.menus, in that order. `collapsed` (below 1200 px) shows icons with
 * tooltips; `temporary` (below 900 px) is a drawer opened from the header.
 */
export function Sidebar({
  menus,
  collapsed,
  temporary,
  open,
  onClose,
  counts,
}: {
  menus: string[]
  collapsed: boolean
  temporary: boolean
  open: boolean
  onClose: () => void
  /** optional red count per menu code (none yet) */
  counts?: Record<string, number>
}) {
  const width = collapsed && !temporary ? l.sidebarCollapsedWidth : l.sidebarWidth
  const showLabels = temporary || !collapsed

  return (
    <Drawer
      variant={temporary ? 'temporary' : 'permanent'}
      open={temporary ? open : true}
      onClose={onClose}
      sx={{
        width: temporary ? undefined : width,
        flexShrink: 0,
        '& .MuiDrawer-paper': {
          width,
          boxSizing: 'border-box',
          border: 0,
          color: c.white,
          background: `linear-gradient(180deg, ${c.navy700} 0%, ${c.navy900} 100%)`,
          overflowX: 'hidden',
        },
      }}
    >
      <Box sx={{ height: l.headerHeight, flexShrink: 0 }} />
      <List component="nav" aria-label={sq.shell.mainNav} sx={{ py: 1.5 }}>
        {menus.map((code) => {
          const Icon = navIcon(code)
          const label = sq.menu[code] ?? code
          const count = counts?.[code]
          const item = (
            <ListItemButton
              key={code}
              component={NavLink}
              to={menuPath(code)}
              onClick={temporary ? onClose : undefined}
              aria-label={showLabels ? undefined : label}
              sx={{
                position: 'relative',
                height: l.sidebarRowHeight,
                gap: 1.75,
                px: showLabels ? 3 : 0,
                justifyContent: showLabels ? 'flex-start' : 'center',
                color: c.white,
                '&:hover': { bgcolor: c.onNavy.hover },
                '&.active': { bgcolor: c.navActive },
                '&.active::before': {
                  content: '""',
                  position: 'absolute',
                  left: 0,
                  top: 0,
                  bottom: 0,
                  width: l.activeBarWidth,
                  bgcolor: c.primaryLight,
                },
              }}
            >
              <Badge color="error" badgeContent={count} invisible={!count}>
                <Icon size={22} strokeWidth={1.75} aria-hidden />
              </Badge>
              {showLabels && (
                <Typography variant="body1" noWrap sx={{ color: c.white }}>
                  {label}
                </Typography>
              )}
            </ListItemButton>
          )
          return showLabels ? (
            item
          ) : (
            <Tooltip key={code} title={label} placement="right">
              {item}
            </Tooltip>
          )
        })}
      </List>

      {showLabels && (
        <Box sx={{ mt: 'auto', px: 3, pb: 3 }}>
          {/* the gold line belongs to the illustration: both appear only when the image exists */}
          <BrandImage
            src="/brand/building.svg"
            alt=""
            sx={{ width: '100%', maxWidth: 180, display: 'block' }}
            wrap={(image) => (
              <>
                <Box aria-hidden sx={{ width: 48, height: 2, bgcolor: c.gold, mb: 2 }} />
                {image}
              </>
            )}
          />
        </Box>
      )}
    </Drawer>
  )
}
