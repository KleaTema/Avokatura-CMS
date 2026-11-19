import { Button, Menu, MenuItem, Stack } from '@mui/material'
import { Plus } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { sq } from '../../i18n/sq'
import { FilterField } from './FilterField'
import { hasValue, optionsUnder, type FilterDef, type FilterValue, type FilterValues } from './filterTypes'

/**
 * Task 053m — the ONE filter bar of every table: the table's default filters (2–3, its main ones), the filters the user
 * added (each with ×) and "+ Shto filtër", a menu of every other column of the table. Each filter is drawn by its kind
 * (FilterField); a child's options follow its parent's choice. The bar only shows and edits values — the page keeps them
 * (useUrlFilters for a list, its own draft for a report). `before`: controls the bar does not own (e.g. the tabs' search).
 */
export function FilterBar({
  filters,
  values,
  added,
  onChange,
  onAdd,
  onRemove,
  before,
  after,
}: {
  filters: readonly FilterDef[]
  values: FilterValues
  /** the keys added with "+ Shto filtër" (shown even while empty) */
  added: readonly string[]
  onChange: (filter: FilterDef, value: FilterValue) => void
  onAdd: (key: string) => void
  onRemove: (filter: FilterDef) => void
  before?: ReactNode
  after?: ReactNode
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const visible = filters.filter((f) => f.isDefault || f.locked || added.includes(f.key) || hasValue(values[f.key]))
  const addable = filters.filter((f) => !visible.includes(f))

  return (
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: { md: 'flex-start' } }} role="group" aria-label={sq.filters.title}>
      {before}
      {visible.map((f) => (
        <FilterField
          key={f.key}
          filter={f}
          value={values[f.key]}
          options={optionsUnder(f, values)}
          onChange={(v) => onChange(f, v)}
          onRemove={f.isDefault || f.locked ? undefined : () => onRemove(f)}
          autoFocus={added[added.length - 1] === f.key}
        />
      ))}
      {addable.length > 0 && (
        <Button
          variant="outlined"
          startIcon={<Plus size={18} aria-hidden />}
          onClick={(e) => setAnchor(e.currentTarget)}
          aria-haspopup="menu"
          sx={{ mt: 0.25, whiteSpace: 'nowrap' }}
        >
          {sq.filters.add}
        </Button>
      )}
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        {addable.map((f) => (
          <MenuItem
            key={f.key}
            onClick={() => {
              onAdd(f.key)
              setAnchor(null)
            }}
          >
            {f.label}
          </MenuItem>
        ))}
      </Menu>
      {after}
    </Stack>
  )
}
