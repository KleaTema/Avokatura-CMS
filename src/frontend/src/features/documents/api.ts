import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, ApiError, authHeaders, notifyUnauthorized, unwrap, type ProblemDetails } from '../../api/client'
import type { components } from '../../api/schema'
import { fileNameFromDisposition } from '../../lib/uploads'
import { afterCaseChange, caseKeys, type PageRequest } from '../cases/api'

export type DocumentItem = components['schemas']['DocumentListItem']
export type UploadDocumentResponse = components['schemas']['UploadDocumentResponse']

export interface DocumentFilters {
  /** document type code, '' = all */
  type: string
  /** n of X/n, '' = all levels */
  level: string
  /** scan status (PENDING / CLEAN / INFECTED / FAILED), '' = all */
  status: string
}

/** One page of the dossier timeline (every level the dossier has), newest first, with the total. */
export const useDocuments = (caseId: string, f: DocumentFilters, p: PageRequest) =>
  useQuery({
    queryKey: [...caseKeys.documents(caseId), f, p.cursor ?? null, p.size] as const,
    placeholderData: keepPreviousData,
    queryFn: () =>
      api
        .GET('/api/cases/{id}/documents', {
          params: {
            path: { id: caseId },
            query: {
              type: f.type || undefined,
              level: f.level ? Number(f.level) : undefined,
              status: f.status || undefined,
              after: p.cursor,
              limit: p.size,
            },
          },
        })
        .then(unwrap),
  })

/** How often the decision step looks again while a just-uploaded decision is "Në kontroll". TODO(task-040): SignalR. */
const DECISION_POLL_MS = 3000

/**
 * The dossier's documents of type VENDIM (every level, newest first, at most 50) — the reasoned decision is uploaded or
 * picked from these (task 039). Polled every 3 s while `poll` (a decision being checked).
 */
export const useDecisionDocuments = (caseId: string, poll: boolean) =>
  useQuery({
    queryKey: [...caseKeys.documents(caseId), 'decision-picker'] as const,
    refetchInterval: poll ? DECISION_POLL_MS : false,
    queryFn: () =>
      api
        .GET('/api/cases/{id}/documents', { params: { path: { id: caseId }, query: { type: 'VENDIM', limit: 50 } } })
        .then(unwrap)
        .then((page) => page.items),
  })

const problemSchema =z.object({ title: z.string().nullish(), code: z.string().optional() }).passthrough()
const uploadSchema = z.object({
  documentPublicId: z.string(),
  scanStatus: z.string(),
  warnings: z.array(z.object({ code: z.string(), message: z.string() })),
})

const toProblem = (text: string): ProblemDetails | null => {
  try {
    const parsed = problemSchema.safeParse(JSON.parse(text) as unknown)
    return parsed.success ? (parsed.data as ProblemDetails) : null
  } catch {
    return null
  }
}

/** The upload was stopped by the user (Anulo). */
export class UploadCancelled extends Error {}

/**
 * POST /api/cases/{id}/documents with XMLHttpRequest — fetch reports no upload progress. The form already has the
 * fields before the file (documentForm). Resolves with the response, rejects with ApiError (HTTP error),
 * UploadCancelled (signal) or a TypeError-like Error (network).
 */
export async function uploadDocument(
  caseId: string,
  form: FormData,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<UploadDocumentResponse> {
  const headers = await authHeaders()
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `/api/cases/${caseId}/documents`)
    for (const [name, value] of Object.entries(headers)) xhr.setRequestHeader(name, value)
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress(e.loaded / e.total)
    }

    xhr.onload = () => {
      if (xhr.status === 201) {
        const parsed = uploadSchema.safeParse(JSON.parse(xhr.responseText) as unknown)
        if (parsed.success) resolve(parsed.data)
        else reject(new ApiError(xhr.status))
        return
      }

      if (xhr.status === 401) notifyUnauthorized()
      reject(new ApiError(xhr.status, toProblem(xhr.responseText)))
    }
    xhr.onerror = () => reject(new Error('network'))
    xhr.onabort = () => reject(new UploadCancelled('cancelled'))
    if (signal.aborted) {
      reject(new UploadCancelled('cancelled'))
      return
    }

    signal.addEventListener('abort', () => xhr.abort(), { once: true })
    xhr.send(form)
  })
}

export interface FetchedFile {
  blob: Blob
  fileName: string
}

/**
 * The file through the API WITH the login header, as a blob (an <iframe> or <a href> could not send the header).
 * The API checks access and writes the audit row (DOCUMENT_DOWNLOAD) for each call.
 */
export async function fetchDocument(documentId: string, mode: 'download' | 'preview', fallbackName: string): Promise<FetchedFile> {
  const response = await fetch(`/api/documents/${documentId}/${mode}`, { headers: await authHeaders() })
  if (!response.ok) {
    if (response.status === 401) notifyUnauthorized()
    throw new ApiError(response.status, toProblem(await response.text()))
  }

  return {
    blob: await response.blob(),
    fileName: fileNameFromDisposition(response.headers.get('Content-Disposition'), fallbackName),
  }
}

/** Saves a blob under its name (the original file name), then frees it. */
export function saveBlob(file: FetchedFile) {
  const url = URL.createObjectURL(file.blob)
  const link = document.createElement('a')
  link.href = url
  link.download = file.fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000) // after the browser has started the download
}

/** DELETE /api/documents/{docId} with the reason; the case's counts, history and the timeline reload. */
export const useDeleteDocument = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ documentId, reason }: { documentId: string; reason: string }) =>
      api.DELETE('/api/documents/{docId}', { params: { path: { docId: documentId } }, body: { reason } }).then(unwrap),
    onSuccess: () => afterCaseChange(qc, caseId),
  })
}
