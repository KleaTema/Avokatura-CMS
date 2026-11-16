import { Box, CircularProgress, useMediaQuery, useTheme } from '@mui/material'
import { useCallback, useState } from 'react'
import { Navigate, Outlet, ScrollRestoration } from 'react-router'
import { ApiError } from '../api/client'
import { useMe } from '../features/identity/api'
import { sq } from '../i18n/sq'
import { ForbiddenPage, GenericErrorPage } from '../pages/ErrorPages'
import { tokens } from '../theme/tokens'
import { useAuth } from './authContext'
import { IdleLogout } from './IdleLogout'
import { NotificationsProvider } from './NotificationsProvider'
import { AppHeader } from './shell/AppHeader'
import { Sidebar } from './shell/Sidebar'
import { ChatProvider } from '../features/chat/ChatProvider'
import { useChat } from '../features/chat/chatContext'

/** The sidebar with the Chat menu's live unread count (task 050); it must sit inside ChatProvider. */
function ChatBadgeSidebar(props: Omit<Parameters<typeof Sidebar>[0], 'counts'>) {
  const { unread, unreadCapped } = useChat()
  // the server stops counting at 99; the Badge shows "99+" above its max of 99
  return <Sidebar {...props} counts={unread ? { chat: unreadCapped ? 100 : unread } : undefined} />
}

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
  const logoutNow = useCallback(() => void logout(), [logout])

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
    // one SignalR connection for the whole logged-in session (task 040), and one for the chat (task 050)
    <NotificationsProvider>
    <ChatProvider meId={me.data?.publicId}>
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default', '@media print': { display: 'block', bgcolor: 'common.white' } }}>
      {/* printing prints the page only (task 031): no header, no sidebar; "contents" keeps the flex layout on screen */}
      <Box sx={{ display: 'contents', displayPrint: 'none' }}>
        <AppHeader
          user={me.data}
          onMenu={drawer ? () => setMenuOpen((o) => !o) : undefined}
          menuOpen={menuOpen}
          onLogout={() => void logout()}
        />
        <ChatBadgeSidebar
          menus={me.data?.menus ?? []}
          collapsed={collapsed}
          temporary={drawer}
          open={menuOpen}
          onClose={() => setMenuOpen(false)}
        />
      </Box>
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          minWidth: 0,
          pt: `${tokens.layout.headerHeight}px`,
          bgcolor: 'background.default',
          '@media print': { pt: 0, bgcolor: 'common.white' },
        }}
      >
        <Box sx={{ p: tokens.layout.pagePadding, '@media print': { p: 0 } }}>{content}</Box>
      </Box>
      {/* Back/Forward return to the same scroll position (e.g. the case list, whose pages stay cached) */}
      <ScrollRestoration />
      {/* task 051: 30 minutes idle → a 60-second warning → logout (through the provider) */}
      <IdleLogout onLogout={logoutNow} />
    </Box>
    </ChatProvider>
    </NotificationsProvider>
  )
}
