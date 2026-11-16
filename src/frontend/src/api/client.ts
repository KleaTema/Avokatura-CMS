import createClient from 'openapi-fetch'
import type { components, paths } from './schema'
import { accessToken, authMode, getDevUser } from '../app/authConfig'
import { runtimeConfig } from '../app/runtimeConfig'

export type ProblemDetails = components['schemas']['ProblemDetails'] & {
  code?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null = null) {
    super(`API error ${status}`)
    this.status = status
    this.problem = problem
  }
}

const asProblem = (error: unknown): ProblemDetails | null =>
  typeof error === 'object' && error !== null ? (error as ProblemDetails) : null

let unauthorizedHandler: (() => void) | null = null
export const setUnauthorizedHandler = (handler: (() => void) | null) => {
  unauthorizedHandler = handler
}

export const api = createClient<paths>({ baseUrl: '' })

/**
 * The login headers of a request. openapi-fetch adds them itself; requests it cannot make — an upload with progress
 * (XMLHttpRequest), a download turned into a blob (task 035) — add them with this. An <iframe> or <a href> could not.
 */
export async function authHeaders(): Promise<Record<string, string>> {
  if (authMode === 'dev') {
    const email = getDevUser()
    return email ? { 'X-Dev-User': email } : {}
  }

  const token = await accessToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

/** For requests made outside openapi-fetch: the same 401 handling (back to the login). */
export const notifyUnauthorized = () => unauthorizedHandler?.()

api.use({
  async onRequest({ request }) {
    for (const [name, value] of Object.entries(await authHeaders())) request.headers.set(name, value)
    // task 052: the API on another base path (runtime /config.json); '' = same origin, the request as it is
    if (runtimeConfig.apiBasePath) {
      const url = new URL(request.url)
      return new Request(`${runtimeConfig.apiBasePath}${url.pathname}${url.search}`, request)
    }
    return request
  },
  onResponse({ response }) {
    if (response.status === 401) unauthorizedHandler?.()
    return response
  },
})

export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data === undefined) throw new ApiError(result.response.status, asProblem(result.error))
  return result.data
}
