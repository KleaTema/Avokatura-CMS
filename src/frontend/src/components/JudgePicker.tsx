import { useState, type Ref } from 'react'
import { useJudgeSearch } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { MIN_SERVER_QUERY } from '../lib/search'
import { useDebounced } from '../lib/useDebounced'
import { AppAutocomplete, type EmptyChoice } from './ui/AppAutocomplete'

const t = sq.cases.register

export interface PickedJudge {
  id: string
  name: string
  courtId: string | null
}

/**
 * Active judges, searched on the server (name or surname, accent-insensitive): of the chosen court, or — with
 * `showAll` — of every court (from 2 characters; the court is then shown next to the name). `filter` / `required`:
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
  const [input, setInput] = useState(value?.name ?? '')
  const q = useDebounced(input, 300)
  const disabled = !showAll && courtId === null
  // the label of the chosen judge is not a search
  const searchText = value && input === value.name ? '' : q
  const search = useJudgeSearch(showAll ? null : courtId, searchText, !disabled)

  const found: PickedJudge[] = (search.data ?? []).map((j) => ({
    id: String(j.id),
    name: `${j.firstName} ${j.lastName}`,
    courtId: j.courtId === null ? null : String(j.courtId),
  }))
  const options = value && !found.some((o) => o.id === value.id) ? [value, ...found] : found

  const noOptionsText = disabled
    ? t.chooseCourtFirst
    : showAll && searchText.trim().length < MIN_SERVER_QUERY
      ? t.typeJudge
      : t.noJudges

  return (
    <AppAutocomplete<PickedJudge>
      value={value}
      onChange={onChange}
      inputValue={input}
      onInputChange={setInput}
      options={options}
      getOptionLabel={(o) => o.name}
      getOptionKey={(o) => o.id}
      renderOptionLabel={(o) => {
        const court = showAll ? courtName(o.courtId) : undefined
        return court ? `${o.name} — ${court}` : o.name
      }}
      disabled={disabled}
      loading={search.isFetching}
      loadingText={t.loading}
      noOptionsText={noOptionsText}
      inputRef={inputRef}
      label={label}
      required={required}
      filter={filter}
      error={error}
      helperText={helperText ?? (disabled ? t.chooseCourtFirst : undefined)}
      size={size}
    />
  )
}
