import { Autocomplete, TextField, type AutocompleteRenderOptionState } from '@mui/material'
import type { HTMLAttributes, ReactNode, Ref } from 'react'
import { sq } from '../../i18n/sq'

/**
 * How an empty choice works (the same rule as AppSelect):
 * - `filter`: the first option "Të gjitha" means no filter; with nothing chosen the field shows "Të gjitha".
 * - `required`: no empty option and no clear (×) — the user can still pick another option.
 * - neither (an optional field): the first option "— Asnjë —" and the clear (×) icon empty the field.
 */
export interface EmptyChoice {
  required?: boolean
  filter?: boolean
}

/** An option of the list: a real option, or (empty = true, no option) the empty choice. */
interface Item<T> {
  empty: boolean
  option?: T
}

const EMPTY = { empty: true }

/**
 * The only Autocomplete of the app (also inside OptionPicker, CourtPicker, JudgePicker, InstitutionPicker). The
 * empty choice is a real first option, so a filter can go back to "Të gjitha" and an optional field to "— Asnjë —"
 * from the keyboard. Server-searched pickers pass `filterOptions={(x) => x}` with `inputValue`/`onInputChange`.
 */
export function AppAutocomplete<T>({
  options,
  value,
  onChange,
  getOptionLabel,
  getOptionKey,
  renderOptionLabel,
  getOptionDisabled,
  groupBy,
  filterOptions,
  inputValue,
  onInputChange,
  label,
  required,
  filter,
  disabled,
  error,
  helperText,
  autoFocus,
  inputRef,
  size,
  loading,
  loadingText,
  noOptionsText = sq.search.noOptions,
  placeholder,
}: EmptyChoice & {
  options: T[]
  value: T | null
  onChange: (value: T | null) => void
  getOptionLabel: (option: T) => string
  getOptionKey: (option: T) => string
  /** the text of an option in the list, when it differs from the label in the field */
  renderOptionLabel?: (option: T) => ReactNode
  getOptionDisabled?: (option: T) => boolean
  groupBy?: (option: T) => string
  /** narrows the real options to the typed text (default: all of them) */
  filterOptions?: (options: T[], input: string) => T[]
  inputValue?: string
  onInputChange?: (text: string) => void
  label: string
  disabled?: boolean
  error?: boolean
  helperText?: ReactNode
  autoFocus?: boolean
  inputRef?: Ref<HTMLInputElement>
  size?: 'small' | 'medium'
  loading?: boolean
  loadingText?: string
  noOptionsText?: string
  placeholder?: string
}) {
  const emptyLabel = filter ? sq.common.all : sq.common.none
  const items: Item<T>[] = [...(required ? [] : [EMPTY]), ...options.map((option) => ({ empty: false, option }))]
  const selected: Item<T> | null = value === null ? null : { empty: false, option: value }
  const labelOf = (item: Item<T>) => (item.empty ? emptyLabel : getOptionLabel(item.option))
  const keyOf = (item: Item<T>) => (item.empty ? '' : `o:${getOptionKey(item.option)}`)

  return (
    <Autocomplete<Item<T>, false, boolean>
      options={items}
      value={selected}
      onChange={(_, picked) => onChange(picked && !picked.empty ? picked.option : null)}
      inputValue={inputValue}
      onInputChange={onInputChange ? (_, text, reason) => {
        // choosing the empty option leaves its label in the box: the field is empty, so is the text
        onInputChange(reason === 'selectOption' && text === emptyLabel ? '' : text)
      } : undefined}
      getOptionLabel={labelOf}
      getOptionKey={keyOf}
      isOptionEqualToValue={(a, b) => keyOf(a) === keyOf(b)}
      getOptionDisabled={getOptionDisabled ? (item) => !item.empty && getOptionDisabled(item.option) : undefined}
      groupBy={groupBy ? (item) => (item.empty ? '' : groupBy(item.option)) : undefined}
      filterOptions={(all, state) => {
        const real = all.flatMap((item) => (item.empty ? [] : [item.option]))
        const kept = filterOptions ? filterOptions(real, state.inputValue) : real
        const keptItems = kept.map((option) => ({ empty: false, option }))
        // the empty choice stays first while nothing is typed
        return !required && state.inputValue.trim() === '' ? [EMPTY, ...keptItems] : keptItems
      }}
      renderOption={(props: HTMLAttributes<HTMLLIElement> & { key: string }, item: Item<T>, _state: AutocompleteRenderOptionState) => {
        const { key, ...rest } = props
        return (
          <li key={key} {...rest}>
            {item.empty ? emptyLabel : (renderOptionLabel?.(item.option) ?? getOptionLabel(item.option))}
          </li>
        )
      }}
      disableClearable={required}
      disabled={disabled}
      size={size}
      loading={loading}
      loadingText={loadingText}
      noOptionsText={noOptionsText}
      clearText={sq.common.clear}
      renderInput={(params) => (
        <TextField
          {...params}
          inputRef={inputRef}
          label={label}
          required={required}
          error={error}
          helperText={helperText}
          autoFocus={autoFocus}
          // a filter with nothing chosen reads "Të gjitha"
          placeholder={placeholder ?? (filter ? sq.common.all : undefined)}
          slotProps={filter ? { ...params.slotProps, inputLabel: { ...params.slotProps?.inputLabel, shrink: true } } : params.slotProps}
          // "Të gjitha" of a filter reads like a chosen value (as in AppSelect), not like a faint hint
          sx={filter ? { '& input::placeholder': { color: 'text.primary', opacity: 1 } } : undefined}
        />
      )}
    />
  )
}
