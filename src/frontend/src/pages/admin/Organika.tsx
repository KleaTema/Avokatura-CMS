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
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Upload } from 'lucide-react'
import { useRef, useState } from 'react'
import { api, ApiError, authHeaders, unwrap, type ProblemDetails } from '../../api/client'
import type { components } from '../../api/schema'
import { PageHeader } from '../../components/ui/PageHeader'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { sq } from '../../i18n/sq'
import { formatDateTime } from '../../lib/dates'
import { downloadFile } from '../../lib/download'
import { problemMessage } from '../../lib/problem'

const t = sq.organika

type OrgImport = components['schemas']['OrgImportView']
type OrgSummary = components['schemas']['OrgSummary']

const BASE = '/api/identity/admin/organika'
const importsKey = ['organika', 'imports'] as const

/** The rows a status chip warns about (nothing applied for them). */
const PROBLEM_STATUSES = new Set(['EMAIL_NOT_FOUND', 'DUPLICATE', 'MANAGER_NOT_FOUND', 'INVALID'])

/** POST the sheet as multipart (the generated client does not send files); a 400 comes back as ApiError. */
async function uploadSheet(file: File): Promise<OrgImport> {
  const form = new FormData()
  form.append('file', file, file.name)
  const response = await fetch(`${BASE}/imports`, { method: 'POST', body: form, headers: await authHeaders() })
  const body: unknown = await response.json().catch(() => null)
  if (!response.ok) throw new ApiError(response.status, body as ProblemDetails | undefined)
  return body as OrgImport
}

/**
 * "Organika" (task 053g, client decision E): the template, the upload with its preview (matched / e-mail not found /
 * duplicate / unchanged / changed) that applies NOTHING, "Apliko", and the kept imports.
 * TODO(akshi-q-hrmis): an integration with the HR system would replace the sheet.
 */
export function OrganikaPage() {
  const qc = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [current, setCurrent] = useState<OrgImport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const imports = useQuery({ queryKey: importsKey, queryFn: () => api.GET('/api/identity/admin/organika/imports').then(unwrap) })

  const upload = useMutation({
    mutationFn: uploadSheet,
    onSuccess: (view) => {
      setCurrent(view)
      void qc.invalidateQueries({ queryKey: importsKey })
    },
    onError: (e) => setError(problemMessage(e)),
  })
  const apply = useMutation({
    mutationFn: (publicId: string) =>
      api.POST('/api/identity/admin/organika/imports/{publicId}/apply', { params: { path: { publicId } } }).then(unwrap),
    onSuccess: (view) => {
      setCurrent(view)
      setToast(t.applied(Number(view.appliedCount ?? 0)))
      void qc.invalidateQueries({ queryKey: importsKey })
    },
    onError: (e) => setError(problemMessage(e)),
  })
  const open = async (publicId: string) => {
    setError(null)
    try {
      setCurrent(await api.GET('/api/identity/admin/organika/imports/{publicId}', { params: { path: { publicId } } }).then(unwrap))
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  return (
    <Stack spacing={3}>
      <PageHeader title={t.title} />
      <Typography color="text.secondary">{t.intro}</Typography>
      <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
        <Button
          variant="outlined"
          startIcon={<Download size={18} aria-hidden />}
          onClick={() => void downloadFile(`${BASE}/template`, 'organika-shabllon.xlsx', () => undefined, new AbortController().signal).catch((e: unknown) => setError(problemMessage(e)))}
        >
          {t.template}
        </Button>
        <Button
          variant="contained"
          color="secondary"
          startIcon={upload.isPending ? <CircularProgress size={18} aria-hidden /> : <Upload size={18} aria-hidden />}
          disabled={upload.isPending}
          onClick={() => input.current?.click()}
        >
          {upload.isPending ? t.uploading : t.upload}
        </Button>
        <Typography variant="body2" color="text.secondary">
          {t.accept}
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
            if (file) {
              setError(null)
              upload.mutate(file)
            }
          }}
        />
      </Stack>
      {error && <Alert severity="error">{error}</Alert>}

      {current && <Preview view={current} applying={apply.isPending} onApply={() => apply.mutate(current.publicId)} />}

      <Box>
        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
          {t.history}
        </Typography>
        {imports.isPending && <CircularProgress aria-label={sq.errors.loading} />}
        {imports.isSuccess && imports.data.length === 0 && <Typography color="text.secondary">{t.noImports}</Typography>}
        {imports.isSuccess && imports.data.length > 0 && (
          <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
            <Table size="small" aria-label={t.history}>
              <TableBody>
                {imports.data.map((i) => (
                  <TableRow key={i.publicId} hover selected={i.publicId === current?.publicId}>
                    <TableCell>{i.fileName}</TableCell>
                    <TableCell>
                      {formatDateTime(i.uploadedAt)} · {i.uploadedBy}
                    </TableCell>
                    <TableCell>
                      {i.appliedAt ? t.appliedAt(formatDateTime(i.appliedAt), i.appliedBy ?? '') : t.notApplied}
                    </TableCell>
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

function Preview({ view, applying, onApply }: { view: OrgImport; applying: boolean; onApply: () => void }) {
  const s: OrgSummary = view.summary
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Typography variant="h3" component="h2">
          {t.previewTitle(view.fileName)}
        </Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }} role="status">
          {(Object.keys(t.summary) as (keyof OrgSummary)[]).map((key) => (
            <Chip key={key} label={`${t.summary[key]}: ${s[key]}`} variant={key === 'changed' ? 'filled' : 'outlined'} />
          ))}
        </Stack>
        {view.appliedAt ? (
          <Alert severity="success">{t.appliedAt(formatDateTime(view.appliedAt), view.appliedBy ?? '')}</Alert>
        ) : (
          <Box>
            <Button variant="contained" color="secondary" onClick={onApply} disabled={applying || s.changed === 0}>
              {t.apply}
            </Button>
            {s.changed === 0 && (
              <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                {t.nothingToApply}
              </Typography>
            )}
          </Box>
        )}
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t.previewTitle(view.fileName)}>
            <TableHead>
              <TableRow>
                <TableCell>{t.columns.line}</TableCell>
                <TableCell>{t.columns.email}</TableCell>
                <TableCell>{t.columns.user}</TableCell>
                <TableCell>{t.columns.status}</TableCell>
                <TableCell>{t.columns.position}</TableCell>
                <TableCell>{t.columns.unit}</TableCell>
                <TableCell>{t.columns.manager}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {view.rows.map((r) => (
                <TableRow key={`${r.line}-${r.email ?? ''}`}>
                  <TableCell>{r.line}</TableCell>
                  <TableCell>{r.email}</TableCell>
                  <TableCell>{r.userName}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      color={PROBLEM_STATUSES.has(r.status) ? 'warning' : r.status === 'CHANGED' ? 'primary' : 'default'}
                      label={t.statuses[r.status] ?? r.status}
                      title={r.message ?? undefined}
                    />
                  </TableCell>
                  <TableCell>
                    <Change from={r.oldPosition} to={r.newPosition} />
                  </TableCell>
                  <TableCell>
                    <Change from={r.oldUnit} to={r.newUnit} />
                  </TableCell>
                  <TableCell>
                    <Change from={r.oldManager} to={r.newManager} />
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

/** "old → new" when it changes, otherwise the value. */
function Change({ from, to }: { from?: string | null; to?: string | null }) {
  if (from && to && from !== to) {
    return (
      <>
        <Typography component="span" color="text.secondary" sx={{ textDecoration: 'line-through' }}>
          {from}
        </Typography>{' '}
        → {to}
      </>
    )
  }
  return <>{to ?? from ?? ''}</>
}
