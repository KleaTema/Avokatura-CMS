import { CircularProgress, IconButton, InputAdornment, TextField, type SxProps, type Theme } from '@mui/material'
import { Search, X } from 'lucide-react'
import { sq } from '../../i18n/sq'

/** The longest search text the API accepts (TextSearch.MaxLength). */
export const SEARCH_MAX_LENGTH = 100

/**
 * Live search (search-as-you-type) — the ONE search input of the app: every list, table and filter row uses it, with
 * useLiveSearch (server lists) or matches() (lists filtered in the browser). No "Kërko" button and no Enter: the list
 * follows the typing. The X clears at once; `busy` shows a small spinner while the new rows load (the old ones stay).
 */
export function SearchField({
  value,
  onChange,
  label = sq.search.label,
  busy = false,
  autoFocus,
  fullWidth = true,
  size = 'small',
  sx,
}: {
  value: string
  onChange: (value: string) => void
  label?: string
  /** the list is loading the rows of the new text */
  busy?: boolean
  autoFocus?: boolean
  fullWidth?: boolean
  size?: 'small' | 'medium'
  sx?: SxProps<Theme>
}) {
  return (
    <TextField
      fullWidth={fullWidth}
      size={size}
      label={label}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      autoFocus={autoFocus}
      sx={sx}
      slotProps={{
        htmlInput: { maxLength: SEARCH_MAX_LENGTH, autoComplete: 'off', spellCheck: false },
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <Search size={16} aria-hidden />
            </InputAdornment>
          ),
          endAdornment:
            busy || value !== '' ? (
              <InputAdornment position="end">
                {busy && <CircularProgress size={14} aria-label={sq.search.searching} sx={{ mr: value !== '' ? 0.5 : 0 }} />}
                {value !== '' && (
                  <IconButton size="small" edge="end" aria-label={sq.search.clear} onClick={() => onChange('')}>
                    <X size={16} aria-hidden />
                  </IconButton>
                )}
              </InputAdornment>
            ) : undefined,
        },
      }}
    />
  )
}
