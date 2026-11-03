import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Link as MuiLink,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { OptionPicker } from '../../components/OptionPicker'
import { AppSelect } from '../../components/ui/AppSelect'
import { CaseCode } from '../../components/ui/CaseCode'
import { PaginationFooter } from '../../components/ui/DataTable'
import { PageHeader } from '../../components/ui/PageHeader'
import { StatusChip } from '../../components/ui/StatusChip'
import { NotifyNotClosedDialog } from '../../components/registration/NotifyNotClosedDialog'
import { FormSection, RegistrationForm } from '../../components/registration/RegistrationForm'
import {
  useNextLevelPreview,
  useNotifyRecipients,
  useRegisterNextLevel,
  useRegistrationSearch,
  type NextLevelPreview,
  type RegistrationSearchItem,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatTiraneDate } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { problemMessage } from '../../lib/problem'
import { MIN_SERVER_QUERY, serverQuery } from '../../lib/search'
import { useDebounced } from '../../lib/useDebounced'
import { useUrlSearch } from '../../lib/useUrlSearch'

const t = sq.cases.continue

/**
 * Protokoll continues a closed case at the next level (task 021, part 2): find the dossier (?q= in the URL, so the
 * duplicate dialog of a new registration can open it), then the summons form for X/n+1 (?case=<publicId>).
 */
export function ContinueCasePage() {
  const [params, setParams] = useSearchParams()
  const selected = params.get('case')
  const select = (publicId: string | null) =>
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      if (publicId) next.set('case', publicId)
      else next.delete('case')
      return next
    })

  return selected ? (
    <NextLevel previousCasePublicId={selected} onBack={() => select(null)} />
  ) : (
    <Search onContinue={select} />
  )
}

// ---------------------------------------------------------------- search

function Search({ onContinue }: { onContinue: (publicId: string) => void }) {
  const me = useMe(true)
  const canCreate = me.data?.permissions.includes('case.create') ?? false
  const [search, setSearch] = useUrlSearch()
  const [courtId, setCourtId] = useUrlSearch('court')
  const [status, setStatus] = useUrlSearch('status')
  const q = useDebounced(serverQuery(search), 300)
  const courts = useCourts()
  const paging = useKeysetPaging()
  // every status unless one is chosen: the closed level is the one to continue
  const results = useRegistrationSearch(q, courtId, status, { cursor: paging.cursor, size: paging.size })
  const [notifyFor, setNotifyFor] = useState<RegistrationSearchItem | null>(null)

  const items = results.data?.items ?? []
  const searched = q.length >= MIN_SERVER_QUERY

  return (
    <Stack spacing={3}>
      <PageHeader title={t.title} />
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <TextField
          label={t.search}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          autoFocus
          sx={{ flex: 2 }}
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
        <Box sx={{ flex: 1 }}>
          <OptionPicker
            filter
            options={(courts.data ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
            value={courtId}
            onChange={setCourtId}
            label={t.court}
          />
        </Box>
        <AppSelect
          filter
          label={t.status}
          value={status}
          onChange={setStatus}
          options={Object.entries(t.statuses).map(([value, label]) => ({ value, label }))}
          sx={{ width: { xs: '100%', md: 200 } }}
        />
      </Stack>

      {!searched && <Typography color="text.secondary">{t.typeToSearch}</Typography>}
      {searched && results.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {results.isError && <Alert severity="error">{problemMessage(results.error)}</Alert>}

      {searched && results.isSuccess && items.length === 0 && (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Typography>{sq.search.noResults(search.trim())}</Typography>
          <Stack direction="row" spacing={2} sx={{ mt: 1, alignItems: 'center' }}>
            <Button size="small" onClick={() => setSearch('')}>
              {sq.search.clear}
            </Button>
            {canCreate && (
              <MuiLink component={Link} to="/cases/register">
                {t.registerNew}
              </MuiLink>
            )}
          </Stack>
        </Paper>
      )}

      {searched && items.length > 0 && (
        <Box>
          <Stack spacing={2} component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {items.map((item) => (
              <ResultCard
                key={item.casePublicId}
                item={item}
                onContinue={() => onContinue(item.casePublicId)}
                onNotify={() => setNotifyFor(item)}
              />
            ))}
          </Stack>
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={items.length}
            total={Number(results.data?.total ?? 0)}
            nextCursor={results.data?.nextCursor}
            busy={results.isFetching}
            stale={results.isPlaceholderData}
          />
        </Box>
      )}

      {notifyFor && (
        <NotifyNotClosedDialog
          previousCasePublicId={notifyFor.casePublicId}
          caseCode={notifyFor.caseCode}
          onClose={() => setNotifyFor(null)}
        />
      )}
    </Stack>
  )
}

function ResultCard({
  item,
  onContinue,
  onNotify,
}: {
  item: RegistrationSearchItem
  onContinue: () => void
  onNotify: () => void
}) {
  const hiddenParties = Number(item.partyCount) - item.partyNames.length
  // "E pambyllur në sistem": the level is still open — the lawyer has to record the decision first
  const notClosed = !item.canContinue && item.status !== 'CLOSED'
  return (
    <Paper component="li" variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={1}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="h3" component="h2">
            <CaseCode code={item.caseCode} />
          </Typography>
          <StatusChip kind="status" value={item.status} />
          <Typography color="text.secondary">
            {item.closedAt ? `${t.closedAt} ${formatTiraneDate(item.closedAt)}` : t.notClosed}
          </Typography>
        </Stack>
        <Typography>
          {t.actNumber}: {item.actNumber} · {item.courtName}
        </Typography>
        <Typography>
          {t.parties}: {item.partyNames.join(', ')}
          {hiddenParties > 0 && ` ${t.moreParties(hiddenParties)}`}
        </Typography>
        {item.canContinue ? (
          <Box>
            <Button variant="contained" onClick={onContinue}>
              {t.continueButton}
            </Button>
          </Box>
        ) : (
          <Alert severity="warning" action={notClosed && <NotifyButton caseId={item.casePublicId} onClick={onNotify} />}>
            {item.reason}
          </Alert>
        )}
      </Stack>
    </Paper>
  )
}

/** "Njofto avokatin dhe Kryetarin" when the case has a primary lawyer, "Njofto Kryetarin" when not (the API tells). */
function NotifyButton({ caseId, onClick }: { caseId: string; onClick: () => void }) {
  const recipients = useNotifyRecipients(caseId)
  return (
    <Button color="inherit" size="small" onClick={onClick} disabled={!recipients.data}>
      {t.notify(recipients.data?.hasPrimaryLawyer ?? false)}
    </Button>
  )
}

// ---------------------------------------------------------------- next level

function NextLevel({ previousCasePublicId, onBack }: { previousCasePublicId: string; onBack: () => void }) {
  const preview = useNextLevelPreview(previousCasePublicId)
  const register = useRegisterNextLevel(previousCasePublicId)

  const back = (
    <Box>
      <Button onClick={onBack}>{t.back}</Button>
    </Box>
  )

  if (preview.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (preview.isError) {
    // 409: already continued, archived or not closed — the search shows the current state
    return (
      <Stack spacing={2}>
        <Alert severity="warning">{problemMessage(preview.error)}</Alert>
        {back}
      </Stack>
    )
  }

  return (
    <Stack spacing={2}>
      {back}
      <RegistrationForm
        mode="next"
        title={t.nextTitle(preview.data.caseCode)}
        above={<PreviewPanel preview={preview.data} />}
        send={(request) => register.mutateAsync(request)}
        anotherLabel={t.back}
        onAnother={onBack}
      />
    </Stack>
  )
}

function PreviewPanel({ preview }: { preview: NextLevelPreview }) {
  return (
    <FormSection title={t.previewTitle}>
      <Typography>
        <strong>{t.previous}:</strong> {preview.caseCode} · {preview.courtLevelName} · {preview.courtName}
      </Typography>
      <Box>
        <Typography variant="h5" component="h3">
          {t.object}
        </Typography>
        <Typography sx={{ whiteSpace: 'pre-wrap' }}>{preview.objectText}</Typography>
      </Box>
      <Box>
        <Typography variant="h5" component="h3">
          {t.parties}
        </Typography>
        {preview.parties.length === 0 ? (
          <Typography color="text.secondary">{t.noParties}</Typography>
        ) : (
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {preview.parties.map((p, i) => (
              <li key={`${p.displayName}-${i}`}>
                {p.role} — {p.displayName} ({sq.partyEditor.kinds[p.kind] ?? p.kind}
                {p.identifier ? `, ${sq.partyEditor.nipt} ${p.identifier}` : ''})
              </li>
            ))}
          </Box>
        )}
      </Box>
      {preview.institutionLinks.length > 0 && (
        <Box>
          <Typography variant="h5" component="h3">
            {t.links}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {preview.institutionLinks.map((l, i) => (
              <li key={`${l.institution}-${i}`}>
                {l.institution} ({t.modes[l.mode] ?? l.mode})
              </li>
            ))}
          </Box>
          {preview.institutionLinks.some((l) => l.mode === 'DELEGATED') && (
            <Typography variant="body2" color="text.secondary">
              {t.delegationNote}
            </Typography>
          )}
        </Box>
      )}
    </FormSection>
  )
}
