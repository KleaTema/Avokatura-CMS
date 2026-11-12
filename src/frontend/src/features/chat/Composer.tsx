import { Box, Button, IconButton, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { Send, X } from 'lucide-react'
import { useEffect, useId, useState, type KeyboardEvent } from 'react'
import { sq } from '../../i18n/sq'

const t = sq.chat

export const MAX_BODY = 4000
/** The counter shows from here on. */
const COUNTER_FROM = 3500

const draftKey = (conversationId: string) => `ash.chat.draft.${conversationId}`

/** The draft of a conversation survives a reload of the tab (sessionStorage; it may be unavailable — then no draft). */
function readDraft(conversationId: string): string {
  try {
    return sessionStorage.getItem(draftKey(conversationId)) ?? ''
  } catch {
    return ''
  }
}

function writeDraft(conversationId: string, text: string) {
  try {
    if (text) sessionStorage.setItem(draftKey(conversationId), text)
    else sessionStorage.removeItem(draftKey(conversationId))
  } catch {
    // storage blocked (private mode, quota): the draft just is not kept
  }
}

/**
 * The message box (task 050): multiline, Enter sends, Shift+Enter a new line, a counter near 4,000. Shows the message
 * being answered or edited above the box. In edit mode Enter saves and Esc cancels.
 */
export function Composer({
  conversationId,
  disabled,
  banner,
  replyTo,
  onCancelReply,
  editing,
  onCancelEdit,
  onSend,
  onSaveEdit,
}: {
  conversationId: string
  disabled: boolean
  /** shown instead of the box (read-only, removed) */
  banner?: string
  replyTo?: { name: string; excerpt: string }
  onCancelReply: () => void
  editing?: { id: string; body: string }
  onCancelEdit: () => void
  onSend: (body: string) => void
  onSaveEdit: (id: string, body: string) => void
}) {
  const [text, setText] = useState(() => readDraft(conversationId))
  const [editText, setEditText] = useState('')
  const counterId = useId()
  // the conversation changes in the same component (the chat page): load its own draft
  const [draftOf, setDraftOf] = useState(conversationId)
  if (draftOf !== conversationId) {
    setDraftOf(conversationId)
    setText(readDraft(conversationId))
  }
  const [editOf, setEditOf] = useState<string | undefined>(undefined)
  if (editing?.id !== editOf) {
    setEditOf(editing?.id)
    setEditText(editing?.body ?? '')
  }

  useEffect(() => writeDraft(conversationId, text), [conversationId, text])

  if (banner) {
    return (
      <Box sx={{ px: 2, py: 1.5, borderTop: 1, borderColor: 'divider', bgcolor: 'action.hover' }}>
        <Typography variant="body2" color="text.secondary" role="status">
          {banner}
        </Typography>
      </Box>
    )
  }

  const value = editing ? editText : text
  const setValue = editing ? setEditText : setText
  const trimmed = value.trim()
  const tooLong = value.length > MAX_BODY
  const canSubmit = !disabled && trimmed.length > 0 && !tooLong

  const submit = () => {
    if (!canSubmit) return
    if (editing) {
      onSaveEdit(editing.id, trimmed)
    } else {
      onSend(trimmed)
      setText('')
    }
  }
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      submit()
    } else if (e.key === 'Escape' && editing) {
      e.preventDefault()
      onCancelEdit()
    }
  }

  const context = editing ? { label: t.editing, cancel: t.cancelEdit, onCancel: onCancelEdit, excerpt: undefined }
    : replyTo ? { label: t.replyingTo(replyTo.name), cancel: t.cancelReply, onCancel: onCancelReply, excerpt: replyTo.excerpt }
    : null

  return (
    <Box sx={{ borderTop: 1, borderColor: 'divider', px: 2, py: 1.5 }}>
      {context && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1, pl: 1.5, borderLeft: 3, borderColor: 'primary.main' }}>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Typography variant="caption" color="primary" component="p" sx={{ fontWeight: 600 }}>
              {context.label}
            </Typography>
            {context.excerpt && (
              <Typography variant="caption" color="text.secondary" component="p" noWrap>
                {context.excerpt}
              </Typography>
            )}
          </Box>
          <Tooltip title={context.cancel}>
            <IconButton size="small" onClick={context.onCancel} aria-label={context.cancel}>
              <X size={18} aria-hidden />
            </IconButton>
          </Tooltip>
        </Stack>
      )}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-end' }}>
        <TextField
          fullWidth
          multiline
          maxRows={8}
          size="small"
          label={t.composerLabel}
          placeholder={t.composerPlaceholder}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={onKeyDown}
          disabled={disabled}
          error={tooLong}
          helperText={tooLong ? t.tooLong(MAX_BODY) : undefined}
          slotProps={{ htmlInput: { 'aria-describedby': value.length >= COUNTER_FROM ? counterId : undefined } }}
        />
        {editing ? (
          <Button variant="contained" onClick={submit} disabled={!canSubmit}>
            {t.save}
          </Button>
        ) : (
          <Tooltip title={t.send}>
            <span>
              <IconButton color="primary" onClick={submit} disabled={!canSubmit} aria-label={t.send}>
                <Send size={22} aria-hidden />
              </IconButton>
            </span>
          </Tooltip>
        )}
      </Stack>
      {value.length >= COUNTER_FROM && (
        <Typography id={counterId} variant="caption" color={tooLong ? 'error' : 'text.secondary'} component="p" sx={{ mt: 0.5, textAlign: 'right' }}>
          {t.counter(value.length, MAX_BODY)}
        </Typography>
      )}
    </Box>
  )
}
