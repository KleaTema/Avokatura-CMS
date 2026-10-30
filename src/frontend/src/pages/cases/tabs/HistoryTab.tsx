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
import { useCaseHistory, type CaseHistoryItem } from '../../../features/cases/api'
import { sq } from '../../../i18n/sq'
import { formatDateTime } from '../../../lib/dates'
import { detailRows } from '../../../lib/historyDetails'
import { problemMessage } from '../../../lib/problem'

const h = sq.cases.page.history

/** Historiku — every level of the dossier the user can view, newest first, with readable before/after details. */
export function HistoryTab({ casePublicId }: { casePublicId: string }) {
  const history = useCaseHistory(casePublicId, true)
  const items = history.data?.pages.flatMap((p) => p.items) ?? []

  if (history.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (history.isError) return <Alert severity="error">{problemMessage(history.error)}</Alert>
  if (items.length === 0) return <Typography color="text.secondary">{h.empty}</Typography>

  return (
    <Stack spacing={1}>
      <Box component="ol" sx={{ listStyle: 'none', m: 0, p: 0 }}>
        {items.map((item, index) => (
          <HistoryEntry key={`${item.occurredAt}-${index}`} item={item} />
        ))}
      </Box>
      {history.hasNextPage && (
        <Box>
          <Button onClick={() => void history.fetchNextPage()} disabled={history.isFetchingNextPage}>
            {sq.common.loadMore}
          </Button>
        </Box>
      )}
    </Stack>
  )
}

function HistoryEntry({ item }: { item: CaseHistoryItem }) {
  const [open, setOpen] = useState(false)
  const detailsId = useId()
  const rows = detailRows(item.old, item.new)
  return (
    <Paper component="li" variant="outlined" sx={{ p: 2, mb: 1 }}>
      <Stack spacing={0.5}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
          <Chip size="small" label={item.level} sx={{ fontFamily: 'monospace' }} />
          <Typography sx={{ fontWeight: 600 }}>{h.events[item.eventType] ?? item.eventType}</Typography>
          <Typography variant="body2" color="text.secondary">
            {formatDateTime(item.occurredAt)} · {h.by(item.actorName ?? h.system)}
          </Typography>
        </Stack>
        <Typography>{item.summary}</Typography>
        {rows.length > 0 && (
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
