import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, authHeaders, unwrap, type ProblemDetails } from '../../api/client'
import type { components } from '../../api/schema'

/** Task 053l: the import kinds of the shared pipeline (Administrimi). */
export type ImportKind = 'organika' | 'users'

export type ImportView = components['schemas']['ImportView']
export type ImportRowView = components['schemas']['ImportRowView']
export type ImportSummary = components['schemas']['ImportSummary']
export type ImportDefinitionView = components['schemas']['ImportDefinitionView']

export const importBase = (kind: ImportKind) => `/api/identity/admin/imports/${kind}`

const keys = {
  definition: (kind: ImportKind) => ['imports', kind, 'definition'] as const,
  list: (kind: ImportKind) => ['imports', kind, 'list'] as const,
}

/** The columns, instructions and limits — the same definition the server parses with and writes the template from. */
export const useImportDefinition = (kind: ImportKind) =>
  useQuery({
    queryKey: keys.definition(kind),
    staleTime: Infinity,
    queryFn: () =>
      (kind === 'organika'
        ? api.GET('/api/identity/admin/imports/organika/definition')
        : api.GET('/api/identity/admin/imports/users/definition')
      ).then(unwrap),
  })

export const useImports = (kind: ImportKind) =>
  useQuery({
    queryKey: keys.list(kind),
    queryFn: () =>
      (kind === 'organika' ? api.GET('/api/identity/admin/imports/organika') : api.GET('/api/identity/admin/imports/users')).then(unwrap),
  })

export async function getImport(kind: ImportKind, publicId: string): Promise<ImportView> {
  const params = { params: { path: { publicId } } }
  return (
    kind === 'organika'
      ? api.GET('/api/identity/admin/imports/organika/{publicId}', params)
      : api.GET('/api/identity/admin/imports/users/{publicId}', params)
  ).then(unwrap)
}

/** POST the sheet as multipart (the generated client does not send files); a 400 comes back as ApiError. */
async function upload(kind: ImportKind, file: File): Promise<ImportView> {
  const form = new FormData()
  form.append('file', file, file.name)
  const response = await fetch(`${importBase(kind)}/`, { method: 'POST', body: form, headers: await authHeaders() })
  const body: unknown = await response.json().catch(() => null)
  if (!response.ok) throw new ApiError(response.status, body as ProblemDetails | undefined)
  return body as ImportView
}

export const useUploadImport = (kind: ImportKind) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => upload(kind, file),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.list(kind) }),
  })
}

export const useApplyImport = (kind: ImportKind) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => {
      const params = { params: { path: { publicId } } }
      return (
        kind === 'organika'
          ? api.POST('/api/identity/admin/imports/organika/{publicId}/apply', params)
          : api.POST('/api/identity/admin/imports/users/{publicId}/apply', params)
      ).then(unwrap)
    },
    // the users list too: an import creates and updates users
    onSuccess: () => Promise.all([qc.invalidateQueries({ queryKey: keys.list(kind) }), qc.invalidateQueries({ queryKey: ['admin'] })]),
  })
}
