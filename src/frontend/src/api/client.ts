import createClient from 'openapi-fetch'
import type { components, paths } from './schema'
import { authMode, getDevUser, userManager } from '../app/authConfig'

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

api.use({
  async onRequest({ request }) {
    if (authMode === 'dev') {
      const email = getDevUser()
      if (email) request.headers.set('X-Dev-User', email)
    } else {
      const user = await userManager?.getUser()
      if (user && !user.expired) request.headers.set('Authorization', `Bearer ${user.access_token}`)
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
