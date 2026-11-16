import { Alert, Button, Dialog, DialogActions, DialogTitle, Stack, TableCell, TableRow } from '@mui/material'
import { Play, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { DataTable } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import {
  cleanValues,
  filtersParam,
  parseValues,
  useDeleteSavedReport,
  useSavedReports,
  type SavedReportView,
} from '../../../features/reports/builder'
import { sq } from '../../../i18n/sq'
import { formatDateTime } from '../../../lib/dates'
import { problemMessage } from '../../../lib/problem'

const t = sq.reports.builder

/** The builder's URL of a saved report, generated at once ("rerun in one click"). */
function savedReportUrl(report: SavedReportView) {
  const query = new URLSearchParams({
    tab: `tabela${Number(report.table)}`,
    f: filtersParam(parseValues(report.filters)),
    run: '1',
    saved: report.publicId,
    name: report.name,
  })
  if (report.columns.length > 0) query.set('c', report.columns.join(','))
  return `/reports?${query.toString()}`
}

/**
 * "Raportet e mia" (task 053h): the user's saved reports — name, table, filters, last change; "Hap" opens the builder
 * with them and generates at once; "Fshi" after a confirmation. Private to the user (the API's rule).
 */
export function SavedReportsTab() {
  const saved = useSavedReports()
  const remove = useDeleteSavedReport()
  const navigate = useNavigate()
  const [deleting, setDeleting] = useState<SavedReportView | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const c = t.mineColumns
  const items = saved.data ?? []

  if (saved.isError) return <Alert severity="error">{problemMessage(saved.error)}</Alert>

  return (
    <Stack spacing={2}>
      <DataTable
        label={t.mineTable}
        loading={saved.isPending}
        columns={[
          { key: 'name', label: c.name },
          { key: 'table', label: c.table },
          { key: 'updated', label: c.updated },
          { key: 'actions', label: c.actions, align: 'right' as const },
        ]}
        empty={saved.isSuccess && items.length === 0 ? <EmptyState title={t.mineEmpty} /> : undefined}
      >
        {items.map((r) => (
          <TableRow key={r.publicId}>
            <TableCell sx={{ fontWeight: 600 }}>{r.name}</TableCell>
            <TableCell>
              {r.tableName}
              <br />
              <span style={{ opacity: 0.7 }}>{t.filtersSummary(Object.keys(cleanValues(parseValues(r.filters))).length)}</span>
            </TableCell>
            <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(r.updatedAt)}</TableCell>
            <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
              <Button size="small" startIcon={<Play size={16} aria-hidden />} onClick={() => void navigate(savedReportUrl(r))}>
                {t.open}
              </Button>
              <Button size="small" color="error" startIcon={<Trash2 size={16} aria-hidden />} onClick={() => setDeleting(r)}>
                {t.delete}
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </DataTable>

      <Dialog open={deleting !== null} onClose={() => setDeleting(null)} aria-labelledby="delete-saved-title">
        <DialogTitle id="delete-saved-title">{deleting ? t.deleteTitle(deleting.name) : ''}</DialogTitle>
        {remove.isError && (
          <Alert severity="error" sx={{ mx: 3 }}>
            {problemMessage(remove.error)}
          </Alert>
        )}
        <DialogActions>
          <Button onClick={() => setDeleting(null)}>{sq.common.cancel}</Button>
          <Button
            color="error"
            variant="contained"
            disabled={remove.isPending}
            onClick={() => {
              if (!deleting) return
              const name = deleting.name
              remove.mutate(deleting.publicId, {
                onSuccess: () => {
                  setDeleting(null)
                  setToast(t.deleted(name))
                },
              })
            }}
          >
            {t.delete}
          </Button>
        </DialogActions>
      </Dialog>
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}
