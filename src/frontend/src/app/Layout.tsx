import {
  AppBar,
  Box,
  Button,
  CircularProgress,
  Drawer,
  List,
  ListItemButton,
  ListItemText,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useState } from 'react'
import { Navigate, NavLink, Outlet, ScrollRestoration } from 'react-router'
import { ApiError } from '../api/client'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'
import { ForbiddenPage, GenericErrorPage } from '../pages/ErrorPages'
import { useAuth } from './authContext'
import { menuPath } from './menu'

const DRAWER_WIDTH = 220

export function Layout() {
  const { isAuthenticated, isLoading, logout } = useAuth()
  const me = useMe(isAuthenticated)
  const narrow = useMediaQuery(useTheme().breakpoints.down('md'))
  const [menuOpen, setMenuOpen] = useState(false)

  if (isLoading) return <CircularProgress />
  if (!isAuthenticated) return <Navigate to="/login" replace />

  const status = me.error instanceof ApiError ? me.error.status : null
  if (status === 401) return <Navigate to="/login" replace />

  let content
  if (me.isError) {
    content = status === 403 ? <ForbiddenPage /> : <GenericErrorPage />
  } else if (!me.data) {
    content = <CircularProgress />
  } else {
    content = <Outlet />
  }

  return (
    <Box sx={{ display: 'flex' }}>
      <AppBar position="fixed" sx={{ zIndex: (t) => t.zIndex.drawer + 1 }}>
        <Toolbar>
          {narrow && (
            <Button
              color="inherit"
              onClick={() => setMenuOpen((o) => !o)}
              aria-label={sq.topBar.menu}
              aria-expanded={menuOpen}
              sx={{ minWidth: 0, mr: 1, fontSize: 22, lineHeight: 1 }}
            >
              ☰
            </Button>
          )}
          <Typography variant="h6" noWrap sx={{ flexGrow: 1 }}>
            {sq.appName}
          </Typography>
          {me.data && (
            <Typography variant="body2" sx={{ display: { xs: 'none', sm: 'block' } }}>
              {me.data.firstName} {me.data.lastName} ({me.data.roles.join(', ')})
            </Typography>
          )}
        </Toolbar>
      </AppBar>
      {/* permanent on wide screens; on a phone a menu that slides in and closes after choosing a page */}
      <Drawer
        variant={narrow ? 'temporary' : 'permanent'}
        open={narrow ? menuOpen : true}
        onClose={() => setMenuOpen(false)}
        sx={{
          width: narrow ? undefined : DRAWER_WIDTH,
          flexShrink: 0,
          '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' },
        }}
      >
        <Toolbar />
        <List component="nav">
          {me.data?.menus.map((code) => (
            <ListItemButton key={code} component={NavLink} to={menuPath(code)} onClick={() => setMenuOpen(false)}>
              <ListItemText primary={sq.menu[code] ?? code} />
            </ListItemButton>
          ))}
          <ListItemButton onClick={() => void logout()}>
            <ListItemText primary={sq.logout} />
          </ListItemButton>
        </List>
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, p: { xs: 2, md: 3 }, minWidth: 0 }}>
        <Toolbar />
        {content}
      </Box>
      {/* Back/Forward return to the same scroll position (e.g. the case list, whose pages stay cached) */}
      <ScrollRestoration />
    </Box>
  )
}
