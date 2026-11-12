import {
  Alert,
  Badge,
  Box,
  Button,
  IconButton,
  List,
  ListItemButton,
  Paper,
  Skeleton,
  Stack,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { ArrowLeft } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { PageHeader } from '../../components/ui/PageHeader'
import { useConversationPages, type ChatConversation } from '../../features/chat/api'
import { ChatThread } from '../../features/chat/ChatThread'
import { ParticipantsButton } from '../../features/chat/ParticipantsButton'
import { sq } from '../../i18n/sq'
import { formatDateTime, formatRelative } from '../../lib/dates'
import { tokens } from '../../theme/tokens'

const t = sq.chat

/** Header + thread + composer fill the screen below the page header. */
const THREAD_HEIGHT = { xs: 'calc(100dvh - 200px)', md: 'calc(100dvh - 230px)' }

function ConversationRow({ c, selected, onOpen }: { c: ChatConversation; selected: boolean; onOpen: () => void }) {
  const unread = Number(c.unread)
  return (
    <ListItemButton
      selected={selected}
      onClick={onOpen}
      aria-current={selected ? 'true' : undefined}
      sx={{ alignItems: 'flex-start', gap: 1.5, py: 1.25, borderBottom: `1px solid ${tokens.color.cardBorder}` }}
    >
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline' }}>
          <Typography variant="subtitle2" noWrap sx={{ flex: 1, fontWeight: unread ? 700 : 600 }}>
            {c.title}
          </Typography>
          {c.lastMessageAt && (
            <Tooltip title={formatDateTime(c.lastMessageAt)}>
              <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
                {formatRelative(c.lastMessageAt)}
              </Typography>
            </Tooltip>
          )}
        </Stack>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Typography variant="body2" color="text.secondary" noWrap sx={{ flex: 1 }}>
            {c.lastMessagePreview ?? t.noMessages}
          </Typography>
          {unread > 0 && (
            <Badge
              color="error"
              badgeContent={c.unreadCapped ? '99+' : unread}
              aria-label={t.unread(unread, c.unreadCapped)}
              sx={{ '& .MuiBadge-badge': { position: 'static', transform: 'none' } }}
            />
          )}
        </Stack>
        {c.isReadOnly && (
          <Typography variant="caption" color="text.secondary">
            {t.readOnly}
          </Typography>
        )}
      </Box>
    </ListItemButton>
  )
}

/**
 * "Chat" (task 050): my case conversations on the left (newest activity first, keyset "Shfaq më shumë"), the thread
 * on the right. Below 900 px one at a time: the list, then the thread with a back arrow. The chosen conversation is in
 * the URL (?c=…), so Back and a reload keep it.
 */
export function ChatPage() {
  const theme = useTheme()
  const narrow = useMediaQuery(theme.breakpoints.down('md'))
  const [params, setParams] = useSearchParams()
  const selectedId = params.get('c') ?? undefined
  const [cursors, setCursors] = useState<string[]>([])
  const pages = useConversationPages(cursors)
  const more = pages[pages.length - 1]
  const items = dedupe(pages.flatMap((p) => p.data?.items ?? []))
  const nextCursor = pages[pages.length - 1].data?.nextCursor ?? undefined
  const selected = items.find((c) => c.id === selectedId)
  const first = pages[0]

  const choose = (id: string | undefined) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (id) next.set('c', id)
      else next.delete('c')
      return next
    })

  const list = (
    <Paper variant="outlined" component="section" aria-label={t.conversations} sx={{ display: 'flex', flexDirection: 'column', height: THREAD_HEIGHT, minHeight: 0 }}>
      <Typography variant="h3" component="h2" sx={{ px: 2, py: 1.75, borderBottom: `1px solid ${tokens.color.cardBorder}` }}>
        {t.conversations}
      </Typography>
      <Box sx={{ flex: 1, overflowY: 'auto' }}>
        {first.isPending && [0, 1, 2].map((i) => <Skeleton key={i} variant="rectangular" height={64} sx={{ m: 1 }} />)}
        {first.isError && <Alert severity="error" sx={{ m: 2 }}>{t.loadError}</Alert>}
        {first.isSuccess && items.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
            {t.noConversations}
          </Typography>
        )}
        <List disablePadding>
          {items.map((c) => (
            <ConversationRow key={c.id} c={c} selected={c.id === selectedId} onOpen={() => choose(c.id)} />
          ))}
        </List>
        {nextCursor && (
          <Box sx={{ p: 1.5, textAlign: 'center' }}>
            <Button onClick={() => setCursors((c) => [...c, nextCursor])} disabled={more.isFetching}>
              {t.loadMore}
            </Button>
          </Box>
        )}
      </Box>
    </Paper>
  )

  // a conversation in the URL that is not (or no longer) in my list still opens: the API decides — a 403 shows
  // "Nuk jeni më pjesë e kësaj bisede." (the fallback when the live participants.changed was missed)
  const thread = selectedId ? (
    <Paper variant="outlined" component="section" aria-label={selected?.title ?? t.messages} sx={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 1.5, py: 1, borderBottom: `1px solid ${tokens.color.cardBorder}` }}>
        {narrow && (
          <Tooltip title={t.back}>
            <IconButton onClick={() => choose(undefined)} aria-label={t.back}>
              <ArrowLeft size={20} aria-hidden />
            </IconButton>
          </Tooltip>
        )}
        {/* on a phone the buttons leave little room: the title wraps to two lines instead of "202…" */}
        <Typography
          variant="h3"
          component="h2"
          sx={{
            flex: 1,
            minWidth: 0,
            typography: { xs: 'subtitle2', md: 'h3' },
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
          }}
        >
          {selected?.title}
        </Typography>
        {selected?.casePublicId && (
          <Button size="small" component={Link} to={`/cases/${selected.casePublicId}/chat`}>
            {t.openCase}
          </Button>
        )}
        {selected && <ParticipantsButton conversationId={selected.id} />}
      </Stack>
      <ChatThread
        key={selectedId}
        conversationId={selectedId}
        readOnly={selected?.isReadOnly ?? false}
        height={{ xs: 'calc(100dvh - 260px)', md: 'calc(100dvh - 290px)' }}
      />
    </Paper>
  ) : (
    <Paper variant="outlined" sx={{ display: 'grid', placeItems: 'center', height: THREAD_HEIGHT, p: 3 }}>
      <Typography variant="body2" color="text.secondary">
        {t.chooseConversation}
      </Typography>
    </Paper>
  )

  return (
    <>
      <PageHeader title={t.title} subtitle={narrow && selected ? undefined : t.subtitle} />
      {narrow ? (
        selectedId ? thread : list
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: { md: '320px 1fr', lg: '380px 1fr' }, gap: 2.5, alignItems: 'start' }}>
          {list}
          {thread}
        </Box>
      )}
    </>
  )
}

function dedupe(items: ChatConversation[]): ChatConversation[] {
  const seen = new Set<string>()
  return items.filter((c) => (seen.has(c.id) ? false : (seen.add(c.id), true)))
}
