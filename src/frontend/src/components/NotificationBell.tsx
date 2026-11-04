import {
  Badge,
  Box,
  Button,
  CircularProgress,
  Divider,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Popover,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material'
import { Bell } from 'lucide-react'
import { useId, useState } from 'react'
import { useNavigate } from 'react-router'
import { useNotificationsState } from '../app/notificationsContext'
import { useLatestNotifications, useMarkAllRead } from '../features/notifications/api'
import { sq } from '../i18n/sq'
import { formatDateTime, formatRelative } from '../lib/dates'
import { NotificationIcon } from './NotificationIcon'
import { visuallyHidden } from './ui/visuallyHidden'

const t = sq.notifications
const hidden = visuallyHidden

/**
 * The bell in the header (task 040): the unread count (99+), a popover with the newest 10 (unread in bold) — a click
 * marks it read and opens its case page — "Shëno të gjitha si të lexuara" and "Shiko të gjitha" (/notifications).
 * A notification that arrives live is announced politely to screen readers.
 */
export function NotificationBell() {
  const { unread, latestLive, open } = useNotificationsState()
  const navigate = useNavigate()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const popoverId = useId()
  const latest = useLatestNotifications(anchor !== null)
  const markAll = useMarkAllRead()
  const items = latest.data ?? []
  const close = () => setAnchor(null)

  return (
    <>
      <IconButton
        color="inherit"
        aria-label={unread ? t.bellWithCount(unread) : t.bell}
        aria-haspopup="dialog"
        aria-expanded={anchor !== null}
        aria-controls={anchor ? popoverId : undefined}
        onClick={(e) => setAnchor(e.currentTarget)}
      >
        <Badge color="error" badgeContent={unread} max={99} invisible={!unread}>
          <Bell size={22} aria-hidden />
        </Badge>
      </IconButton>
      <Box sx={hidden} aria-live="polite" aria-atomic="true">
        {latestLive ? t.announced(latestLive.title) : ''}
      </Box>

      <Popover
        id={popoverId}
        open={anchor !== null}
        anchorEl={anchor}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ paper: { role: 'dialog', 'aria-label': t.latest, sx: { width: { xs: 'calc(100vw - 24px)', sm: 400 }, maxWidth: 400 } } }}
      >
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: 2, py: 1.5, gap: 1 }}>
          <Typography variant="h3" component="h2">
            {t.latest}
          </Typography>
          <Button size="small" onClick={() => markAll.mutate()} disabled={!unread || markAll.isPending}>
            {t.markAll}
          </Button>
        </Stack>
        <Divider />
        {latest.isPending ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
            <CircularProgress size={24} aria-label={sq.errors.loading} />
          </Box>
        ) : latest.isError ? (
          <Typography variant="body2" color="error" sx={{ p: 2 }}>
            {t.loadError}
          </Typography>
        ) : items.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
            {t.empty}
          </Typography>
        ) : (
          <List dense disablePadding sx={{ maxHeight: 420, overflowY: 'auto' }}>
            {items.map((n) => {
              const isUnread = !n.readAt
              return (
                <ListItemButton
                  key={n.publicId}
                  onClick={() => {
                    close()
                    open(n)
                  }}
                  sx={{ alignItems: 'flex-start', py: 1.25 }}
                >
                  <ListItemIcon sx={{ minWidth: 36, mt: 0.25, color: isUnread ? 'primary.main' : 'text.secondary' }}>
                    <NotificationIcon type={n.type} />
                  </ListItemIcon>
                  <ListItemText
                    primary={
                      <>
                        {n.title}
                        {isUnread && <Box component="span" sx={hidden}>{` (${t.unread})`}</Box>}
                      </>
                    }
                    secondary={
                      <>
                        {n.body && (
                          <Box component="span" sx={{ display: 'block', color: 'text.secondary' }}>
                            {n.body}
                          </Box>
                        )}
                        <Tooltip title={formatDateTime(n.createdAt)}>
                          <Box component="span" sx={{ typography: 'caption', color: 'text.secondary' }}>
                            {formatRelative(n.createdAt)}
                          </Box>
                        </Tooltip>
                      </>
                    }
                    slotProps={{
                      primary: { variant: 'body2', sx: { fontWeight: isUnread ? 700 : 400 } },
                      secondary: { variant: 'body2', component: 'span' },
                    }}
                  />
                </ListItemButton>
              )
            })}
          </List>
        )}
        <Divider />
        <Box sx={{ p: 1, display: 'flex', justifyContent: 'center' }}>
          <Button
            onClick={() => {
              close()
              navigate('/notifications')
            }}
          >
            {t.seeAll}
          </Button>
        </Box>
      </Popover>
    </>
  )
}
