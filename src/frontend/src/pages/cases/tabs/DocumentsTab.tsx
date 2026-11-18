import { Alert, Box, Button, CircularProgress, Divider, Stack, Typography } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { Download, Eye, FileImage, FileText, File as FileIcon, Trash2 } from 'lucide-react'
import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import { DeleteDocumentDialog, DocumentPreviewDialog } from '../../../components/documents/DocumentDialogs'
import { UploadQueue } from '../../../components/documents/UploadQueue'
import { hasValue, rangeOf, textOf } from '../../../components/filters/filterTypes'
import { TableFilterBar } from '../../../components/filters/TableFilterBar'
import { useUrlFilters } from '../../../components/filters/useUrlFilters'
import { PaginationFooter } from '../../../components/ui/DataTable'
import { EmptyState } from '../../../components/ui/EmptyState'
import { SectionCard } from '../../../components/ui/SectionCard'
import { StatusChip } from '../../../components/ui/StatusChip'
import { SuccessToast } from '../../../components/ui/SuccessToast'
import { afterCaseChange, type CaseDetail } from '../../../features/cases/api'
import {
  fetchDocument,
  saveBlob,
  useDocuments,
  type DocumentFilters,
  type DocumentItem,
  type FetchedFile,
} from '../../../features/documents/api'
import { useTableFilters } from '../../../features/filters/api'
import { sq } from '../../../i18n/sq'
import { formatDateTime } from '../../../lib/dates'
import { problemMessage } from '../../../lib/problem'
import { pageKey, useKeysetPaging } from '../../../lib/paging'
import { serverQuery } from '../../../lib/search'
import { useDebounced } from '../../../lib/useDebounced'
import { formatSize } from '../../../lib/uploads'

const t = sq.cases.documents

/** URL prefix of this tab's page and size (docspage, docssize). */
const DOCS = 'docs'

/**
 * A just-uploaded document still "Në kontroll" after this long gets the slow-scan note. The list itself reloads on
 * the "document.status" push (NotificationsProvider, task 040) and once after a reconnect — no polling.
 */
const SLOW_SCAN_MS = 2 * 60 * 1000

/** A fallback download name when the response has none: the title plus the type's usual extension. */
const fallbackName = (d: DocumentItem) =>
  `${d.title}.${d.contentType === 'application/pdf' ? 'pdf' : d.contentType.startsWith('image/') ? d.contentType.slice(6) : 'bin'}`

/**
 * Dokumentet (task 035): the dossier's documents as one timeline grouped by level (X/2 above X/1), with filters;
 * the upload queue (document.upload); "Shiko" / "Shkarko" through the API as a blob; "Hiq" (document.delete, for
 * documents of THIS level — the API checks the level the document belongs to).
 */
export function DocumentsTab({ detail }: { detail: CaseDetail }) {
  const caseId = detail.header.publicId
  const qc = useQueryClient()
  // task 053m: the shared FilterBar (GET /api/filters/documents?case=…); filters and page in the URL (prefix "docs"),
  // so Back and refresh keep them
  const meta = useTableFilters('documents', { case: caseId })
  const url = useUrlFilters(meta.data ?? [], { resetParams: [pageKey(DOCS)] })
  const filters: DocumentFilters = {
    type: textOf(url.values, 'type'),
    level: textOf(url.values, 'level'),
    status: textOf(url.values, 'status'),
    q: useDebounced(serverQuery(textOf(url.values, 'q')), 300),
    from: rangeOf(url.values, 'uploaded', 'from'),
    to: rangeOf(url.values, 'uploaded', 'to'),
  }
  const paging = useKeysetPaging(DOCS)
  const documents = useDocuments(caseId, filters, { cursor: paging.cursor, size: paging.size })
  const items = useMemo(() => documents.data?.items ?? [], [documents.data])
  const can = (code: string) => detail.permissions.includes(code)

  // just uploaded and still "Në kontroll" after 2 minutes → the slow-scan note (the push updates the row itself)
  const [watched, setWatched] = useState<Record<string, number>>({})
  const [slow, setSlow] = useState<Set<string>>(new Set())
  const pending = items.filter((d) => d.scanStatus === 'PENDING' && watched[d.publicId] !== undefined && !slow.has(d.publicId))
  const pendingKey = pending.map((d) => d.publicId).join(',')
  useEffect(() => {
    if (!pendingKey) return
    const ids = pendingKey.split(',')
    const due = Math.min(...ids.map((id) => watched[id] + SLOW_SCAN_MS)) - Date.now()
    const timer = setTimeout(() => {
      const now = Date.now()
      setSlow((s) => new Set([...s, ...ids.filter((id) => now - watched[id] >= SLOW_SCAN_MS)]))
    }, Math.max(0, due))
    return () => clearTimeout(timer)
  }, [pendingKey, watched])

  const [busy, setBusy] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [preview, setPreview] = useState<{ title: string; file: FetchedFile; url: string } | null>(null)
  // the open preview's blob URL, revoked on close — and when the page is left with the preview open
  const previewUrl = useRef<string | null>(null)
  useEffect(
    () => () => {
      if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    },
    [],
  )
  const closePreview = () => {
    if (previewUrl.current) URL.revokeObjectURL(previewUrl.current)
    previewUrl.current = null
    setPreview(null)
  }
  const [deleting, setDeleting] = useState<DocumentItem | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const open = async (d: DocumentItem, mode: 'download' | 'preview') => {
    setBusy(d.publicId)
    setActionError(null)
    try {
      const file = await fetchDocument(d.publicId, mode, fallbackName(d))
      if (mode === 'preview') {
        previewUrl.current = URL.createObjectURL(file.blob)
        setPreview({ title: d.title, file, url: previewUrl.current })
      } else {
        saveBlob(file)
      }
    } catch (error) {
      setActionError(problemMessage(error))
    } finally {
      setBusy(null)
    }
  }

  // X/2 above X/1; a level only when it has documents in the list
  const levels = [...detail.levels].sort((a, b) => Number(b.levelSeq) - Number(a.levelSeq))
  const groups = levels
    .map((l) => ({ code: l.caseCode, items: items.filter((d) => d.level === l.caseCode) }))
    .filter((g) => g.items.length > 0)
  const filtered = url.filters.some((f) => hasValue(url.values[f.key]))

  return (
    <Stack spacing={2}>
      {can('document.upload') && (
        <SectionCard title={t.uploadTitle}>
          <UploadQueue
            caseId={caseId}
            onUploaded={(id) => {
              setWatched((w) => ({ ...w, [id]: Date.now() }))
              void afterCaseChange(qc, caseId) // the list, the tab count, the history
            }}
          />
        </SectionCard>
      )}

      <SectionCard title={t.title}>
        <Stack spacing={2}>
          {/* type, level, status on top; "+ Shto filtër" for the title and the upload days */}
          <TableFilterBar meta={meta} url={url} />

          {actionError && (
            <Alert severity="error" onClose={() => setActionError(null)}>
              {actionError}
            </Alert>
          )}
          {documents.isError && <Alert severity="error">{problemMessage(documents.error)}</Alert>}
          {documents.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : items.length === 0 ? (
            <EmptyState title={filtered ? t.emptyFiltered : t.empty} />
          ) : (
            groups.map((group) => (
              <Box key={group.code} component="section" aria-label={group.code}>
                <Typography variant="body2" sx={{ fontWeight: 700, color: 'text.secondary', mb: 0.5 }}>
                  {group.code}
                </Typography>
                <Stack component="ul" sx={{ m: 0, p: 0, listStyle: 'none' }}>
                  {group.items.map((d, index) => (
                    <Fragment key={d.publicId}>
                      {index > 0 && <Divider component="li" aria-hidden />}
                      <DocumentRow
                        document={d}
                        slow={slow.has(d.publicId) && d.scanStatus === 'PENDING'}
                        busy={busy === d.publicId}
                        canDelete={can('document.delete') && d.level === detail.header.caseCode}
                        onPreview={() => void open(d, 'preview')}
                        onDownload={() => void open(d, 'download')}
                        onDelete={() => setDeleting(d)}
                      />
                    </Fragment>
                  ))}
                </Stack>
              </Box>
            ))
          )}
          <PaginationFooter
            mode="keyset"
            paging={paging}
            count={items.length}
            total={Number(documents.data?.total ?? 0)}
            nextCursor={documents.data?.nextCursor}
            busy={documents.isFetching}
            stale={documents.isPlaceholderData}
          />
        </Stack>
      </SectionCard>

      {preview && <DocumentPreviewDialog title={preview.title} file={preview.file} url={preview.url} onClose={closePreview} />}
      {deleting && (
        <DeleteDocumentDialog
          caseId={caseId}
          document={deleting}
          onClose={() => setDeleting(null)}
          onDone={() => {
            setToast(t.deleted(deleting.title))
            setDeleting(null)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

function TypeIcon({ contentType }: { contentType: string }) {
  const Icon = contentType === 'application/pdf' ? FileText : contentType.startsWith('image/') ? FileImage : FileIcon
  return (
    <Box aria-hidden sx={{ color: 'primary.main', display: 'flex', pt: 0.25 }}>
      <Icon size={22} />
    </Box>
  )
}

function DocumentRow({
  document: d,
  slow,
  busy,
  canDelete,
  onPreview,
  onDownload,
  onDelete,
}: {
  document: DocumentItem
  slow: boolean
  busy: boolean
  canDelete: boolean
  onPreview: () => void
  onDownload: () => void
  onDelete: () => void
}) {
  const type = d.otherTypeText ? `${d.documentType.name}: ${d.otherTypeText}` : d.documentType.name
  return (
    // on a phone the actions go below the text (beside it they would squeeze the title to a letter per line)
    <Box
      component="li"
      sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: { xs: 0.5, md: 1.5 }, py: 1.25, alignItems: { md: 'flex-start' } }}
    >
      <Box sx={{ display: 'flex', gap: 1.5, flex: 1, minWidth: 0 }}>
        <TypeIcon contentType={d.contentType} />
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>{d.title}</Typography>
            <StatusChip kind="scan" value={d.scanStatus} />
          </Stack>
          <Typography variant="body2" color="text.secondary">
            {t.meta(type, d.uploadedBy ?? t.unknownUploader, formatDateTime(d.uploadedAt), formatSize(Number(d.sizeBytes)))}
          </Typography>
          {slow && (
            <Typography variant="caption" color="warning.dark">
              {t.slowScan}
            </Typography>
          )}
        </Box>
      </Box>
      <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexShrink: 0, flexWrap: 'wrap', ml: { xs: 4, md: 0 } }}>
        {d.canPreview && (
          <Button size="small" startIcon={<Eye size={16} aria-hidden />} onClick={onPreview} disabled={busy} aria-label={t.viewFor(d.title)}>
            {t.view}
          </Button>
        )}
        {d.scanStatus === 'CLEAN' && (
          <Button size="small" startIcon={<Download size={16} aria-hidden />} onClick={onDownload} disabled={busy} aria-label={t.downloadFor(d.title)}>
            {t.download}
          </Button>
        )}
        {canDelete && (
          <Button size="small" color="error" startIcon={<Trash2 size={16} aria-hidden />} onClick={onDelete} aria-label={t.deleteFor(d.title)}>
            {t.delete}
          </Button>
        )}
      </Stack>
    </Box>
  )
}
