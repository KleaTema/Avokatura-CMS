import {
  Avatar,
  Box,
  ButtonBase,
  Dialog,
  IconButton,
  Menu,
  MenuItem,
  Stack,
  Typography,
} from '@mui/material'
import { ChevronDown, LogOut, Menu as MenuIcon, Search, UserRound, X } from 'lucide-react'
import { useId, useState } from 'react'
import { useNavigate } from 'react-router'
import { GlobalSearch } from '../../components/GlobalSearch'
import { NotificationBell } from '../../components/NotificationBell'
import { sq } from '../../i18n/sq'
import { tokens } from '../../theme/tokens'
import { BrandImage } from './BrandImage'

const c = tokens.color
const s = sq.shell

export interface HeaderUser {
  firstName: string
  lastName: string
  roles: string[]
}

/**
 * The navy header: emblem + institution name, case search (→ /cases?q=…), the notification bell (task 040), user
 * menu (Profili, Dil). Needs the NotificationsProvider of the Layout.
 * `onMenu` is set below 900 px, where the sidebar is a drawer.
 */
export function AppHeader({
  user,
  onMenu,
  menuOpen,
  onLogout,
}: {
  user: HeaderUser | undefined
  onMenu?: () => void
  menuOpen: boolean
  onLogout: () => void
}) {
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [sheetOpen, setSheetOpen] = useState(false)
  const menuId = useId()

  const initials = user ? `${user.firstName.charAt(0)}${user.lastName.charAt(0)}`.toUpperCase() : ''
  const role = user?.roles.map((r) => sq.roleNames[r] ?? r).join(', ') ?? ''

  return (
    <Box
      component="header"
      sx={{
        position: 'fixed',
        inset: '0 0 auto 0',
        height: tokens.layout.headerHeight,
        zIndex: (t) => t.zIndex.drawer + 1,
        background: `linear-gradient(90deg, ${c.navy800} 0%, ${c.navyHeaderEnd} 100%)`,
        color: c.white,
        display: 'flex',
        alignItems: 'center',
        gap: { xs: 1, md: 2 },
        px: { xs: 1.5, md: 3 },
      }}
    >
      {onMenu && (
        <IconButton color="inherit" onClick={onMenu} aria-label={s.menu} aria-expanded={menuOpen}>
          <MenuIcon size={24} />
        </IconButton>
      )}

      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', minWidth: 0, flexShrink: 1 }}>
        <BrandImage
          src="/brand/emblem-white.webp"
          alt={sq.brand.emblemAlt}
          sx={{ height: tokens.layout.emblemSize, width: 'auto', display: { xs: 'none', sm: 'block' } }}
          wrap={(image) => (
            <>
              {image}
              <Box
                aria-hidden
                sx={{ width: '1px', height: 44, bgcolor: c.onNavy.border, display: { xs: 'none', sm: 'block' } }}
              />
            </>
          )}
        />
        <Box sx={{ minWidth: 0 }}>
          <Typography
            component="p"
            noWrap
            sx={{
              // one step smaller on a phone, where the menu, bell and avatar share the bar
              typography: { xs: 'body2', sm: 'h3' },
              color: c.white,
              fontWeight: 700,
              textTransform: 'uppercase',
              letterSpacing: { xs: '0.03em', sm: '0.06em' },
            }}
          >
            {sq.brand.title}
          </Typography>
          <Typography
            component="p"
            variant="body2"
            noWrap
            sx={{ color: c.onNavy.muted, display: { xs: 'none', sm: 'block' } }}
          >
            {sq.brand.subtitle}
          </Typography>
        </Box>
      </Stack>

      <Box sx={{ flex: 1 }} />

      {/* the typeahead (task 047) from 900 px; below, the icon opens a full-width search sheet */}
      <Box role="search" sx={{ display: { xs: 'none', md: 'block' }, width: { md: 360, lg: 460 } }}>
        <GlobalSearch variant="header" />
      </Box>
      <IconButton
        color="inherit"
        aria-label={sq.globalSearch.open}
        onClick={() => setSheetOpen(true)}
        sx={{ display: { xs: 'inline-flex', md: 'none' } }}
      >
        <Search size={22} aria-hidden />
      </IconButton>
      {/* disableAutoFocus: the dialog would take the focus from the search box */}
      <Dialog fullScreen disableAutoFocus open={sheetOpen} onClose={() => setSheetOpen(false)} aria-label={sq.globalSearch.label}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', p: 2 }} role="search">
          <GlobalSearch variant="sheet" onDone={() => setSheetOpen(false)} />
          <IconButton aria-label={sq.globalSearch.close} onClick={() => setSheetOpen(false)}>
            <X size={22} aria-hidden />
          </IconButton>
        </Stack>
      </Dialog>

      <NotificationBell />

      {user && (
        <>
          <ButtonBase
            onClick={(e) => setAnchor(e.currentTarget)}
            aria-label={s.userMenu}
            aria-haspopup="menu"
            aria-expanded={anchor !== null}
            aria-controls={anchor ? menuId : undefined}
            sx={{
              gap: 1.25,
              px: 1,
              py: 0.75,
              borderRadius: `${tokens.radius.control}px`,
              color: c.white,
              '&:hover': { bgcolor: c.onNavy.hover },
            }}
          >
            <Avatar sx={{ width: 38, height: 38, bgcolor: c.navActive, color: c.white }}>{initials}</Avatar>
            <Box sx={{ textAlign: 'left', display: { xs: 'none', lg: 'block' }, maxWidth: 220 }}>
              <Typography variant="subtitle2" noWrap sx={{ color: c.white }}>
                {user.firstName} {user.lastName}
              </Typography>
              <Typography variant="caption" noWrap component="p" sx={{ color: c.onNavy.muted }}>
                {role}
              </Typography>
            </Box>
            <Box component="span" sx={{ display: { xs: 'none', sm: 'inline-flex' } }}>
              <ChevronDown size={18} aria-hidden />
            </Box>
          </ButtonBase>
          <Menu
            id={menuId}
            anchorEl={anchor}
            open={anchor !== null}
            onClose={() => setAnchor(null)}
            anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            transformOrigin={{ vertical: 'top', horizontal: 'right' }}
          >
            <MenuItem
              onClick={() => {
                setAnchor(null)
                navigate('/profile')
              }}
            >
              <UserRound size={18} aria-hidden />
              <Box component="span" sx={{ ml: 1.5 }}>
                {s.profile}
              </Box>
            </MenuItem>
            <MenuItem
              onClick={() => {
                setAnchor(null)
                onLogout()
              }}
            >
              <LogOut size={18} aria-hidden />
              <Box component="span" sx={{ ml: 1.5 }}>
                {s.logout}
              </Box>
            </MenuItem>
          </Menu>
        </>
      )}
    </Box>
  )
}
