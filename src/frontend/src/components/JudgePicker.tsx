import { Autocomplete, TextField } from '@mui/material'
import { useState, type Ref } from 'react'
import { useJudgeSearch } from '../features/reference/api'
import { sq } from '../i18n/sq'
import { MIN_SERVER_QUERY } from '../lib/search'
import { useDebounced } from '../lib/useDebounced'

const t = sq.cases.register

export interface PickedJudge {
  id: string
  name: string
  courtId: string | null
}

/**
 * Active judges, searched on the server (name or surname, accent-insensitive): of the chosen court, or — with
 * `showAll` — of every court (from 2 characters; the court is then shown next to the name).
 */
export function JudgePicker({
  courtId,
  showAll,
  courtName,
  value,
  onChange,
  label,
  required,
  error,
  helperText,
  inputRef,
}: {
  courtId: number | null
  showAll: boolean
  courtName: (courtId: string | null) => string | undefined
  value: PickedJudge | null
  onChange: (judge: PickedJudge | null) => void
  label: string
  required?: boolean
  error?: boolean
  helperText?: string
  inputRef?: Ref<HTMLInputElement>
}) {
  const [input, setInput] = useState('')
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
    <Autocomplete<PickedJudge>
      value={value}
      onChange={(_, picked) => onChange(picked)}
      inputValue={input}
      onInputChange={(_, text) => setInput(text)}
      options={options}
      filterOptions={(x) => x}
      getOptionLabel={(o) => o.name}
      getOptionKey={(o) => o.id}
      renderOption={(props, o) => {
        const { key, ...rest } = props
        const court = showAll ? courtName(o.courtId) : undefined
        return (
          <li key={key} {...rest}>
            {court ? `${o.name} — ${court}` : o.name}
          </li>
        )
      }}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      disabled={disabled}
      loading={search.isFetching}
      loadingText={t.loading}
      noOptionsText={noOptionsText}
      renderInput={(params) => (
        <TextField
          {...params}
          inputRef={inputRef}
          label={label}
          required={required}
          error={error}
          helperText={helperText ?? (disabled ? t.chooseCourtFirst : undefined)}
        />
      )}
    />
  )
}
