import { Alert, Box, Button, IconButton, LinearProgress, MenuItem, Paper, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { FileUp, RotateCcw, Upload, X } from 'lucide-react'
import { useEffect, useRef, useState, type DragEvent } from 'react'
import { ApiError } from '../../api/client'
import { uploadDocument, UploadCancelled } from '../../features/documents/api'
import { useAllowedFileTypes, useDocumentTypes } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { acceptOf, documentForm, formatSize, preCheck, titleOf } from '../../lib/uploads'

const t = sq.cases.documents
const MAX_TITLE = 255

type ItemState = 'ready' | 'invalid' | 'queued' | 'uploading' | 'done' | 'error' | 'cancelled'

interface QueueItem {
  id: string
  file: File
  typeCode: string
  otherText: string
  title: string
  state: ItemState
  /** 0..1 while uploading */
  progress: number
  /** the reason it cannot be or was not uploaded */
  error: string | null
  /** the server's warnings for an uploaded file (e.g. the same file already in the dossier) */
  warnings: string[]
  fieldErrors: { type?: string; other?: string; title?: string }
}

/** What a failed upload says: the server's field messages when it sent some, otherwise its message. */
function uploadError(error: unknown): string {
  if (error instanceof ApiError && error.problem?.errors) {
    const messages = Object.values(error.problem.errors).flat()
    if (messages.length > 0) return messages.join(' ')
  }

  return error instanceof ApiError ? problemMessage(error) : t.networkError
}

/**
 * The upload area of the Dokumentet tab (document.upload): files dropped or chosen become rows of a queue, where the
 * type (Tjetër → text) and the title are set; "Ngarko" then uploads them ONE AFTER ANOTHER with a progress bar each
 * (XMLHttpRequest), cancel and retry per file. The extension and size are checked here first (fail fast); the
 * server checks again, including the file's first bytes.
 */
export function UploadQueue({ caseId, onUploaded }: { caseId: string; onUploaded: (documentId: string) => void }) {
  const documentTypes = useDocumentTypes()
  const allowed = useAllowedFileTypes()
  const [items, setItems] = useState<QueueItem[]>([])
  const [dragging, setDragging] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  // the queue as the runner sees it NOW (state updates are applied on the next render)
  const current = useRef<QueueItem[]>([])
  const running = useRef(false)
  const controllers = useRef(new Map<string, AbortController>())

  const change = (next: (list: QueueItem[]) => QueueItem[]) => {
    current.current = next(current.current)
    setItems(current.current)
  }
  const patch = (id: string, values: Partial<QueueItem>) => change((list) => list.map((i) => (i.id === id ? { ...i, ...values } : i)))

  // leaving the page stops the uploads still running
  useEffect(() => {
    const active = controllers.current
    return () => active.forEach((c) => c.abort())
  }, [])

  const types = documentTypes.data ?? []
  const isOther = (code: string) => types.some((x) => x.code === code && x.isOther)
  const allowedTypes = allowed.data ?? []
  const extensions = acceptOf(allowedTypes)

  const addFiles = (files: FileList | null) => {
    if (!files || files.length === 0) return
    const added = Array.from(files).map((file): QueueItem => {
      const problem = preCheck(file, allowedTypes)
      return {
        id: crypto.randomUUID(),
        file,
        typeCode: '',
        otherText: '',
        title: titleOf(file.name).slice(0, MAX_TITLE),
        state: problem ? 'invalid' : 'ready',
        progress: 0,
        error: problem,
        warnings: [],
        fieldErrors: {},
      }
    })
    change((list) => [...list, ...added])
  }

  /** Type chosen, "Tjetër" described, title not too long — otherwise the row shows what is missing. */
  const validate = (item: QueueItem): QueueItem['fieldErrors'] => ({
    type: item.typeCode ? undefined : t.typeRequired,
    other: isOther(item.typeCode) && !item.otherText.trim() ? t.otherRequired : undefined,
    title: item.title.trim().length > MAX_TITLE ? t.titleTooLong : undefined,
  })

  const queue = (ids: string[]) => {
    change((list) =>
      list.map((i) => {
        if (!ids.includes(i.id)) return i
        const fieldErrors = validate(i)
        return Object.values(fieldErrors).some(Boolean) ? { ...i, fieldErrors } : { ...i, fieldErrors, state: 'queued', error: null }
      }),
    )
    void run()
  }

  const run = async () => {
    if (running.current) return
    running.current = true
    try {
      for (let next = current.current.find((i) => i.state === 'queued'); next; next = current.current.find((i) => i.state === 'queued')) {
        await uploadOne(next)
      }
    } finally {
      running.current = false
    }
  }

  const uploadOne = async (item: QueueItem) => {
    const controller = new AbortController()
    controllers.current.set(item.id, controller)
    patch(item.id, { state: 'uploading', progress: 0, error: null, warnings: [] })
    try {
      const form = documentForm(
        {
          documentTypeCode: item.typeCode,
          otherTypeText: isOther(item.typeCode) ? item.otherText.trim() : undefined,
          title: item.title.trim() || undefined,
        },
        item.file,
        item.file.name,
      )
      const result = await uploadDocument(caseId, form, (progress) => patch(item.id, { progress }), controller.signal)
      patch(item.id, { state: 'done', progress: 1, warnings: result.warnings.map((w) => w.message) })
      onUploaded(result.documentPublicId)
    } catch (error) {
      patch(item.id, error instanceof UploadCancelled
        ? { state: 'cancelled', error: t.cancelled }
        : { state: 'error', error: uploadError(error) })
    } finally {
      controllers.current.delete(item.id)
    }
  }

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setDragging(false)
    addFiles(e.dataTransfer.files)
  }

  const ready = items.filter((i) => i.state === 'ready')
  const done = items.filter((i) => i.state === 'done')

  return (
    <Stack spacing={2}>
      <Box
        onDragOver={(e) => {
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        sx={{
          border: 2,
          borderStyle: 'dashed',
          borderColor: dragging ? 'primary.main' : 'divider',
          bgcolor: dragging ? 'action.hover' : 'transparent',
          borderRadius: 2,
          p: { xs: 2, md: 3 },
          textAlign: 'center',
        }}
      >
        <Stack spacing={1} sx={{ alignItems: 'center' }}>
          <Box aria-hidden sx={{ color: 'text.secondary' }}>
            <FileUp size={32} strokeWidth={1.5} />
          </Box>
          <Typography>{t.dropHere}</Typography>
          <Button variant="outlined" onClick={() => input.current?.click()}>
            {t.choose}
          </Button>
          {extensions && (
            <Typography variant="caption" color="text.secondary">
              {t.allowed(extensions.replaceAll(',', ', '))}
            </Typography>
          )}
          <input
            ref={input}
            type="file"
            multiple
            hidden
            accept={extensions}
            onChange={(e) => {
              addFiles(e.target.files)
              e.target.value = ''
            }}
          />
        </Stack>
      </Box>

      {items.length > 0 && (
        <Stack spacing={1.5} component="ul" sx={{ m: 0, p: 0, listStyle: 'none' }}>
          {items.map((item) => (
            <QueueRow
              key={item.id}
              item={item}
              types={types}
              otherType={isOther(item.typeCode)}
              onChange={(values) => patch(item.id, { ...values, fieldErrors: {} })}
              onRemove={() => change((list) => list.filter((i) => i.id !== item.id))}
              onCancel={() => controllers.current.get(item.id)?.abort()}
              onRetry={() => queue([item.id])}
            />
          ))}
        </Stack>
      )}

      {items.length > 0 && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ justifyContent: 'flex-end', flexWrap: 'wrap' }}>
          {done.length > 0 && (
            <Button onClick={() => change((list) => list.filter((i) => i.state !== 'done'))}>{t.clearDone}</Button>
          )}
          <Button
            variant="contained"
            color="secondary"
            startIcon={<Upload size={20} aria-hidden />}
            disabled={ready.length === 0}
            onClick={() => queue(ready.map((i) => i.id))}
          >
            {ready.length > 0 ? `${t.upload} (${ready.length})` : t.upload}
          </Button>
        </Stack>
      )}
    </Stack>
  )
}

function QueueRow({
  item,
  types,
  otherType,
  onChange,
  onRemove,
  onCancel,
  onRetry,
}: {
  item: QueueItem
  types: { code: string; name: string }[]
  otherType: boolean
  onChange: (values: Partial<QueueItem>) => void
  onRemove: () => void
  onCancel: () => void
  onRetry: () => void
}) {
  const editable = item.state === 'ready' || item.state === 'error' || item.state === 'cancelled'
  const busy = item.state === 'uploading' || item.state === 'queued'
  const percent = Math.round(item.progress * 100)
  return (
    <Paper component="li" variant="outlined" sx={{ p: 1.5 }}>
      <Stack spacing={1.25}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
          <Typography sx={{ fontWeight: 600, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {item.file.name}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
            {formatSize(item.file.size)}
          </Typography>
          {(item.state === 'error' || item.state === 'cancelled') && (
            <Tooltip title={t.retry}>
              <IconButton size="small" aria-label={`${t.retry}: ${item.file.name}`} onClick={onRetry}>
                <RotateCcw size={18} />
              </IconButton>
            </Tooltip>
          )}
          <Tooltip title={busy ? t.cancel : t.remove}>
            <IconButton
              size="small"
              aria-label={`${busy ? t.cancel : t.remove}: ${item.file.name}`}
              onClick={busy ? onCancel : onRemove}
            >
              <X size={18} />
            </IconButton>
          </Tooltip>
        </Stack>

        {item.state !== 'invalid' && item.state !== 'done' && (
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
            <TextField
              select
              size="small"
              label={t.type}
              value={item.typeCode}
              onChange={(e) => onChange({ typeCode: e.target.value })}
              disabled={!editable}
              required
              error={!!item.fieldErrors.type}
              helperText={item.fieldErrors.type}
              sx={{ width: { xs: '100%', md: 240 }, flexShrink: 0 }}
            >
              {types.map((x) => (
                <MenuItem key={x.code} value={x.code}>
                  {x.name}
                </MenuItem>
              ))}
            </TextField>
            {otherType && (
              <TextField
                size="small"
                label={t.otherType}
                value={item.otherText}
                onChange={(e) => onChange({ otherText: e.target.value })}
                disabled={!editable}
                required
                error={!!item.fieldErrors.other}
                helperText={item.fieldErrors.other}
                slotProps={{ htmlInput: { maxLength: 200 } }}
                sx={{ width: { xs: '100%', md: 220 }, flexShrink: 0 }}
              />
            )}
            <TextField
              size="small"
              label={t.documentTitle}
              value={item.title}
              onChange={(e) => onChange({ title: e.target.value })}
              disabled={!editable}
              error={!!item.fieldErrors.title}
              helperText={item.fieldErrors.title}
              slotProps={{ htmlInput: { maxLength: MAX_TITLE } }}
              sx={{ flex: 1 }}
            />
          </Stack>
        )}

        {busy && (
          <Box>
            <LinearProgress
              variant={item.state === 'queued' ? 'indeterminate' : 'determinate'}
              value={percent}
              aria-label={`${item.file.name}: ${t.uploading(percent)}`}
            />
            <Typography variant="caption" color="text.secondary">
              {item.state === 'queued' ? t.queued : t.uploading(percent)}
            </Typography>
          </Box>
        )}
        {item.state === 'done' && (
          <Typography variant="body2" color="success.dark">
            {t.uploaded}
          </Typography>
        )}
        {item.error && <Alert severity={item.state === 'cancelled' ? 'info' : 'error'}>{item.error}</Alert>}
        {item.warnings.map((warning) => (
          <Alert key={warning} severity="warning">
            {warning}
          </Alert>
        ))}
      </Stack>
    </Paper>
  )
}
