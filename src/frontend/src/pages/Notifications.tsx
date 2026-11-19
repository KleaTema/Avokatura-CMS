import { Alert, Box, Button, Link, TableCell, TableRow, Tooltip, Typography } from '@mui/material'
import { CheckCheck } from 'lucide-react'
import { useNotificationsState } from '../app/notificationsContext'
import { rangeOf, textOf } from '../components/filters/filterTypes'
import { TableFilterBar } from '../components/filters/TableFilterBar'
import { useUrlFilters } from '../components/filters/useUrlFilters'
import { NotificationIcon } from '../components/NotificationIcon'
import { DataTable, PaginationFooter } from '../components/ui/DataTable'
import { EmptyState } from '../components/ui/EmptyState'
import { PageHeader } from '../components/ui/PageHeader'
import { visuallyHidden } from '../components/ui/visuallyHidden'
import { useTableFilters } from '../features/filters/api'
import { useMarkAllRead, useNotifications } from '../features/notifications/api'
import { sq } from '../i18n/sq'
import { formatDateTime, formatRelative } from '../lib/dates'
import { useKeysetPaging } from '../lib/paging'
import { problemMessage } from '../lib/problem'
import { useLiveSearch } from '../lib/useLiveSearch'

const t = sq.notifications

const UNREAD = 'unread'

/**
 * "Njoftimet" (task 040): the user's notifications, newest first, 10 a page with the shared footer; the filter
 * Të palexuara / Të gjitha lives in the URL (?show=unread). A row opens its case page and marks it read.
 */
export function NotificationsPage() {
  // task 053m: the shared FilterBar (GET /api/filters/notifications), the values in the URL, sent to the API
  const meta = useTableFilters('notifications')
  const url = useUrlFilters(meta.data ?? [])
  const q = useLiveSearch(textOf(url.values, 'q'))
  const unreadOnly = textOf(url.values, 'show') === UNREAD
  const paging = useKeysetPaging()
  const list = useNotifications(
    {
      unreadOnly,
      type: textOf(url.values, 'type'),
      q,
      from: rangeOf(url.values, 'created', 'from'),
      to: rangeOf(url.values, 'created', 'to'),
    },
    { cursor: paging.cursor, size: paging.size },
  )
  const markAll = useMarkAllRead()
  const { unread, open } = useNotificationsState()
  const rows = list.data?.items ?? []


  return (
    <>
      <PageHeader
        title={t.title}
        subtitle={t.subtitle}
        actions={
          <Button
            variant="outlined"
            startIcon={<CheckCheck size={20} aria-hidden />}
            onClick={() => markAll.mutate()}
            disabled={!unread || markAll.isPending}
          >
            {t.markAll}
          </Button>
        }
      />

      <TableFilterBar meta={meta} url={url} busy={list.isFetching} />

      {list.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {problemMessage(list.error)}
        </Alert>
      )}
      {markAll.isError && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => markAll.reset()}>
          {problemMessage(markAll.error)}
        </Alert>
      )}

      <DataTable
        label={t.tableLabel}
        loading={list.isPending}
        columns={[
          { key: 'icon', label: '', width: 48 },
          { key: 'notification', label: t.colNotification },
        ]}
        empty={list.isSuccess && rows.length === 0 && <EmptyState title={unreadOnly ? t.emptyUnread : t.empty} />}
        footer={
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={rows.length}
            total={Number(list.data?.total ?? 0)}
            nextCursor={list.data?.nextCursor}
            busy={list.isFetching}
            stale={list.isPlaceholderData}
          />
        }
      >
        {rows.map((n) => {
          const isUnread = !n.readAt
          return (
            <TableRow key={n.publicId} hover>
              <TableCell sx={{ color: isUnread ? 'primary.main' : 'text.secondary', verticalAlign: 'top' }}>
                <NotificationIcon type={n.type} />
              </TableCell>
              <TableCell>
                <Link
                  component="button"
                  type="button"
                  underline="hover"
                  onClick={() => open(n)}
                  sx={{ textAlign: 'left', typography: 'body2', fontWeight: isUnread ? 700 : 400, color: 'text.primary' }}
                >
                  {n.title}
                  {isUnread && (
                    <Box component="span" sx={visuallyHidden}>
                      {` (${t.unread})`}
                    </Box>
                  )}
                </Link>
                {n.body && (
                  <Typography variant="body2" color="text.secondary">
                    {n.body}
                  </Typography>
                )}
                {/* the time under the text (one column fits a phone), the exact time on hover / focus */}
                <Box sx={{ mt: 0.5 }}>
                  <Tooltip title={formatDateTime(n.createdAt)}>
                    <Typography variant="caption" color="text.secondary" component="span" tabIndex={0}>
                      {formatRelative(n.createdAt)}
                    </Typography>
                  </Tooltip>
                </Box>
              </TableCell>
            </TableRow>
          )
        })}
      </DataTable>
    </>
  )
}
