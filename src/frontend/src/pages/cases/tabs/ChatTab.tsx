import { Alert, CircularProgress, Stack, Typography } from '@mui/material'
import { ApiError } from '../../../api/client'
import { SectionCard } from '../../../components/ui/SectionCard'
import { useCaseConversation } from '../../../features/chat/api'
import { ChatThread } from '../../../features/chat/ChatThread'
import { ParticipantsButton } from '../../../features/chat/ParticipantsButton'
import { sq } from '../../../i18n/sq'
import { problemMessage } from '../../../lib/problem'

const t = sq.chat

/**
 * The case's "Chat" tab (task 050): the same thread as the chat page, for this case's conversation — opened (and
 * created on first use) through POST /api/chat/cases/{id}/conversation. Shown only with chat.case in permissions.
 */
export function ChatTab({ casePublicId }: { casePublicId: string }) {
  const conversation = useCaseConversation(casePublicId, true)

  if (conversation.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (conversation.isError) {
    const forbidden = conversation.error instanceof ApiError && conversation.error.status === 403
    return <Alert severity={forbidden ? 'warning' : 'error'}>{forbidden ? t.notAllowed : problemMessage(conversation.error)}</Alert>
  }

  const c = conversation.data
  return (
    <SectionCard
      padded={false}
      title={
        <Stack component="span" direction="row" spacing={1} sx={{ alignItems: 'baseline' }}>
          <span>{t.title}</span>
          {c.isReadOnly && (
            <Typography component="span" variant="caption" color="text.secondary">
              {t.readOnly}
            </Typography>
          )}
        </Stack>
      }
      actions={<ParticipantsButton conversationId={c.id} />}
    >
      <ChatThread key={c.id} conversationId={c.id} readOnly={c.isReadOnly} height={{ xs: '70dvh', md: '65dvh' }} />
    </SectionCard>
  )
}
