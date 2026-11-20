import type { Ref } from 'react'
import { useActiveJudges } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { pickerMatches } from '../lib/search'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

const t = sq.cases.register

export interface PickedJudge {
  id: string
  name: string
  courtId: string | null
}

/**
 * The active judges (task 054a): of the chosen court, or — with `showAll` — of every court (the court is then shown
 * next to the name). The whole list opens at once; typing filters it from the 2nd letter (name or surname, substring,
 * case- and accent-insensitive — pickerMatches). Loaded once and cached (useActiveJudges). `filter` / `required`:
 * see AppAutocomplete.
 */
export function JudgePicker({
  courtId,
  showAll,
  courtName,
  value,
  onChange,
  label,
  required,
  filter,
  error,
  helperText,
  inputRef,
  size,
}: EmptyChoice & {
  courtId: number | null
  showAll: boolean
  courtName: (courtId: string | null) => string | undefined
  value: PickedJudge | null
  onChange: (judge: PickedJudge | null) => void
  label: string
  error?: boolean
  helperText?: string
  inputRef?: Ref<HTMLInputElement>
  size?: 'small' | 'medium'
}) {
  const disabled = !showAll && courtId === null
  const judges = useActiveJudges(!disabled)

  const all: PickedJudge[] = (judges.data ?? [])
    .filter((j) => showAll || j.courtId === courtId)
    .map((j) => ({
      id: String(j.id),
      name: `${j.firstName} ${j.lastName}`,
      courtId: j.courtId === null ? null : String(j.courtId),
    }))
  // the current value stays an option (e.g. a judge deactivated since)
  const options = value && !all.some((o) => o.id === value.id) ? [value, ...all] : all
  const selected = value ? (options.find((o) => o.id === value.id) ?? null) : null
  const label2 = (o: PickedJudge) => {
    const court = showAll ? courtName(o.courtId) : undefined
    return court ? `${o.name} — ${court}` : o.name
  }

  return (
    <AppAutocomplete<PickedJudge>
      value={selected}
      onChange={onChange}
      options={options}
      getOptionLabel={(o) => o.name}
      getOptionKey={(o) => o.id}
      renderOptionLabel={label2}
      filterOptions={(items, input) => items.filter((o) => pickerMatches(input, o.name, showAll ? courtName(o.courtId) : undefined))}
      disabled={disabled}
      loading={judges.isPending && !disabled}
      loadingText={t.loading}
      noOptionsText={disabled ? t.chooseCourtFirst : t.noJudges}
      inputRef={inputRef}
      label={label}
      required={required}
      filter={filter}
      error={error}
      helperText={helperText}
      size={size}
    />
  )
}
