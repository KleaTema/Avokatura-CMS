import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { ApiError } from '../api/client'
import { useInstitutionSearch } from '../features/admin/api'
import { useAddParty, useRemoveParty, useUpdateParty, type PartyResponse } from '../features/cases/api'
import { usePartyRoles } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { serverQuery } from '../lib/search'
import { useDebounced } from '../lib/useDebounced'
import {
  draftPartySchema,
  newDraftParty,
  PARTY_KINDS,
  partyFieldFromServer,
  toPartyRequest,
  type DraftParty,
  type DraftPartyErrors,
  type PartyKind,
} from './draftParty'
import { InstitutionPicker } from './InstitutionPicker'

const t = sq.partyEditor

interface Focusable {
  focus: () => void
}

/**
 * The parties of a case being registered (DRAFT: a local list, nothing is saved until the form is sent).
 * "Shto palë" adds a row and focuses its role; Enter inside the rows never submits the form.
 */
export function PartyEditor({
  value,
  onChange,
  rowErrors,
  error,
}: {
  value: DraftParty[]
  onChange: (parties: DraftParty[]) => void
  rowErrors: (DraftPartyErrors | undefined)[]
  error?: string
}) {
  const roles = usePartyRoles()
  const firstFields = useRef(new Map<string, Focusable>())
  // the row added by "Shto palë": focused once it has rendered
  const pendingFocus = useRef<string | null>(null)

  useEffect(() => {
    if (pendingFocus.current === null) return
    firstFields.current.get(pendingFocus.current)?.focus()
    pendingFocus.current = null
  }, [value])

  const update = (key: string, patch: Partial<DraftParty>) =>
    onChange(value.map((p) => (p.key === key ? { ...p, ...patch } : p)))

  const add = () => {
    const party = newDraftParty()
    pendingFocus.current = party.key
    onChange([...value, party])
  }

  // Enter in a text field of a row would submit the whole form; Ctrl+Enter (submit) still bubbles up.
  const blockEnter = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && !e.ctrlKey && !e.metaKey && e.target instanceof HTMLInputElement) e.preventDefault()
  }

  return (
    <Stack spacing={2} onKeyDown={blockEnter}>
      {error && <Alert severity="error">{error}</Alert>}
      {value.length === 0 && <Typography color="text.secondary">{t.empty}</Typography>}
      {value.map((party, index) => (
        <PartyRow
          key={party.key}
          index={index}
          party={party}
          roles={roles.data ?? []}
          errors={rowErrors[index]}
          firstFieldRef={(node) => {
            if (node) firstFields.current.set(party.key, node)
            else firstFields.current.delete(party.key)
          }}
          onChange={(patch) => update(party.key, patch)}
          onRemove={() => onChange(value.filter((p) => p.key !== party.key))}
        />
      ))}
      <Box>
        <Button variant="outlined" onClick={add}>
          {t.add}
        </Button>
      </Box>
    </Stack>
  )
}

/**
 * The fields of one party. `kindLocked`: a saved party — kind and institution cannot change (delete and add again).
 * `footer` replaces the DRAFT "remove" button (LIVE mode: save/cancel).
 */
function PartyRow({
  index,
  party,
  roles,
  errors,
  firstFieldRef,
  onChange,
  onRemove,
  kindLocked,
  footer,
}: {
  index: number
  party: DraftParty
  roles: { code: string; name: string }[]
  errors: DraftPartyErrors | undefined
  firstFieldRef: (node: Focusable | null) => void
  onChange: (patch: Partial<DraftParty>) => void
  onRemove?: () => void
  kindLocked?: boolean
  footer?: ReactNode
}) {
  const typesName = !kindLocked && (party.kind === 'NATURAL' || party.kind === 'LEGAL')
  // a typed name that matches a state institution: suggest picking it from the list instead
  const nameQuery = useDebounced(typesName ? serverQuery(party.displayName) : '', 400)
  const similar = useInstitutionSearch(nameQuery, nameQuery.length >= 3)
  const suggestion = typesName && nameQuery.length >= 3 ? similar.data?.[0] : undefined

  const changeKind = (kind: PartyKind) =>
    onChange(
      kind === 'INSTITUTION'
        ? { kind, displayName: '', identifier: '' }
        : { kind, institution: null, identifier: kind === 'LEGAL' ? party.identifier : '' },
    )

  const rowLabel = t.row(index + 1)
  return (
    <Paper variant="outlined" sx={{ p: 2 }} role="group" aria-label={rowLabel}>
      <Stack spacing={2}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <TextField
            select
            label={t.role}
            required
            value={party.partyRoleCode}
            onChange={(e) => onChange({ partyRoleCode: e.target.value })}
            inputRef={firstFieldRef}
            error={!!errors?.partyRoleCode}
            helperText={errors?.partyRoleCode}
            sx={{ minWidth: 180 }}
          >
            {roles.map((r) => (
              <MenuItem key={r.code} value={r.code}>
                {r.name}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            select
            label={t.kind}
            required
            value={party.kind}
            onChange={(e) => changeKind(e.target.value as PartyKind)}
            disabled={kindLocked}
            error={!!errors?.kind}
            helperText={errors?.kind}
            sx={{ minWidth: 180 }}
          >
            {PARTY_KINDS.map((kind) => (
              <MenuItem key={kind} value={kind}>
                {t.kinds[kind]}
              </MenuItem>
            ))}
          </TextField>
          {party.kind === 'INSTITUTION' && kindLocked ? (
            <TextField label={t.institution} value={party.institution?.name ?? ''} disabled sx={{ flex: 1 }} />
          ) : party.kind === 'INSTITUTION' ? (
            <Box sx={{ flex: 1 }}>
              <InstitutionPicker
                value={party.institution}
                onChange={(institution) => onChange({ institution })}
                label={t.institution}
                required
                error={!!errors?.institution}
                helperText={errors?.institution}
              />
            </Box>
          ) : (
            <TextField
              label={t.name}
              required
              value={party.displayName}
              onChange={(e) => onChange({ displayName: e.target.value })}
              disabled={party.kind === ''}
              error={!!errors?.displayName}
              helperText={errors?.displayName}
              slotProps={{ htmlInput: { maxLength: 300 } }}
              sx={{ flex: 1 }}
            />
          )}
          {party.kind === 'LEGAL' && (
            <TextField
              label={t.nipt}
              value={party.identifier}
              onChange={(e) => onChange({ identifier: e.target.value.toUpperCase() })}
              error={!!errors?.identifier}
              helperText={errors?.identifier ?? t.niptHint}
              slotProps={{ htmlInput: { maxLength: 30 } }}
              sx={{ minWidth: 160 }}
            />
          )}
        </Stack>
        {suggestion && (
          <Alert
            severity="info"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() =>
                  onChange({
                    kind: 'INSTITUTION',
                    institution: { publicId: suggestion.publicId, name: suggestion.name },
                    displayName: '',
                    identifier: '',
                  })
                }
              >
                {t.useInstitution}
              </Button>
            }
          >
            {t.institutionHint(suggestion.name)}
          </Alert>
        )}
        {footer ??
          (onRemove && (
            <Box>
              <Button color="error" size="small" onClick={onRemove} aria-label={`${t.remove}: ${rowLabel}`}>
                {t.remove}
              </Button>
            </Box>
          ))}
      </Stack>
    </Paper>
  )
}

// ---------------------------------------------------------------- LIVE mode (case page)

const p = sq.cases.page.parties

const toDraft = (party: PartyResponse): DraftParty => ({
  key: party.publicId,
  partyRoleCode: party.role.code,
  kind: party.kind as PartyKind,
  displayName: party.kind === 'INSTITUTION' ? '' : party.displayName,
  identifier: party.identifier ?? '',
  institution: party.institution,
})

/** Server field errors of a single-party request (no "parties[i]." prefix) → the row's fields. */
function rowErrorsOf(error: unknown): { fields: DraftPartyErrors; other: string | null } {
  if (!(error instanceof ApiError) || error.status !== 400 || !error.problem?.errors) {
    return { fields: {}, other: problemMessage(error) }
  }
  const fields: DraftPartyErrors = {}
  const other: string[] = []
  for (const [key, messages] of Object.entries(error.problem.errors)) {
    const field = partyFieldFromServer(key)
    if (field) fields[field] = messages.join(' ')
    else other.push(...messages)
  }
  return { fields, other: other.length > 0 ? other.join(' ') : null }
}

/**
 * The parties of an existing case (LIVE: every change is saved at once through the task 017 endpoints).
 * Add / edit / remove appear only when party.add / party.edit / party.delete is in `permissions`.
 * Removing an institution's last party asks first whether the institution's link with the case ends too.
 */
export function LivePartyEditor({
  casePublicId,
  parties,
  permissions,
}: {
  casePublicId: string
  parties: PartyResponse[]
  permissions: string[]
}) {
  const roles = usePartyRoles()
  const add = useAddParty(casePublicId)
  const update = useUpdateParty(casePublicId)
  const remove = useRemoveParty(casePublicId)
  const canAdd = permissions.includes('party.add')
  const canEdit = permissions.includes('party.edit')
  const canDelete = permissions.includes('party.delete')

  // one row is edited at a time: an existing party (its public id) or a new one ('new')
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState<DraftParty>(newDraftParty)
  const [errors, setErrors] = useState<DraftPartyErrors>({})
  const [message, setMessage] = useState<{ severity: 'error' | 'success' | 'warning'; text: string } | null>(null)
  const [removing, setRemoving] = useState<PartyResponse | null>(null)
  const busy = add.isPending || update.isPending || remove.isPending
  const firstField = useRef<Focusable | null>(null)

  const start = (key: string, party: DraftParty) => {
    setEditing(key)
    setDraft(party)
    setErrors({})
    setMessage(null)
  }
  const stop = () => {
    setEditing(null)
    setErrors({})
  }

  useEffect(() => {
    if (editing !== null) firstField.current?.focus()
  }, [editing])

  const save = async () => {
    const checked = draftPartySchema.safeParse(draft)
    if (!checked.success) {
      const fields: DraftPartyErrors = {}
      for (const issue of checked.error.issues) {
        const field = issue.path[0]
        if (typeof field === 'string' && field !== 'key') fields[field as keyof DraftPartyErrors] = issue.message
      }
      setErrors(fields)
      return
    }
    try {
      if (editing === 'new') await add.mutateAsync(toPartyRequest(draft))
      else if (editing) await update.mutateAsync({ partyId: editing, body: toPartyRequest(draft) })
      stop()
    } catch (error) {
      const { fields, other } = rowErrorsOf(error)
      setErrors(fields)
      setMessage(other ? { severity: 'error', text: other } : null)
    }
  }

  const confirmRemove = async (endInstitutionLink?: boolean) => {
    if (!removing) return
    try {
      const result = await remove.mutateAsync({ partyId: removing.publicId, endInstitutionLink })
      const warning = result.warnings[0]?.message
      if (warning) setMessage({ severity: 'warning', text: warning }) // e.g. a DELEGATED link is kept
      else if (result.lastInstitutionParty) {
        setMessage({ severity: 'success', text: result.institutionLinkEnded ? p.linkEnded : p.linkKept })
      } else setMessage(null)
    } catch (error) {
      setMessage({ severity: 'error', text: problemMessage(error) })
    } finally {
      setRemoving(null)
    }
  }

  // the institution's last party: no other party of the case has the same institution
  const isLastOfInstitution = (party: PartyResponse) =>
    party.institution !== null &&
    !parties.some((x) => x.publicId !== party.publicId && x.institution?.publicId === party.institution?.publicId)

  const editRow = (key: string, index: number) => (
    <PartyRow
      key={key}
      index={index}
      party={draft}
      roles={roles.data ?? []}
      errors={errors}
      firstFieldRef={(node) => {
        firstField.current = node
      }}
      onChange={(patch) => setDraft((d) => ({ ...d, ...patch }))}
      kindLocked={key !== 'new'}
      footer={
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Button variant="contained" size="small" onClick={() => void save()} disabled={busy}>
            {p.save}
          </Button>
          <Button size="small" onClick={stop} disabled={busy}>
            {p.cancel}
          </Button>
          {key !== 'new' && (
            <Typography variant="body2" color="text.secondary">
              {p.kindLocked}
            </Typography>
          )}
        </Stack>
      }
    />
  )

  return (
    <Stack spacing={2}>
      {message && (
        <Alert severity={message.severity} onClose={() => setMessage(null)}>
          {message.text}
        </Alert>
      )}
      {parties.length === 0 && editing !== 'new' && <Typography color="text.secondary">{p.none}</Typography>}
      {parties.map((party, index) =>
        editing === party.publicId ? (
          editRow(party.publicId, index)
        ) : (
          <Paper key={party.publicId} variant="outlined" sx={{ p: 2 }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
              <Box sx={{ flex: 1 }}>
                <Typography sx={{ fontWeight: 600 }}>{party.displayName}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {party.role.name} · {t.kinds[party.kind] ?? party.kind}
                  {party.identifier ? ` · ${t.nipt} ${party.identifier}` : ''}
                </Typography>
              </Box>
              {canEdit && (
                <Button size="small" onClick={() => start(party.publicId, toDraft(party))} disabled={editing !== null}>
                  {p.edit}
                </Button>
              )}
              {canDelete && (
                <Button size="small" color="error" onClick={() => setRemoving(party)} disabled={editing !== null || busy}>
                  {p.remove}
                </Button>
              )}
            </Stack>
          </Paper>
        ),
      )}
      {editing === 'new' && editRow('new', parties.length)}
      {canAdd && editing === null && (
        <Box>
          <Button variant="outlined" onClick={() => start('new', newDraftParty())}>
            {p.add}
          </Button>
        </Box>
      )}

      {removing && (
        <Dialog open onClose={() => setRemoving(null)} aria-labelledby="remove-party-title">
          <DialogTitle id="remove-party-title">{p.confirmRemoveTitle}</DialogTitle>
          <DialogContent>
            <DialogContentText>{p.confirmRemove(removing.displayName)}</DialogContentText>
            {isLastOfInstitution(removing) && (
              <DialogContentText sx={{ mt: 2 }}>
                {p.endLinkHint} <strong>{p.endLinkQuestion}</strong>
              </DialogContentText>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setRemoving(null)} autoFocus>
              {p.cancel}
            </Button>
            {isLastOfInstitution(removing) ? (
              <>
                <Button color="error" onClick={() => void confirmRemove(false)} disabled={busy}>
                  {sq.cases.page.no}
                </Button>
                <Button color="error" variant="contained" onClick={() => void confirmRemove(true)} disabled={busy}>
                  {sq.cases.page.yes}
                </Button>
              </>
            ) : (
              <Button color="error" variant="contained" onClick={() => void confirmRemove()} disabled={busy}>
                {p.remove}
              </Button>
            )}
          </DialogActions>
        </Dialog>
      )}
    </Stack>
  )
}
