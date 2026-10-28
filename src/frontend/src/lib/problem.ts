import { ApiError } from '../api/client'
import { sq } from '../i18n/sq'

/**
 * Albanian message for a failed request. Our own errors carry a `code` extension and an Albanian title;
 * framework-generated problems (English titles like "Bad Request") fall back to a status message.
 */
export function problemMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return sq.errors.network
  if (error.problem?.code && error.problem.title && error.status < 500) return error.problem.title
  switch (error.status) {
    case 400:
      return sq.errors.badRequest
    case 403:
      return sq.errors.forbidden
    case 404:
      return sq.errors.notFound
    case 409:
      return sq.errors.conflict
    default:
      return sq.errors.generic
  }
}

/** The machine-readable `code` extension of a ProblemDetails, if any. */
export const problemCode = (error: unknown): string | undefined =>
  error instanceof ApiError ? error.problem?.code : undefined
