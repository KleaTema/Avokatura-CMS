import { sq } from '../i18n/sq'

const t = sq.cases.documents

/** An allowed file type as the API lists it (extension without the dot, size limit in bytes). */
export interface AllowedType {
  extension: string
  maxSizeBytes: number | string
  appliesTo: string
}

/** The last extension, lower case, without the dot ("Vendim.Final.PDF" → "pdf"); "" without one. */
export function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return dot > 0 && dot < fileName.length - 1 ? fileName.slice(dot + 1).toLowerCase() : ''
}

/** The default title: the file name without its last extension. */
export function titleOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.')
  return (dot > 0 ? fileName.slice(0, dot) : fileName).trim()
}

const documentTypes = (types: AllowedType[]) => types.filter((x) => x.appliesTo === 'DOCUMENT' || x.appliesTo === 'BOTH')

/** ".pdf,.docx,…" for the file picker's accept attribute. */
export const acceptOf = (types: AllowedType[]) =>
  [...new Set(documentTypes(types).map((x) => `.${x.extension}`))].join(',')

const MB = 1024 * 1024

/**
 * The quick check before sending (fail fast: no 50 MB upload only to hear "not allowed"): the extension must be an
 * allowed DOCUMENT type and the size within its limit. The server checks again — including the file's first bytes.
 */
export function preCheck(file: { name: string; size: number }, types: AllowedType[]): string | null {
  const type = documentTypes(types).find((x) => x.extension === extensionOf(file.name))
  if (!type) return t.typeNotAllowed
  if (file.size === 0) return t.emptyFile
  const max = Number(type.maxSizeBytes)
  if (file.size > max) return t.tooLarge(Math.floor(max / MB))
  return null
}

/** "850 KB", "2,4 MB" (Albanian decimal comma). */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < MB) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / MB).toFixed(1).replace('.', ',')} MB`
}

export interface DocumentFields {
  documentTypeCode: string
  otherTypeText?: string
  title?: string
}

/** The upload form: the fields FIRST, then the file — the API reads the fields before it reads the file. */
export function documentForm(fields: DocumentFields, file: Blob, fileName: string): FormData {
  const form = new FormData()
  form.append('documentTypeCode', fields.documentTypeCode)
  if (fields.otherTypeText) form.append('otherTypeText', fields.otherTypeText)
  if (fields.title) form.append('title', fields.title)
  form.append('file', file, fileName)
  return form
}

/** The file name of a download: filename* (RFC 5987, UTF-8) first, then filename, else the fallback. */
export function fileNameFromDisposition(header: string | null, fallback: string): string {
  if (!header) return fallback
  const star = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header)
  if (star) {
    try {
      return decodeURIComponent(star[1].trim())
    } catch {
      // a broken encoding: try the plain name
    }
  }

  const plain = /filename\s*=\s*"([^"]*)"/i.exec(header) ?? /filename\s*=\s*([^;]+)/i.exec(header)
  return plain ? plain[1].trim() : fallback
}
