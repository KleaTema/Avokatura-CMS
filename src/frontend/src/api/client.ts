import createClient from 'openapi-fetch'
import type { paths } from './schema'
import { authMode, getDevUser, userManager } from '../app/authConfig'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number) {
    super(`API error ${status}`)
    this.status = status
  }
}

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

export function unwrap<T>(result: { data?: T; response: Response }): T {
  if (result.data === undefined) throw new ApiError(result.response.status)
  return result.data
}
