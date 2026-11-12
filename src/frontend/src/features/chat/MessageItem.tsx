import { Box, Button, IconButton, Link, Stack, Tooltip, Typography } from '@mui/material'
import { Pencil, Reply, Trash2 } from 'lucide-react'
import { memo } from 'react'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { tokens } from '../../theme/tokens'
import { linkify, SYSTEM, type ThreadMessage } from './model'

const t = sq.chat

/** The text as text: line breaks kept (pre-wrap), only http/https links, never HTML. */
function Body({ text }: { text: string }) {
  return (
    <>
      {linkify(text).map((part, i) =>
        part.href ? (
          <Link key={i} href={part.href} target="_blank" rel="noopener noreferrer" sx={{ wordBreak: 'break-all' }}>
            {part.text}
          </Link>
        ) : (
          <span key={i}>{part.text}</span>
        ),
      )}
    </>
  )
}

const timeOf = (utcIso: string) => formatDateTime(utcIso).slice(11)

/**
 * One message of the thread (task 050). Mine on the right, others on the left with the name at the start of a group;
 * a system message centered and muted. The actions (reply; edit and delete for my own within 15 minutes) appear on
 * hover and on keyboard focus.
 */
export const MessageItem = memo(function MessageItem({
  message,
  mine,
  firstOfGroup,
  replied,
  canReply,
  canChange,
  onReply,
  onEdit,
  onDelete,
  onRetry,
  failedText,
}: {
  message: ThreadMessage
  mine: boolean
  firstOfGroup: boolean
  /** the message this one answers, when it is loaded; undefined = not in view */
  replied?: ThreadMessage
  canReply: boolean
  canChange: boolean
  onReply: (m: ThreadMessage) => void
  onEdit: (m: ThreadMessage) => void
  onDelete: (m: ThreadMessage) => void
  onRetry: (m: ThreadMessage) => void
  failedText?: string
}) {
  if (message.type === SYSTEM) {
    return (
      <Typography variant="caption" color="text.secondary" component="p" sx={{ textAlign: 'center', my: 1, fontStyle: 'italic' }}>
        {message.body}
        {' · '}
        <Tooltip title={formatDateTime(message.createdAt)}>
          <span>{timeOf(message.createdAt)}</span>
        </Tooltip>
      </Typography>
    )
  }

  const actions = (canReply || canChange) && (
    <Stack
      direction="row"
      className="message-actions"
      aria-label={t.messageActions}
      sx={{ opacity: 0, transition: 'opacity 120ms', alignSelf: 'center', '&:focus-within': { opacity: 1 } }}
    >
      {canReply && (
        <Tooltip title={t.reply}>
          <IconButton size="small" onClick={() => onReply(message)} aria-label={t.reply}>
            <Reply size={18} aria-hidden />
          </IconButton>
        </Tooltip>
      )}
      {canChange && (
        <>
          <Tooltip title={t.edit}>
            <IconButton size="small" onClick={() => onEdit(message)} aria-label={t.edit}>
              <Pencil size={18} aria-hidden />
            </IconButton>
          </Tooltip>
          <Tooltip title={t.delete}>
            <IconButton size="small" onClick={() => onDelete(message)} aria-label={t.delete}>
              <Trash2 size={18} aria-hidden />
            </IconButton>
          </Tooltip>
        </>
      )}
    </Stack>
  )

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: mine ? 'flex-end' : 'flex-start',
        mt: firstOfGroup ? 1.5 : 0.25,
        '&:hover .message-actions': { opacity: 1 },
      }}
    >
      {firstOfGroup && !mine && (
        <Typography variant="caption" sx={{ fontWeight: 600, color: 'text.primary', px: 1.5, mb: 0.25 }}>
          {message.sender?.name}
        </Typography>
      )}
      <Stack direction={mine ? 'row-reverse' : 'row'} spacing={0.5} sx={{ maxWidth: { xs: '92%', md: '75%' } }}>
        <Box
          sx={{
            px: 1.5,
            py: 1,
            borderRadius: `${tokens.radius.control}px`,
            bgcolor: mine ? tokens.color.tint.blue : 'background.paper',
            border: `1px solid ${tokens.color.cardBorder}`,
            opacity: message.status === 'sending' ? 0.7 : 1,
            minWidth: 0,
          }}
        >
          {message.replyToMessageId && (
            <Box sx={{ borderLeft: 3, borderColor: 'primary.main', pl: 1, mb: 0.75 }}>
              <Typography variant="caption" color="primary" component="p" sx={{ fontWeight: 600 }}>
                {t.replyTo(replied?.sender?.name ?? '…')}
              </Typography>
              <Typography variant="caption" color="text.secondary" component="p" noWrap sx={{ maxWidth: 360 }}>
                {replied ? (replied.isDeleted ? t.deleted : replied.body) : t.replyUnavailable}
              </Typography>
            </Box>
          )}
          {message.isDeleted ? (
            <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic' }}>
              {t.deleted}
            </Typography>
          ) : (
            <Typography variant="body2" component="div" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
              <Body text={message.body} />
            </Typography>
          )}
          <Typography variant="caption" color="text.secondary" component="p" sx={{ textAlign: 'right', mt: 0.25 }}>
            {message.editedAt && !message.isDeleted && <>{t.edited} </>}
            {message.status === 'sending' ? (
              t.sending
            ) : (
              <Tooltip title={formatDateTime(message.createdAt)}>
                <span>{timeOf(message.createdAt)}</span>
              </Tooltip>
            )}
          </Typography>
        </Box>
        {actions}
      </Stack>
      {message.status === 'failed' && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 0.25 }}>
          <Typography variant="caption" color="error">
            {failedText ?? t.failed}
          </Typography>
          <Button size="small" color="error" onClick={() => onRetry(message)}>
            {t.retry}
          </Button>
        </Stack>
      )}
    </Box>
  )
})
