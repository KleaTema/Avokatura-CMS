import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Paper,
  Stack,
  Typography,
} from '@mui/material'
import { UserPlus, Users } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { OptionPicker } from '../../components/OptionPicker'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { SearchField } from '../../components/ui/SearchField'
import { SuccessToast } from '../../components/ui/SuccessToast'
import {
  useAssistantLinks,
  useLinkAssistant,
  useUnlinkAssistant,
  type AssistantLink,
  type OfficeAssistant,
} from '../../features/assistants/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { matches } from '../../lib/search'

const t = sq.assistantLinks

/**
 * "Ndihmësit ligjorë" (task 053e, client decision C, default D4): the Kryetar links each legal assistant of the office
 * to the lawyers they help. The assistant then sees every case where such a lawyer is the current primary; the
 * per-case "Shto ndihmës" of task 026 is gone.
 */
export function AssistantsPage() {
  const links = useAssistantLinks()
  const [adding, setAdding] = useState<OfficeAssistant | null>(null)
  const [removing, setRemoving] = useState<{ assistant: OfficeAssistant; link: AssistantLink } | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  // live search (the list is loaded whole): the assistant's or a linked lawyer's name; the text in the URL (?q=)
  const [params, setParams] = useSearchParams()
  const text = params.get('q') ?? ''
  const setText = (value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set('q', value)
        else next.delete('q')
        return next
      },
      { replace: true },
    )
  const all = links.data?.assistants ?? []
  const assistants = all.filter((a) => matches(text, a.name, ...a.lawyers.map((l) => l.lawyerName)))

  return (
    <Stack spacing={3}>
      <PageHeader title={t.title} />
      <Typography color="text.secondary">{t.intro}</Typography>
      {all.length > 0 && (
        <Box sx={{ maxWidth: 360 }}>
          <SearchField label={t.search} value={text} onChange={setText} />
        </Box>
      )}
      {links.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {links.isError && <Alert severity="error">{problemMessage(links.error)}</Alert>}
      {links.isSuccess && all.length === 0 && <EmptyState icon={Users} title={t.empty} />}
      {all.length > 0 && assistants.length === 0 && <Typography color="text.secondary">{sq.search.noResults(text.trim())}</Typography>}
      <Stack spacing={2} component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }}>
        {assistants.map((a) => (
          <Paper key={a.publicId} component="li" variant="outlined" sx={{ p: 2 }}>
            <Stack spacing={1.5}>
              <Stack direction="row" spacing={2} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <Typography variant="h3" component="h2" sx={{ flex: 1, minWidth: 200 }}>
                  {a.name}
                </Typography>
                <Button
                  variant="outlined"
                  size="small"
                  startIcon={<UserPlus size={18} aria-hidden />}
                  onClick={() => setAdding(a)}
                  aria-label={t.addFor(a.name)}
                >
                  {t.add}
                </Button>
              </Stack>
              {a.lawyers.length === 0 ? (
                <Typography color="text.secondary">{t.noLinks}</Typography>
              ) : (
                <Box component="ul" aria-label={`${t.lawyers}: ${a.name}`} sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, p: 0, m: 0, listStyle: 'none' }}>
                  {a.lawyers.map((l) => (
                    <li key={l.linkPublicId}>
                      <Chip
                        label={`${l.lawyerName} · ${t.cases(Number(l.cases))}`}
                        onDelete={() => setRemoving({ assistant: a, link: l })}
                        slotProps={{ root: { 'aria-label': `${l.lawyerName} · ${t.cases(Number(l.cases))}` } }}
                      />
                    </li>
                  ))}
                </Box>
              )}
            </Stack>
          </Paper>
        ))}
      </Stack>

      {adding && (
        <LinkDialog
          assistant={adding}
          lawyers={(links.data?.lawyers ?? []).filter((l) => !adding.lawyers.some((x) => x.lawyerPublicId === l.publicId))}
          onClose={() => setAdding(null)}
          onLinked={(message) => {
            setAdding(null)
            setToast(message)
          }}
        />
      )}
      {removing && (
        <UnlinkDialog
          assistant={removing.assistant}
          link={removing.link}
          onClose={() => setRemoving(null)}
          onRemoved={() => {
            setRemoving(null)
            setToast(t.removed)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

function LinkDialog({
  assistant,
  lawyers,
  onClose,
  onLinked,
}: {
  assistant: OfficeAssistant
  lawyers: { publicId: string; name: string }[]
  onClose: () => void
  onLinked: (message: string) => void
}) {
  const link = useLinkAssistant()
  const [lawyer, setLawyer] = useState('')
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    setError(null)
    try {
      const created = await link.mutateAsync({ assistantUserPublicId: assistant.publicId, lawyerUserPublicId: lawyer })
      onLinked(t.linked(created.lawyerName, Number(created.cases)))
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="link-assistant-title">
      <DialogTitle id="link-assistant-title">{t.addTitle(assistant.name)}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          {lawyers.length === 0 ? (
            <Alert severity="info">{t.noLawyers}</Alert>
          ) : (
            <OptionPicker
              required
              label={t.lawyer}
              options={lawyers.map((l) => ({ value: l.publicId, label: l.name }))}
              value={lawyer}
              onChange={setLawyer}
              autoFocus
            />
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" color="secondary" onClick={() => void save()} disabled={!lawyer || link.isPending}>
          {t.confirm}
        </Button>
      </DialogActions>
    </Dialog>
  )
}

function UnlinkDialog({
  assistant,
  link,
  onClose,
  onRemoved,
}: {
  assistant: OfficeAssistant
  link: AssistantLink
  onClose: () => void
  onRemoved: () => void
}) {
  const unlink = useUnlinkAssistant()
  const [error, setError] = useState<string | null>(null)
  const confirm = async () => {
    setError(null)
    try {
      await unlink.mutateAsync(link.linkPublicId)
      onRemoved()
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="unlink-assistant-title">
      <DialogTitle id="unlink-assistant-title">{t.removeTitle}</DialogTitle>
      <DialogContent>
        {error && <Alert severity="error">{error}</Alert>}
        <DialogContentText>{t.removeText(assistant.name, link.lawyerName)}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button
          variant="contained"
          color="error"
          onClick={() => void confirm()}
          disabled={unlink.isPending}
          aria-label={t.removeFor(assistant.name, link.lawyerName)}
        >
          {t.remove}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
