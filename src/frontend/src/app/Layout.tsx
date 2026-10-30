import { Box, CircularProgress, useMediaQuery, useTheme } from '@mui/material'
import { useState } from 'react'
import { Navigate, Outlet, ScrollRestoration } from 'react-router'
import { ApiError } from '../api/client'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'
import { ForbiddenPage, GenericErrorPage } from '../pages/ErrorPages'
import { tokens } from '../theme/tokens'
import { useAuth } from './authContext'
import { AppHeader } from './shell/AppHeader'
import { Sidebar } from './shell/Sidebar'

/**
 * The app shell: navy header, navy sidebar (icons only below 1200 px, a drawer below 900 px), and the page on the
 * light page background.
 */
export function Layout() {
  const { isAuthenticated, isLoading, logout } = useAuth()
  const me = useMe(isAuthenticated)
  const theme = useTheme()
  const drawer = useMediaQuery(theme.breakpoints.down('md')) // < 900 px
  const collapsed = useMediaQuery(theme.breakpoints.down('lg')) // < 1200 px
  const [menuOpen, setMenuOpen] = useState(false)

  if (isLoading) return <CircularProgress aria-label={sq.errors.loading} />
  if (!isAuthenticated) return <Navigate to="/login" replace />

  const status = me.error instanceof ApiError ? me.error.status : null
  if (status === 401) return <Navigate to="/login" replace />

  let content
  if (me.isError) {
    content = status === 403 ? <ForbiddenPage /> : <GenericErrorPage />
  } else if (!me.data) {
    content = <CircularProgress aria-label={sq.errors.loading} />
  } else {
    content = <Outlet />
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppHeader
        user={me.data}
        onMenu={drawer ? () => setMenuOpen((o) => !o) : undefined}
        menuOpen={menuOpen}
        onLogout={() => void logout()}
      />
      <Sidebar
        menus={me.data?.menus ?? []}
        collapsed={collapsed}
        temporary={drawer}
        open={menuOpen}
        onClose={() => setMenuOpen(false)}
      />
      <Box
        component="main"
        sx={{ flexGrow: 1, minWidth: 0, pt: `${tokens.layout.headerHeight}px`, bgcolor: 'background.default' }}
      >
        <Box sx={{ p: tokens.layout.pagePadding }}>{content}</Box>
      </Box>
      {/* Back/Forward return to the same scroll position (e.g. the case list, whose pages stay cached) */}
      <ScrollRestoration />
    </Box>
  )
}
