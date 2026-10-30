import { Box, List, ListItemButton, ListItemText, Paper, Stack } from '@mui/material'
import { PageHeader } from '../../components/ui/PageHeader'
import { useSearchParams } from 'react-router'
import type { SimpleListKey } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { CourtsPanel } from './lists/CourtsPanel'
import { JudgesPanel } from './lists/JudgesPanel'
import { SimpleListPanel } from './lists/SimpleListPanel'

const t = sq.adminLists

const listKeys = [
  'hearing-types',
  'party-roles',
  'summons-types',
  'document-types',
  'decision-results',
  'courts',
  'judges',
] as const
type ListKey = (typeof listKeys)[number]

const isListKey = (value: string | null): value is ListKey => listKeys.some((k) => k === value)

/** List picker on the left, the selected list on the right. The selection lives in ?list= so it survives a reload. */
export function ListsPage() {
  const [params, setParams] = useSearchParams()
  const requested = params.get('list')
  const selected: ListKey = isListKey(requested) ? requested : 'hearing-types'

  let panel
  if (selected === 'courts') panel = <CourtsPanel />
  else if (selected === 'judges') panel = <JudgesPanel />
  else panel = <SimpleListPanel key={selected} list={selected satisfies SimpleListKey} />

  return (
    <Box>
      <PageHeader title={t.title} />
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={3} sx={{ alignItems: 'flex-start' }}>
        <Paper sx={{ width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
          <List component="nav" aria-label={t.pickerLabel} dense>
            {listKeys.map((key) => (
              <ListItemButton
                key={key}
                selected={key === selected}
                aria-current={key === selected ? 'page' : undefined}
                onClick={() => setParams({ list: key })}
              >
                <ListItemText primary={t.lists[key]} />
              </ListItemButton>
            ))}
          </List>
        </Paper>
        <Box sx={{ flexGrow: 1, minWidth: 0, width: '100%' }}>{panel}</Box>
      </Stack>
    </Box>
  )
}
