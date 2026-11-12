import { Alert, Button, Stack, Typography } from '@mui/material'
import { FileSpreadsheet, X } from 'lucide-react'
import { useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { sq } from '../../i18n/sq'
import { downloadFile, formatMegabytes } from '../../lib/download'
import { formatCount } from '../../lib/numbers'
import { problemMessage } from '../../lib/problem'

const t = sq.reports

/** The API's export limit (Reports:MaxExportRows); the server refuses above it with the same message. */
export const MAX_EXPORT_ROWS = 200_000

const grouped = formatCount

/**
 * "Eksporto në Excel" (task 045) — the page's main (gold) action. Shows how many rows the export will hold (the current
 * total); above the limit it is disabled with the server's message. The download streams: progress is the size received
 * so far (no Content-Length, so no percentage) and "Anulo" stops it. The file name comes from Content-Disposition.
 */
export function ExportButton({
  url,
  rows,
  fallbackName,
}: {
  /** the export URL with the current filters */
  url: string
  /** the current total; undefined while it loads (the statistics: no row count shown) */
  rows: number | undefined
  fallbackName: string
}) {
  const [bytes, setBytes] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const abort = useRef<AbortController | null>(null)
  const tooMany = rows !== undefined && rows > MAX_EXPORT_ROWS

  const start = async () => {
    const controller = new AbortController()
    abort.current = controller
    setError(null)
    setBytes(0)
    try {
      const result = await downloadFile(url, fallbackName, setBytes, controller.signal)
      setDone(t.exported(result.fileName))
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') setDone(t.exportCancelled)
      else if (e instanceof ApiError && (e.status === 503 || e.status === 504)) setError(t.exportTooSlow)
      else setError(e instanceof ApiError ? problemMessage(e) : t.exportFailed)
    } finally {
      setBytes(null)
      abort.current = null
    }
  }

  return (
    <Stack spacing={0.5} sx={{ alignItems: { xs: 'stretch', sm: 'flex-end' } }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        {bytes !== null ? (
          <>
            <Typography variant="body2" role="status" aria-live="polite">
              {t.exporting(formatMegabytes(bytes))}
            </Typography>
            <Button variant="outlined" startIcon={<X size={18} aria-hidden />} onClick={() => abort.current?.abort()}>
              {t.cancel}
            </Button>
          </>
        ) : (
          <Button
            variant="contained"
            color="secondary"
            startIcon={<FileSpreadsheet size={20} aria-hidden />}
            onClick={() => void start()}
            disabled={tooMany}
          >
            {t.export}
          </Button>
        )}
      </Stack>
      {rows !== undefined && !tooMany && (
        <Typography variant="caption" color="text.secondary">
          {t.exportRows(grouped(rows))}
        </Typography>
      )}
      {tooMany && (
        <Typography variant="caption" color="error" sx={{ maxWidth: 360, textAlign: { sm: 'right' } }}>
          {t.exportTooMany(grouped(rows), grouped(MAX_EXPORT_ROWS))}
        </Typography>
      )}
      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ maxWidth: 420 }}>
          {error}
        </Alert>
      )}
      <SuccessToast message={done} onClose={() => setDone(null)} />
    </Stack>
  )
}
