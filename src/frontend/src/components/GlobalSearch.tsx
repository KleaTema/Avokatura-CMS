import { Box, CircularProgress, InputBase, Paper, Popper, Stack, Typography } from '@mui/material'
import { Search } from 'lucide-react'
import { Fragment, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router'
import { searchTabOf, useCaseTabs } from '../features/cases/api'
import { MATCH_ORDER, useGlobalSearch, type SearchResult } from '../features/search/api'
import { sq } from '../i18n/sq'
import { highlightParts } from '../lib/highlight'
import { useDebounced } from '../lib/useDebounced'
import { tokens } from '../theme/tokens'
import { StatusChip } from './ui/StatusChip'

const t = sq.globalSearch
const c = tokens.color

/** The searched words in bold inside a text (accent-insensitive, like the search). */
function Highlighted({ text, q }: { text: string; q: string }) {
  return (
    <>
      {highlightParts(text, q).map((p, i) =>
        p.match ? (
          <Box component="strong" key={i} sx={{ fontWeight: 700, color: 'text.primary' }}>
            {p.text}
          </Box>
        ) : (
          <Fragment key={i}>{p.text}</Fragment>
        ),
      )}
    </>
  )
}

/** True when a keyboard shortcut must not steal the key (the user is typing somewhere). */
const typingIn = (target: EventTarget | null) =>
  target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))

/**
 * The header search (task 047): a combobox with a list of up to 8 cases from GET /api/search, grouped by where the
 * text was found, the match in bold. 250 ms after typing stops, from 2 characters; a newer text cancels the older
 * request. Keys: Ctrl+K or "/" focus it (variant "header"), ↑/↓ move, Enter opens the chosen case — or, with
 * nothing chosen, the case list searched for the text over every status — Esc closes. The last row opens that list.
 * Variant "sheet": the full-width search of a phone (opened from the header's search icon), focused at once.
 */
export function GlobalSearch({ variant, onDone }: { variant: 'header' | 'sheet'; onDone?: () => void }) {
  const navigate = useNavigate()
  const id = useId()
  const listId = `${id}-list`
  const inputRef = useRef<HTMLInputElement>(null)
  // the list's anchor as state (a callback ref): its width is read while rendering
  const [anchor, setAnchor] = useState<HTMLDivElement | null>(null)
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const debounced = useDebounced(text.trim(), 250)
  const q = debounced.length >= 2 ? debounced : ''
  // the highlighted row belongs to the text it was chosen for: a new text starts with none
  const [selection, setSelection] = useState({ q: '', index: -1 })
  const active = selection.q === q ? selection.index : -1
  const setActive = (next: number | ((current: number) => number)) =>
    setSelection({ q, index: typeof next === 'function' ? next(active) : next })
  const search = useGlobalSearch(q)
  const tabs = useCaseTabs()

  // the phone sheet: the cursor in the box once the dialog (and its focus trap) has settled
  useEffect(() => {
    if (variant !== 'sheet') return
    const timer = setTimeout(() => inputRef.current?.focus(), 50)
    return () => clearTimeout(timer)
  }, [variant])

  // Ctrl+K anywhere, "/" when not typing: focus the search
  useEffect(() => {
    if (variant !== 'header') return
    const onKey = (e: globalThis.KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        inputRef.current?.focus()
      } else if (e.key === '/' && !typingIn(e.target)) {
        e.preventDefault()
        inputRef.current?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [variant])

  // the results grouped in the ranking's order (the server already ranks; a group keeps that order)
  const groups = useMemo(() => {
    const items = q && search.data ? search.data.items : []
    return MATCH_ORDER.map((on) => ({ on, items: items.filter((i) => i.matchedOn === on) })).filter((g) => g.items.length > 0)
  }, [q, search.data])
  const flat = groups.flatMap((g) => g.items)
  const total = Number(search.data?.totalApprox ?? 0)
  const hasViewAll = q !== '' && flat.length > 0
  const optionCount = flat.length + (hasViewAll ? 1 : 0)
  const showList = open && q !== '' && (search.data !== undefined || search.isFetching)

  const listUrl = () => {
    const tab = searchTabOf(tabs.data?.tabs ?? [])
    return `/cases?${tab ? `tab=${tab}&` : ''}q=${encodeURIComponent(text.trim())}&status=ALL`
  }
  const finish = (to: string) => {
    setOpen(false)
    setText('')
    inputRef.current?.blur()
    navigate(to)
    onDone?.()
  }
  const choose = (index: number) => {
    if (index < flat.length) finish(`/cases/${flat[index].casePublicId}`)
    else finish(listUrl())
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown' && optionCount > 0) {
      e.preventDefault()
      setOpen(true)
      setActive((a) => (a + 1) % optionCount)
    } else if (e.key === 'ArrowUp' && optionCount > 0) {
      e.preventDefault()
      setActive((a) => (a <= 0 ? optionCount - 1 : a - 1))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (active >= 0 && active < optionCount) choose(active)
      else if (text.trim().length >= 2) finish(listUrl())
    } else if (e.key === 'Escape') {
      if (open) {
        e.preventDefault()
        setOpen(false)
        setActive(-1)
      } else {
        onDone?.()
      }
    }
  }

  const optionId = (i: number) => `${id}-option-${i}`
  const header = variant === 'header'
  let index = 0

  return (
    <Box ref={setAnchor} sx={{ position: 'relative', width: '100%' }}>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 1,
          height: tokens.layout.controlHeight,
          px: 1.5,
          borderRadius: `${tokens.radius.control}px`,
          ...(header
            ? {
                bgcolor: c.onNavy.field,
                border: `1px solid ${c.onNavy.border}`,
                color: c.white,
                '&:hover': { bgcolor: c.onNavy.fieldHover },
                '&:focus-within': { borderColor: c.primaryLight },
              }
            : { bgcolor: 'background.paper', border: `1px solid ${c.cardBorder}`, '&:focus-within': { borderColor: c.primary } }),
        }}
      >
        <Search size={18} aria-hidden />
        <InputBase
          inputRef={inputRef}
          autoFocus={!header}
          value={text}
          onChange={(e) => {
            setText(e.target.value)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setOpen(false)}
          onKeyDown={onKeyDown}
          placeholder={t.placeholder}
          inputProps={{
            role: 'combobox',
            'aria-label': t.label,
            'aria-expanded': showList,
            'aria-controls': listId,
            'aria-autocomplete': 'list',
            'aria-activedescendant': showList && active >= 0 ? optionId(active) : undefined,
            maxLength: 100,
          }}
          sx={{
            flex: 1,
            color: header ? c.white : 'text.primary',
            '& input::placeholder': { color: header ? c.onNavy.muted : 'text.secondary', opacity: 1 },
          }}
        />
        {/* the spinner's place is always there: no jump when it appears */}
        <Box sx={{ width: 18, display: 'grid', placeItems: 'center' }} aria-hidden={!search.isFetching}>
          {search.isFetching && <CircularProgress size={16} color="inherit" aria-label={t.loading} />}
        </Box>
        {header && (
          <Typography variant="caption" sx={{ color: c.onNavy.muted, display: { xs: 'none', lg: 'block' } }} aria-hidden>
            {t.shortcut}
          </Typography>
        )}
      </Box>

      <Popper
        open={showList}
        anchorEl={anchor}
        placement="bottom-start"
        disablePortal={!header}
        sx={{ zIndex: (theme) => theme.zIndex.modal + 1, width: anchor?.offsetWidth }}
      >
        {/* mouse down would blur the input before the click lands */}
        <Paper elevation={6} sx={{ mt: 0.5, maxHeight: '70vh', overflowY: 'auto' }} onMouseDown={(e) => e.preventDefault()}>
          <Box component="ul" role="listbox" id={listId} aria-label={t.results} sx={{ listStyle: 'none', m: 0, p: 0 }}>
            {flat.length === 0 && !search.isFetching && (
              <Box component="li" role="presentation" sx={{ px: 2, py: 1.5 }}>
                <Typography variant="body2" color="text.secondary">
                  {t.noCases(q)}
                </Typography>
              </Box>
            )}
            {groups.map((g) => (
              <Fragment key={g.on}>
                <Box
                  component="li"
                  role="presentation"
                  sx={{ px: 2, pt: 1.25, pb: 0.5, typography: 'caption', color: 'text.secondary', fontWeight: 600 }}
                >
                  {t.groups[g.on] ?? g.on}
                </Box>
                {g.items.map((item: SearchResult) => {
                  const i = index++
                  return (
                    <Box
                      component="li"
                      key={item.casePublicId}
                      id={optionId(i)}
                      role="option"
                      aria-selected={active === i}
                      onMouseEnter={() => setActive(i)}
                      onClick={() => choose(i)}
                      sx={{
                        px: 2,
                        py: 1,
                        cursor: 'pointer',
                        bgcolor: active === i ? c.tint.blue : undefined,
                      }}
                    >
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                        <Typography variant="body2" sx={{ color: 'primary.main', fontWeight: 600 }}>
                          <Highlighted text={item.caseCode} q={q} />
                        </Typography>
                        <StatusChip kind="status" value={item.status} />
                        {item.isArchived && (
                          <Typography variant="caption" color="text.secondary">
                            {t.archived}
                          </Typography>
                        )}
                      </Stack>
                      <Typography variant="caption" color="text.secondary" component="p" noWrap>
                        {[item.courtName, item.primaryLawyerName].filter(Boolean).join(' · ')}
                      </Typography>
                      {item.snippet && (
                        <Typography variant="caption" color="text.secondary" component="p">
                          <Highlighted text={item.snippet} q={q} />
                        </Typography>
                      )}
                    </Box>
                  )
                })}
              </Fragment>
            ))}
            {hasViewAll && (
              <Box
                component="li"
                id={optionId(flat.length)}
                role="option"
                aria-selected={active === flat.length}
                onMouseEnter={() => setActive(flat.length)}
                onClick={() => choose(flat.length)}
                sx={{
                  px: 2,
                  py: 1.25,
                  cursor: 'pointer',
                  borderTop: `1px solid ${c.cardBorder}`,
                  color: 'primary.main',
                  typography: 'body2',
                  fontWeight: 600,
                  bgcolor: active === flat.length ? c.tint.blue : undefined,
                }}
              >
                {t.viewAll(`${total.toLocaleString('sq-AL')}${search.data?.capped ? '+' : ''}`)}
              </Box>
            )}
          </Box>
        </Paper>
      </Popper>
    </Box>
  )
}
