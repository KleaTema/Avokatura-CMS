import { useState } from 'react'
import { MIN_INSTITUTION_QUERY, useInstitutionSearch } from '../features/admin/api'
import { sq } from '../i18n/sq'
import { useDebounced } from '../lib/useDebounced'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

export interface PickedInstitution {
  publicId: string
  name: string
}

const t = sq.institutionPicker

/**
 * Type-ahead over active institutions (GET /api/identity/institutions, anywhere inside the name, short name or NIPT).
 * `onlyType` keeps one institution type, e.g. MINISTRY for a parent; `excludeId` hides the institution being edited.
 * `filter` / `required`: see AppAutocomplete.
 */
export function InstitutionPicker({
  value,
  onChange,
  label,
  required,
  filter,
  error,
  helperText,
  onlyType,
  excludeId,
  size,
}: EmptyChoice & {
  value: PickedInstitution | null
  onChange: (value: PickedInstitution | null) => void
  label: string
  error?: boolean
  helperText?: string
  onlyType?: string
  excludeId?: string
  size?: 'small' | 'medium'
}) {
  const [input, setInput] = useState(value?.name ?? '')
  const q = useDebounced(input.trim(), 300)
  // the name of the chosen institution is not a search
  const search = useInstitutionSearch(value && input === value.name ? '' : q)

  const found: PickedInstitution[] = (search.data ?? [])
    .filter((i) => (!onlyType || i.institutionType === onlyType) && i.publicId !== excludeId)
    .map((i) => ({ publicId: i.publicId, name: i.name }))
  // the current value stays an option, so it is shown even before (or without) a search
  const options = value && !found.some((o) => o.publicId === value.publicId) ? [value, ...found] : found

  return (
    <AppAutocomplete<PickedInstitution>
      value={value}
      onChange={onChange}
      inputValue={input}
      onInputChange={setInput}
      options={options}
      getOptionLabel={(o) => o.name}
      getOptionKey={(o) => o.publicId}
      loading={search.isFetching}
      loadingText={t.loading}
      noOptionsText={q.length < MIN_INSTITUTION_QUERY ? t.typeToSearch : t.noResults}
      label={label}
      required={required}
      filter={filter}
      error={error}
      helperText={helperText}
      size={size}
    />
  )
}
