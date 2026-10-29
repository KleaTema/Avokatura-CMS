import { Autocomplete, TextField } from '@mui/material'
import { useState } from 'react'
import { MIN_INSTITUTION_QUERY, useInstitutionSearch } from '../features/admin/api'
import { sq } from '../i18n/sq'
import { useDebounced } from '../lib/useDebounced'

export interface PickedInstitution {
  publicId: string
  name: string
}

const t = sq.institutionPicker

/**
 * Type-ahead over active institutions (GET /api/identity/institutions). `onlyType` keeps one institution type,
 * e.g. MINISTRY for a parent; `excludeId` hides the institution being edited.
 */
export function InstitutionPicker({
  value,
  onChange,
  label,
  required,
  error,
  helperText,
  onlyType,
  excludeId,
}: {
  value: PickedInstitution | null
  onChange: (value: PickedInstitution | null) => void
  label: string
  required?: boolean
  error?: boolean
  helperText?: string
  onlyType?: string
  excludeId?: string
}) {
  const [input, setInput] = useState(value?.name ?? '')
  const q = useDebounced(input.trim(), 300)
  const search = useInstitutionSearch(q)

  const found: PickedInstitution[] = (search.data ?? [])
    .filter((i) => (!onlyType || i.institutionType === onlyType) && i.publicId !== excludeId)
    .map((i) => ({ publicId: i.publicId, name: i.name }))
  // the current value stays an option, so it is shown even before (or without) a search
  const options = value && !found.some((o) => o.publicId === value.publicId) ? [value, ...found] : found

  return (
    <Autocomplete<PickedInstitution>
      value={value}
      onChange={(_, picked) => onChange(picked)}
      inputValue={input}
      onInputChange={(_, text) => setInput(text)}
      options={options}
      filterOptions={(x) => x}
      getOptionLabel={(o) => o.name}
      isOptionEqualToValue={(a, b) => a.publicId === b.publicId}
      loading={search.isFetching}
      loadingText={t.loading}
      noOptionsText={q.length < MIN_INSTITUTION_QUERY ? t.typeToSearch : t.noResults}
      renderInput={(params) => (
        <TextField {...params} label={label} required={required} error={error} helperText={helperText} />
      )}
    />
  )
}
