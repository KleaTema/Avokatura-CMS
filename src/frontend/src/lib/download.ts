import { ApiError, authHeaders, notifyUnauthorized, type ProblemDetails } from '../api/client'

/**
 * Task 045: downloading a streamed export. The API streams the file (no Content-Length), so progress is the size
 * received so far, never a percentage. The name comes from Content-Disposition; the file is saved through a blob URL.
 */

/**
 * The file name of a Content-Disposition header: filename* (RFC 5987, UTF-8''percent-encoded) wins over filename;
 * quotes removed. Null when there is none.
 */
export function fileNameFrom(header: string | null | undefined): string | null {
  if (!header) return null
  const star = /filename\*\s*=\s*([^;]+)/i.exec(header)
  if (star) {
    const value = star[1].trim().replace(/^"(.*)"$/, '$1')
    const encoded = /^([\w-]+)'[^']*'(.*)$/.exec(value)
    try {
      return decodeURIComponent(encoded ? encoded[2] : value)
    } catch {
      // a broken encoding: fall back to the plain name
    }
  }
  const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(header)
  if (!plain) return null
  const name = (plain[2] ?? plain[1]).trim()
  return name || null
}

/** Bytes received so far as "3,4 MB" (Albanian decimal comma, one decimal; "0,0 MB" at the start). */
export function formatMegabytes(bytes: number): string {
  // as lib/money.ts writes numbers: "." groups thousands always (sq-AL leaves 4-digit numbers ungrouped)
  const [int, frac] = (bytes / (1024 * 1024)).toFixed(1).split('.')
  return `${int.replace(/\B(?=(\d{3})+(?!\d))/g, '.')},${frac} MB`
}

export interface DownloadResult {
  fileName: string
  bytes: number
}

/**
 * GET `url` with the user's auth header and save the body as a file. `onProgress` gets the bytes received; `signal`
 * cancels (an AbortError is thrown). A problem response (400, 403, 503…) throws ApiError with the server's
 * ProblemDetails, like every other call.
 */
export async function downloadFile(
  url: string,
  fallbackName: string,
  onProgress: (bytes: number) => void,
  signal: AbortSignal,
): Promise<DownloadResult> {
  const response = await fetch(url, { headers: await authHeaders(), signal })
  if (response.status === 401) notifyUnauthorized()
  if (!response.ok) {
    let problem: ProblemDetails | null = null
    try {
      const body: unknown = await response.json()
      if (typeof body === 'object' && body !== null) problem = body as ProblemDetails
    } catch {
      problem = null
    }
    throw new ApiError(response.status, problem)
  }

  const chunks: Uint8Array[] = []
  let bytes = 0
  const reader = response.body?.getReader()
  if (reader) {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      chunks.push(value)
      bytes += value.byteLength
      onProgress(bytes)
    }
  }

  const fileName = fileNameFrom(response.headers.get('Content-Disposition')) ?? fallbackName
  const blob = new Blob(chunks as BlobPart[], { type: response.headers.get('Content-Type') ?? 'application/octet-stream' })
  const href = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = href
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  // give the browser a moment to start the save before the URL goes
  setTimeout(() => URL.revokeObjectURL(href), 10_000)
  return { fileName, bytes }
}
