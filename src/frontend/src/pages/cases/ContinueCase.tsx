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
import { JudgePicker, type PickedJudge } from '../../components/JudgePicker'
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
  useRegistrationFilters,
  useRegistrationSearch,
  type NextLevelPreview,
  type RegistrationFilter,
  type RegistrationFilterName,
  type RegistrationFilterValues,
  type RegistrationSearchItem,
} from '../../features/cases/api'
import { useMe } from '../../features/identity/api'
import { useCaseCategories, useCaseSubcategories, useCourts, useDecisionResults } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { formatTiraneDate } from '../../lib/dates'
import { useKeysetPaging } from '../../lib/paging'
import { problemMessage } from '../../lib/problem'
import { serverQuery } from '../../lib/search'
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

/** URL parameters that only label a chosen value (not sent to the API). */
const JUDGE_NAME = 'judgeName'

function Search({ onContinue }: { onContinue: (publicId: string) => void }) {
  const me = useMe(true)
  const canCreate = me.data?.permissions.includes('case.create') ?? false
  const [params, setParams] = useSearchParams()
  const [status, setStatus] = useUrlSearch('status')
  const filters = useRegistrationFilters()
  const fields = filters.data ?? []

  // every filter lives in the URL under its own name (?actNumber=… opens from the duplicate dialog)
  const raw: RegistrationFilterValues = Object.fromEntries(fields.map((f) => [f.name, params.get(f.name) ?? '']))
  const set = (changes: Record<string, string>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [key, value] of Object.entries(changes)) {
          if (value) next.set(key, value)
          else next.delete(key)
        }
        return next
      },
      { replace: true },
    )
  // a filter's value goes, and so do the values that depend on it (court → judge, category → subcategory)
  const change = (name: string, value: string) => {
    const changes: Record<string, string> = { [name]: value }
    for (const f of fields) {
      if (f.dependsOn !== name) continue
      changes[f.name] = ''
      if (f.input === 'Judge') changes[JUDGE_NAME] = ''
    }
    set(changes)
  }
  const clear = () => set(Object.fromEntries([...fields.map((f) => [f.name, '']), [JUDGE_NAME, '']]))

  // texts count from 2 characters, after a pause; chosen values at once
  // (debounced as a string: `raw` is a new object on every render)
  const typed = JSON.parse(useDebounced(JSON.stringify(raw), 300)) as RegistrationFilterValues
  const values: RegistrationFilterValues = Object.fromEntries(
    fields.map((f) => {
      const key = f.name as RegistrationFilterName
      return [f.name, f.input === 'Text' ? serverQuery(typed[key] ?? '') : (raw[key] ?? '')]
    }),
  )
  const searched = Object.values(values).some((v) => v)
  const paging = useKeysetPaging()
  // every status unless one is chosen: the closed level is the one to continue
  const results = useRegistrationSearch(fields, values, status, { cursor: paging.cursor, size: paging.size })
  const [notifyFor, setNotifyFor] = useState<RegistrationSearchItem | null>(null)

  const items = results.data?.items ?? []

  return (
    <Stack spacing={3}>
      <PageHeader title={t.title} />
      {filters.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {filters.isError && <Alert severity="error">{problemMessage(filters.error)}</Alert>}
      <Box
        sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: 'repeat(3, 1fr)' } }}
        role="search"
      >
        {fields.map((f, i) => (
          <FilterField
            key={f.name}
            filter={f}
            value={raw[f.name as RegistrationFilterName] ?? ''}
            parentValue={f.dependsOn ? (raw[f.dependsOn as RegistrationFilterName] ?? '') : ''}
            judgeName={params.get(JUDGE_NAME) ?? ''}
            onChange={(value) => change(f.name, value)}
            onJudge={(judge) => set({ judgeId: judge?.id ?? '', [JUDGE_NAME]: judge?.name ?? '' })}
            autoFocus={i === 0}
          />
        ))}
        <AppSelect
          filter
          label={t.status}
          value={status}
          onChange={setStatus}
          options={Object.entries(t.statuses).map(([value, label]) => ({ value, label }))}
        />
      </Box>
      {searched && (
        <Box>
          <Button size="small" onClick={clear}>
            {t.clearFilters}
          </Button>
        </Box>
      )}

      {!searched && <Typography color="text.secondary">{t.typeToSearch}</Typography>}
      {searched && results.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {results.isError && <Alert severity="error">{problemMessage(results.error)}</Alert>}

      {searched && results.isSuccess && items.length === 0 && (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Typography>{t.noResults}</Typography>
          <Stack direction="row" spacing={2} sx={{ mt: 1, alignItems: 'center' }}>
            <Button size="small" onClick={clear}>
              {t.clearFilters}
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

/**
 * One search field, by the control the server names for it (filter.input). A new filter of an existing input needs
 * only its label in sq.cases.continue.filters; a new input needs a case here (docs/architecture.md).
 */
function FilterField({
  filter,
  value,
  parentValue,
  judgeName,
  onChange,
  onJudge,
  autoFocus,
}: {
  filter: RegistrationFilter
  value: string
  /** the value of filter.dependsOn (the court of a judge, the category of a subcategory) */
  parentValue: string
  judgeName: string
  onChange: (value: string) => void
  onJudge: (judge: PickedJudge | null) => void
  autoFocus: boolean
}) {
  const label = t.filters[filter.name] ?? filter.name
  const courts = useCourts()
  const categories = useCaseCategories()
  const subcategories = useCaseSubcategories()
  const results = useDecisionResults()
  const courtName = (id: string | null) => courts.data?.find((c) => String(c.id) === id)?.name

  switch (filter.input) {
    case 'Court':
      return (
        <OptionPicker
          filter
          options={(courts.data ?? []).map((c) => ({ value: String(c.id), label: c.name }))}
          value={value}
          onChange={onChange}
          label={label}
        />
      )
    case 'Judge':
      return (
        <JudgePicker
          filter
          courtId={parentValue ? Number(parentValue) : null}
          showAll={!parentValue}
          courtName={courtName}
          value={value ? { id: value, name: judgeName, courtId: parentValue || null } : null}
          onChange={onJudge}
          label={label}
        />
      )
    case 'Category':
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          options={(categories.data ?? []).map((c) => ({ value: c.code, label: c.name }))}
        />
      )
    case 'Subcategory': {
      const own = (subcategories.data ?? []).filter((s) => s.categoryCode === parentValue)
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          disabled={own.length === 0}
          options={own.map((s) => ({ value: s.code, label: s.name }))}
        />
      )
    }
    case 'DecisionResult':
      return (
        <AppSelect
          filter
          label={label}
          value={value}
          onChange={onChange}
          options={(results.data ?? []).map((r) => ({ value: r.code, label: r.name }))}
        />
      )
    default:
      return (
        <TextField
          label={label}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          autoFocus={autoFocus}
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
      )
  }
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
        {item.objectSnippet && (
          <Typography color="text.secondary">
            {t.object}: {item.objectSnippet}
          </Typography>
        )}
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
        </Box>
      )}
      <Typography variant="body2" color="text.secondary">
        {t.linksNote}
      </Typography>
    </FormSection>
  )
}
