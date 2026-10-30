import { Alert, Box, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { useEffect, useRef, type KeyboardEvent } from 'react'
import { useInstitutionSearch } from '../features/admin/api'
import { usePartyRoles } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { serverQuery } from '../lib/search'
import { useDebounced } from '../lib/useDebounced'
import { newDraftParty, PARTY_KINDS, type DraftParty, type DraftPartyErrors, type PartyKind } from './draftParty'
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

function PartyRow({
  index,
  party,
  roles,
  errors,
  firstFieldRef,
  onChange,
  onRemove,
}: {
  index: number
  party: DraftParty
  roles: { code: string; name: string }[]
  errors: DraftPartyErrors | undefined
  firstFieldRef: (node: Focusable | null) => void
  onChange: (patch: Partial<DraftParty>) => void
  onRemove: () => void
}) {
  const typesName = party.kind === 'NATURAL' || party.kind === 'LEGAL'
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
          {party.kind === 'INSTITUTION' ? (
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
        <Box>
          <Button color="error" size="small" onClick={onRemove} aria-label={`${t.remove}: ${rowLabel}`}>
            {t.remove}
          </Button>
        </Box>
      </Stack>
    </Paper>
  )
}
