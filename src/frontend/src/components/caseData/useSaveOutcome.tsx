import { Alert, Stack } from '@mui/material'
import { useQueryClient, type QueryKey } from '@tanstack/react-query'
import { useState } from 'react'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { caseKeys } from '../../features/cases/api'
import { problemMessage } from '../../lib/problem'
import { ConflictAlert } from '../ui/ConflictAlert'

/** The 409 of a stale rowVersion (CaseDataGate / handlers): someone else saved the case meanwhile. */
export const STALE_ROW_VERSION = 'case.changed'

/**
 * What a failed save shows, the same in every editor and dialog of tasks 023–028:
 * - 409 case.changed → ConflictAlert with "Rifresko" (reloads the case and `alsoReload`); the user's input stays;
 * - any other 409 (already assigned, closed, delegated, …) → the API's message, which says what happened;
 * - 400 with field errors → under the fields (`fields` maps the API's keys to the form's), the rest above them;
 * - anything else (also 400 "Nuk ka ndryshime.") → the API's message above the fields.
 */
export function useSaveOutcome<T extends FieldValues = FieldValues>(
  caseId: string,
  setError?: UseFormSetError<T>,
  fields: Record<string, Path<T>> = {},
  alsoReload: QueryKey[] = [],
) {
  const qc = useQueryClient()
  const [conflict, setConflict] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)

  const reset = () => {
    setConflict(false)
    setFormError(null)
  }

  const fail = (error: unknown) => {
    if (error instanceof ApiError && error.status === 409 && error.problem?.code === STALE_ROW_VERSION) {
      setConflict(true)
      return
    }
    if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
      const unmatched: string[] = []
      for (const [key, messages] of Object.entries(error.problem.errors)) {
        const path = fields[key]
        if (path && setError) setError(path, { type: 'server', message: messages.join(' ') })
        else unmatched.push(...messages)
      }
      if (unmatched.length > 0) setFormError(unmatched.join(' '))
      return
    }
    setFormError(problemMessage(error))
  }

  const refresh = async () => {
    setRefreshing(true)
    try {
      await Promise.all(
        [caseKeys.detail(caseId), ...alsoReload].map((queryKey) => qc.refetchQueries({ queryKey })),
      )
      setConflict(false)
    } finally {
      setRefreshing(false)
    }
  }

  const feedback =
    conflict || formError ? (
      <Stack spacing={1}>
        {conflict && <ConflictAlert onRefresh={() => void refresh()} refreshing={refreshing} />}
        {formError && <Alert severity="error">{formError}</Alert>}
      </Stack>
    ) : null

  return { fail, reset, feedback, refreshing }
}
