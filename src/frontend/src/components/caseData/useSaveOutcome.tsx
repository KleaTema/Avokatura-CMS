import { Alert, Button, Stack } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { caseKeys } from '../../features/cases/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'

/**
 * What a failed save of a data editor shows:
 * - 400 with field errors → under the fields (`fields` maps the API's keys to the form's);
 * - 409 (another user changed the case, or it was closed) → the message with "Rifresko", which reloads the case;
 *   the editor stays open with the user's values, to copy or to save again on top of the fresh case;
 * - anything else (also 400 "Nuk ka ndryshime.") → one message above the fields.
 */
export function useSaveOutcome<T extends FieldValues = FieldValues>(
  caseId: string,
  setError?: UseFormSetError<T>,
  fields: Record<string, Path<T>> = {},
) {
  const qc = useQueryClient()
  const [conflict, setConflict] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)

  const reset = () => {
    setConflict(null)
    setFormError(null)
  }

  const fail = (error: unknown) => {
    if (error instanceof ApiError && error.status === 409) {
      setConflict(problemMessage(error))
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
      await qc.refetchQueries({ queryKey: caseKeys.detail(caseId) })
      setConflict(null)
    } finally {
      setRefreshing(false)
    }
  }

  const feedback =
    conflict || formError ? (
      <Stack spacing={1}>
        {conflict && (
          <Alert
            severity="warning"
            action={
              <Button color="inherit" size="small" onClick={() => void refresh()} disabled={refreshing}>
                {sq.cases.edit.refresh}
              </Button>
            }
          >
            {conflict} {sq.cases.edit.conflictKeep}
          </Alert>
        )}
        {formError && <Alert severity="error">{formError}</Alert>}
      </Stack>
    ) : null

  return { fail, reset, feedback, refreshing }
}
