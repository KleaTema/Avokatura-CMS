import { useActiveInstitutions } from '../features/admin/api'
import { sq } from '../i18n/sq'
import { pickerMatches } from '../lib/search'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

export interface PickedInstitution {
  publicId: string
  name: string
}

interface InstitutionOption extends PickedInstitution {
  shortName: string | null
}

const t = sq.institutionPicker

/**
 * The active institutions (task 054a): the whole list, sorted by name, opens at once; typing filters it from the
 * 2nd letter (name or short name, substring, case- and accent-insensitive — pickerMatches). Loaded once and cached
 * (useActiveInstitutions; an admin change refreshes it). `onlyType` keeps one institution type, e.g. MINISTRY for a
 * parent; `excludeId` hides the institution being edited. `filter` / `required`: see AppAutocomplete.
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
  const list = useActiveInstitutions()
  const all: InstitutionOption[] = (list.data ?? [])
    .filter((i) => (!onlyType || i.institutionType === onlyType) && i.publicId !== excludeId)
    .map((i) => ({ publicId: i.publicId, name: i.name, shortName: i.shortName ?? null }))
  // the current value stays an option (e.g. an institution deactivated since it was chosen)
  const options = value && !all.some((o) => o.publicId === value.publicId) ? [{ ...value, shortName: null }, ...all] : all
  const selected = value ? (options.find((o) => o.publicId === value.publicId) ?? null) : null

  return (
    <AppAutocomplete<InstitutionOption>
      value={selected}
      onChange={(o) => onChange(o ? { publicId: o.publicId, name: o.name } : null)}
      options={options}
      getOptionLabel={(o) => o.name}
      getOptionKey={(o) => o.publicId}
      filterOptions={(items, input) => items.filter((o) => pickerMatches(input, o.name, o.shortName))}
      loading={list.isPending}
      loadingText={t.loading}
      noOptionsText={t.noResults}
      label={label}
      required={required}
      filter={filter}
      error={error}
      helperText={helperText}
      size={size}
    />
  )
}
