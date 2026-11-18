import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { Download, FileSpreadsheet, Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import {
  getImport,
  importBase,
  useApplyImport,
  useImportDefinition,
  useImports,
  useUploadImport,
  type ImportDefinitionView,
  type ImportKind,
  type ImportListItem,
  type ImportRowView,
  type ImportView,
} from '../../features/imports/api'
import { useTableFilters } from '../../features/filters/api'
import { sq } from '../../i18n/sq'
import { matchesFilters } from '../filters/clientFilter'
import { TableFilterBar } from '../filters/TableFilterBar'
import { useUrlFilters } from '../filters/useUrlFilters'
import { formatDateTime } from '../../lib/dates'
import { downloadFile } from '../../lib/download'
import { problemMessage } from '../../lib/problem'
import { SectionCard } from '../ui/SectionCard'
import { SuccessToast } from '../ui/SuccessToast'

const t = sq.imports

/** The chip colour of a row status: only NEW / CHANGED are written by "Apliko". */
const statusColor = (status: string) =>
  status === 'ERROR' ? 'error' : status === 'NEW' || status === 'CHANGED' ? 'primary' : status === 'EXISTS' ? 'warning' : 'default'

/**
 * Task 053l — one import of the shared pipeline: "Si duhet të jetë skedari" (purpose, formats, the exact columns with
 * required / optional, format and an example, the rules — all from the server's definition, the same text as the
 * template's "Udhëzime" sheet), "Shkarko shabllonin", the upload and its PREVIEW (nothing is written), "Apliko" (the only
 * gold button), the result report and the earlier imports.
 */
export function ImportPanel({ kind }: { kind: ImportKind }) {
  const input = useRef<HTMLInputElement>(null)
  const definition = useImportDefinition(kind)
  const imports = useImports(kind)
  const upload = useUploadImport(kind)
  const apply = useApplyImport(kind)
  const [current, setCurrent] = useState<ImportView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  // task 053m: the history's filters (GET /api/filters/imports), kept in the URL, applied in the browser
  const filterMeta = useTableFilters('imports')
  const filterUrl = useUrlFilters(filterMeta.data ?? [])
  const field = (i: ImportListItem, key: string): unknown => {
    const fields: Record<string, unknown> = {
      impQ: [i.fileName, i.uploadedBy],
      impApplied: i.appliedAt != null,
      impUploaded: i.uploadedAt,
      impErrors: Number(i.summary.errors) > 0,
    }
    return fields[key]
  }
  const history = (imports.data ?? []).filter((i) => matchesFilters(i, filterUrl.filters, filterUrl.values, field))

  const download = (path: string, name: string) =>
    void downloadFile(`${importBase(kind)}${path}`, name, () => undefined, new AbortController().signal).catch((e: unknown) =>
      setError(problemMessage(e)),
    )

  const open = async (publicId: string) => {
    setError(null)
    try {
      setCurrent(await getImport(kind, publicId))
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  if (definition.isPending) return <CircularProgress aria-label={sq.errors.loading} />
  if (definition.isError) return <Alert severity="error">{problemMessage(definition.error)}</Alert>
  const d = definition.data

  return (
    <Stack spacing={3}>
      <Instructions definition={d} />

      <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
        <Button variant="outlined" startIcon={<Download size={18} aria-hidden />} onClick={() => download('/template', d.templateFileName)}>
          {t.template}
        </Button>
        <Button
          variant="contained"
          startIcon={upload.isPending ? <CircularProgress size={18} aria-hidden /> : <Upload size={18} aria-hidden />}
          disabled={upload.isPending}
          onClick={() => input.current?.click()}
        >
          {upload.isPending ? t.uploading : t.upload}
        </Button>
        <Typography variant="body2" color="text.secondary">
          {t.accept(Number(d.maxRows), Number(d.maxFileMb))}
        </Typography>
        <input
          ref={input}
          type="file"
          hidden
          accept=".xlsx,.csv"
          aria-label={t.upload}
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ''
            if (!file) return
            setError(null)
            upload.mutate(file, { onSuccess: setCurrent, onError: (err) => setError(problemMessage(err)) })
          }}
        />
      </Stack>
      {error && <Alert severity="error">{error}</Alert>}

      {current && (
        <Preview
          view={current}
          applying={apply.isPending}
          onApply={() =>
            apply.mutate(current.publicId, {
              onSuccess: (view) => {
                setCurrent(view)
                setToast(t.applied(Number(view.appliedCount ?? 0)))
              },
              onError: (err) => setError(problemMessage(err)),
            })
          }
          onReport={() => download(`/${current.publicId}/report`, `${current.fileName}-rezultati.xlsx`)}
        />
      )}

      <Box>
        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
          {t.history}
        </Typography>
        {imports.isPending && <CircularProgress aria-label={sq.errors.loading} />}
        {imports.isSuccess && imports.data.length === 0 && <Typography color="text.secondary">{t.noImports}</Typography>}
        {imports.isSuccess && imports.data.length > 0 && <TableFilterBar meta={filterMeta} url={filterUrl} />}
        {imports.isSuccess && imports.data.length > 0 && history.length === 0 && (
          <Typography color="text.secondary">{sq.filters.noMatch}</Typography>
        )}
        {history.length > 0 && (
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label={t.history}>
              <TableBody>
                {history.map((i) => (
                  <TableRow key={i.publicId} hover selected={i.publicId === current?.publicId}>
                    <TableCell>{i.fileName}</TableCell>
                    <TableCell>
                      {formatDateTime(i.uploadedAt)} · {i.uploadedBy}
                    </TableCell>
                    <TableCell>{i.appliedAt ? t.appliedAt(formatDateTime(i.appliedAt), i.appliedBy ?? '') : t.notApplied}</TableCell>
                    <TableCell align="right">
                      <Button size="small" onClick={() => void open(i.publicId)}>
                        {t.open}
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Paper>
        )}
      </Box>
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

/** "Si duhet të jetë skedari": the definition's purpose, formats, columns (with an example row) and rules. */
function Instructions({ definition: d }: { definition: ImportDefinitionView }) {
  return (
    <SectionCard title={t.instructionsTitle}>
      <Stack spacing={2}>
        <Typography>{d.purpose}</Typography>
        <Box>
          <Typography variant="h5" component="h3">
            {t.formats}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {d.formats.map((f) => (
              <li key={f}>{f}</li>
            ))}
          </Box>
        </Box>
        <Box>
          <Typography variant="h5" component="h3" sx={{ mb: 1 }}>
            {t.columnsTitle}
          </Typography>
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label={t.columnsTitle}>
              <TableHead>
                <TableRow>
                  <TableCell>{t.columns.name}</TableCell>
                  <TableCell>{t.columns.required}</TableCell>
                  <TableCell>{t.columns.description}</TableCell>
                  <TableCell>{t.columns.example}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {d.columns.map((c) => (
                  <TableRow key={c.name}>
                    <TableCell sx={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>{c.name}</TableCell>
                    <TableCell>{c.requirementText}</TableCell>
                    <TableCell>{c.description}</TableCell>
                    <TableCell sx={{ fontFamily: 'monospace' }}>{c.example}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        </Box>
        <Box>
          <Typography variant="h5" component="h3">
            {t.rulesTitle}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 3 }}>
            {d.rules.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </Box>
        </Box>
      </Stack>
    </SectionCard>
  )
}

function Preview({
  view,
  applying,
  onApply,
  onReport,
}: {
  view: ImportView
  applying: boolean
  onApply: () => void
  onReport: () => void
}) {
  const s = view.summary
  const writable = Number(s.new) + Number(s.changed)
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Typography variant="h3" component="h2">
          {t.previewTitle(view.fileName)}
        </Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }} role="status">
          {Object.entries(t.summary).map(([key, label]) => (
            <Chip key={key} label={`${label}: ${String(s[key as keyof typeof s])}`} variant={key === 'errors' ? 'outlined' : 'filled'} />
          ))}
        </Stack>
        {view.appliedAt ? (
          <Alert severity="success">{t.appliedAt(formatDateTime(view.appliedAt), view.appliedBy ?? '')}</Alert>
        ) : (
          <Alert severity="info">{t.previewNote}</Alert>
        )}
        <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          {!view.appliedAt && (
            <Button variant="contained" color="secondary" onClick={onApply} disabled={applying || writable === 0}>
              {t.apply}
            </Button>
          )}
          <Button variant="outlined" startIcon={<FileSpreadsheet size={18} aria-hidden />} onClick={onReport}>
            {t.report}
          </Button>
          {!view.appliedAt && writable === 0 && (
            <Typography variant="body2" color="text.secondary">
              {t.nothingToApply}
            </Typography>
          )}
        </Stack>
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t.previewTitle(view.fileName)}>
            <TableHead>
              <TableRow>
                <TableCell>{t.rowColumns.line}</TableCell>
                <TableCell>{t.rowColumns.key}</TableCell>
                <TableCell>{t.rowColumns.name}</TableCell>
                <TableCell>{t.rowColumns.status}</TableCell>
                <TableCell>{t.rowColumns.details}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {view.rows.map((r) => (
                <TableRow key={`${r.line}-${r.key ?? ''}`}>
                  <TableCell>{r.line}</TableCell>
                  <TableCell>{r.key}</TableCell>
                  <TableCell>{r.name}</TableCell>
                  <TableCell>
                    <Chip size="small" color={statusColor(r.status)} label={t.statuses[r.status] ?? r.status} />
                  </TableCell>
                  <TableCell>
                    <Details row={r} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      </Stack>
    </Paper>
  )
}

/** The reason per cell (column: message), the changes (column: old → new), or the row's message. */
function Details({ row }: { row: ImportRowView }) {
  return (
    <Stack spacing={0.5}>
      {row.errors.map((e) => (
        <Typography key={`${e.column}-${e.message}`} variant="body2" color="error">
          <strong>{e.column || t.rowError}:</strong> {e.message}
        </Typography>
      ))}
      {row.changes.map((c) => (
        <Typography key={c.column} variant="body2">
          <strong>{c.column}:</strong>{' '}
          {c.old && (
            <Typography component="span" variant="body2" color="text.secondary" sx={{ textDecoration: 'line-through' }}>
              {c.old}
            </Typography>
          )}
          {c.old && ' → '}
          {c.new ?? '—'}
        </Typography>
      ))}
      {row.message && (
        <Typography variant="body2" color="text.secondary">
          {row.message}
        </Typography>
      )}
    </Stack>
  )
}
