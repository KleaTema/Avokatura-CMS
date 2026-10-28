import {
  AppBar,
  Box,
  CircularProgress,
  Drawer,
  List,
  ListItemButton,
  ListItemText,
  Toolbar,
  Typography,
} from '@mui/material'
import { Navigate, NavLink, Outlet } from 'react-router'
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
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {sq.appName}
          </Typography>
          {me.data && (
            <Typography variant="body2">
              {me.data.firstName} {me.data.lastName} ({me.data.roles.join(', ')})
            </Typography>
          )}
        </Toolbar>
      </AppBar>
      <Drawer
        variant="permanent"
        sx={{ width: DRAWER_WIDTH, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}
      >
        <Toolbar />
        <List component="nav">
          {me.data?.menus.map((code) => (
            <ListItemButton key={code} component={NavLink} to={menuPath(code)}>
              <ListItemText primary={sq.menu[code] ?? code} />
            </ListItemButton>
          ))}
          <ListItemButton onClick={() => void logout()}>
            <ListItemText primary={sq.logout} />
          </ListItemButton>
        </List>
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, p: 3 }}>
        <Toolbar />
        {content}
      </Box>
    </Box>
  )
}
