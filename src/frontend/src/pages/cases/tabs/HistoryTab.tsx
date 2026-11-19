import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useId, useState } from 'react'
import { useSearchParams } from 'react-router'
import { CodeText } from '../../../components/ui/CodeText'
import { PaginationFooter } from '../../../components/ui/DataTable'
import { SearchField } from '../../../components/ui/SearchField'
import { useCaseHistory, type CaseHistoryItem } from '../../../features/cases/api'
import { sq } from '../../../i18n/sq'
import { formatDateTime } from '../../../lib/dates'
import { detailRows, historyLine, textOf } from '../../../lib/historyDetails'
import { pageKey, useKeysetPaging } from '../../../lib/paging'
import { problemMessage } from '../../../lib/problem'
import { useLiveSearch } from '../../../lib/useLiveSearch'

const h = sq.cases.page.history

const SEARCH_PARAM = 'histq'

/**
 * Historiku — every level of the dossier the user can view, newest first, with readable before/after details;
 * keyset pages of 10 (page and size in the URL: histpage, histsize). Live search over the rows' text (?histq=, back to
 * the first page on a new text; the previous rows stay while the new ones load).
 */
export function HistoryTab({ casePublicId }: { casePublicId: string }) {
  const [params, setParams] = useSearchParams()
  const text = params.get(SEARCH_PARAM) ?? ''
  const q = useLiveSearch(text)
  const setText = (value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set(SEARCH_PARAM, value)
        else next.delete(SEARCH_PARAM)
        next.delete(pageKey('hist'))
        return next
      },
      { replace: true },
    )
  const paging = useKeysetPaging('hist')
  const history = useCaseHistory(casePublicId, { cursor: paging.cursor, size: paging.size }, true, q)
  const items = history.data?.items ?? []

  const search = (
    <Box sx={{ maxWidth: 360 }}>
      <SearchField label={h.search} value={text} onChange={setText} busy={history.isFetching && text !== ''} />
    </Box>
  )
  if (history.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (history.isError)
    return (
      <Stack spacing={2}>
        {search}
        <Alert severity="error">{problemMessage(history.error)}</Alert>
      </Stack>
    )
  if (items.length === 0)
    return (
      <Stack spacing={2}>
        {(q !== '' || text !== '') && search}
        <Typography color="text.secondary">{q !== '' ? sq.search.noResults(q) : h.empty}</Typography>
      </Stack>
    )

  return (
    <Stack spacing={1}>
      {search}
      <Box component="ol" sx={{ listStyle: 'none', m: 0, p: 0 }}>
        {items.map((item, index) => (
          <HistoryEntry key={`${item.occurredAt}-${index}`} item={item} />
        ))}
      </Box>
      <PaginationFooter
        mode="keyset"
        paging={paging}
        count={items.length}
        total={Number(history.data?.total ?? 0)}
        nextCursor={history.data?.nextCursor}
        busy={history.isFetching}
        stale={history.isPlaceholderData}
      />
    </Stack>
  )
}

function HistoryEntry({ item }: { item: CaseHistoryItem }) {
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const rows = detailRows(item.old, item.new)
  // an object change: the two full texts one under the other, not squeezed into a table
  const objectBefore = item.eventType === 'OBJECT_CHANGED' ? textOf(item.old, 'objectText') : null
  const objectAfter = item.eventType === 'OBJECT_CHANGED' ? textOf(item.new, 'objectText') : null
  const objectView = objectBefore !== null || objectAfter !== null
  return (
    <Paper component="li" variant="outlined" sx={{ p: 2, mb: 1 }}>
      <Stack spacing={0.5}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
          <Chip size="small" color="primary" label={<CodeText>{item.level}</CodeText>} />
          <Typography variant="h5" component="p">
            {h.events[item.eventType] ?? item.eventType}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {formatDateTime(item.occurredAt)} · {h.by(item.actorName ?? h.system)}
          </Typography>
        </Stack>
        <Typography>{historyLine(item.eventType, item.old, item.new, item.summary)}</Typography>
        {objectView && (
          <Box>
            <Button size="small" onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-controls={detailsId}>
              {open ? h.hideObject : h.showObject}
            </Button>
            <Collapse in={open} id={detailsId}>
              <Stack spacing={1.5} sx={{ mt: 1 }}>
                {[
                  { label: h.before, text: objectBefore },
                  { label: h.after, text: objectAfter },
                ].map((part) => (
                  <Box key={part.label}>
                    <Typography variant="body2" color="text.secondary">
                      {part.label}
                    </Typography>
                    <Paper variant="outlined" sx={{ p: 1.5, bgcolor: 'background.default' }}>
                      <Typography sx={{ whiteSpace: 'pre-wrap' }}>{part.text ?? sq.cases.page.none}</Typography>
                    </Paper>
                  </Box>
                ))}
              </Stack>
            </Collapse>
          </Box>
        )}
        {!objectView && rows.length > 0 && (
          <Box>
            <Button size="small" onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-controls={detailsId}>
              {open ? h.hideDetails : h.showDetails}
            </Button>
            <Collapse in={open} id={detailsId}>
              <Table size="small" sx={{ mt: 1 }}>
                <TableHead>
                  <TableRow>
                    <TableCell>{h.field}</TableCell>
                    <TableCell>{h.before}</TableCell>
                    <TableCell>{h.after}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.key}>
                      <TableCell>{row.label}</TableCell>
                      <TableCell sx={{ whiteSpace: 'pre-wrap' }}>{row.before}</TableCell>
                      <TableCell sx={{ whiteSpace: 'pre-wrap' }}>{row.after}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Collapse>
          </Box>
        )}
      </Stack>
    </Paper>
  )
}
