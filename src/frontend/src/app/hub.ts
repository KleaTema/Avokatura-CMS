import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { accessToken, authMode, getDevUser } from './authConfig'
import { runtimeConfig } from './runtimeConfig'

/**
 * The SignalR connections of the app (task 040 notifications, task 050 chat) are built the same way. In Development the
 * dev user travels in the query string (a browser WebSocket cannot send the X-Dev-User header; the API accepts
 * `dev_user` on /hubs/* only and never logs it). With a provider (task 051) the token comes from accessTokenFactory
 * (the API accepts ?access_token= on /hubs/* only).
 */
export function hubUrl(hubPath: string): string {
  // task 052: the API's base path from /config.json ('' = same origin)
  const path = `${runtimeConfig.apiBasePath}${hubPath}`
  if (authMode !== 'dev') return path
  const email = getDevUser()
  return email ? `${path}?dev_user=${encodeURIComponent(email)}` : path
}

export async function hubAccessToken(): Promise<string> {
  // task 051: the remembered provider's token, renewed silently (MSAL)
  return authMode === 'dev' ? '' : accessToken()
}

export const buildHub = (path: string): HubConnection =>
  new HubConnectionBuilder()
    .withUrl(hubUrl(path), { accessTokenFactory: hubAccessToken })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build()

/** First connection attempts when the server is not reachable yet (later drops: withAutomaticReconnect). */
export const START_RETRY_MS = [2_000, 5_000, 10_000, 30_000]

/**
 * Starts the connection, retrying with back-off until it is up or `isDisposed()`. `onStarted(attempt)` runs once it is
 * up (attempt 0 = at the first try): pushes sent before it was up are lost, so the caller refetches what it needs.
 * Returns the cancel of the pending retry.
 */
export function startWithRetry(
  connection: HubConnection,
  isDisposed: () => boolean,
  onStarted: (attempt: number) => void,
): () => void {
  let retry: ReturnType<typeof setTimeout> | undefined
  const start = async (attempt: number) => {
    try {
      await connection.start()
      onStarted(attempt)
    } catch {
      if (isDisposed()) return
      retry = setTimeout(() => void start(attempt + 1), START_RETRY_MS[Math.min(attempt, START_RETRY_MS.length - 1)])
    }
  }
  void start(0)
  return () => clearTimeout(retry)
}
