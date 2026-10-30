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
import { CaseCode } from '../../components/ui/CaseCode'
import { PageHeader } from '../../components/ui/PageHeader'
import { StatusChip } from '../../components/ui/StatusChip'
import { NotifyNotClosedDialog } from '../../components/registration/NotifyNotClosedDialog'
import { FormSection, RegistrationForm } from '../../components/registration/RegistrationForm'
import {
  useNextLevelPreview,
  useRegisterNextLevel,
  useRegistrationSearch,
  type NextLevelPreview,
  type RegistrationSearchItem,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatTiraneDate } from '../../lib/dates'
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
  const q = useDebounced(serverQuery(search), 300)
  const courts = useCourts()
  const results = useRegistrationSearch(q, courtId)
  const [notifyFor, setNotifyFor] = useState<RegistrationSearchItem | null>(null)

  const items = results.data?.pages.flatMap((p) => p.items) ?? []
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
            options={(courts.data ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
            value={courtId}
            onChange={setCourtId}
            label={t.court}
            helperText={courtId ? undefined : t.allCourts}
          />
        </Box>
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

      {results.hasNextPage && (
        <Box>
          <Button onClick={() => void results.fetchNextPage()} disabled={results.isFetchingNextPage}>
            {sq.common.loadMore}
          </Button>
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
          <Alert
            severity="warning"
            action={
              notClosed && (
                <Button color="inherit" size="small" onClick={onNotify}>
                  {t.notify}
                </Button>
              )
            }
          >
            {item.reason}
          </Alert>
        )}
      </Stack>
    </Paper>
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
