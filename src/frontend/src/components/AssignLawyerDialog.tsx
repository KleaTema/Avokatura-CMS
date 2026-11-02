import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Controller, useForm, useWatch, type Path, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../api/client'
import { useAssignableLawyers, useAssignLawyers, useCase, type AssignableLawyer } from '../features/cases/api'
import { sq } from '../i18n/sq'
import { matches } from '../lib/search'
import { problemMessage } from '../lib/problem'
import { CaseCode } from './ui/CaseCode'

const t = sq.cases.assign
const d = sq.cases.page.data

const SET_PRIORITY = 'case.set_priority_any'

interface AssignValues {
  primary: AssignableLawyer | null
  secondary: AssignableLawyer | null
  priority: string
}

const schema = z.object({
  primary: z.custom<AssignableLawyer | null>().refine((x) => x !== null, t.primaryRequired),
  secondary: z.custom<AssignableLawyer | null>(),
  priority: z.string(),
})

const SERVER_FIELDS: Record<string, Path<AssignValues>> = {
  primaryUserPublicId: 'primary',
  secondaryUserPublicId: 'secondary',
  priority: 'priority',
}

const lawyerLabel = (x: AssignableLawyer) => (x.isSelf ? `${x.fullName} ${t.self}` : x.fullName)

/**
 * The Kryetar assigns the primary (and optionally the secondary) lawyer of a REGISTERED case (task 023): case code,
 * court and object for orientation, the office's lawyers with their workload, priority only with case.set_priority_any.
 * A 409 (someone else assigned or changed the case) keeps the chosen lawyers; "Rifresko" reloads the case.
 */
export function AssignLawyerDialog({
  casePublicId,
  onClose,
  onAssigned,
}: {
  casePublicId: string
  onClose: () => void
  /** after a successful assignment, with the primary lawyer's name */
  onAssigned: (primaryName: string) => void
}) {
  const detail = useCase(casePublicId)
  const lawyers = useAssignableLawyers(casePublicId)
  const assign = useAssignLawyers(casePublicId)
  const [formError, setFormError] = useState<string | null>(null)
  const [conflict, setConflict] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const {
    control,
    handleSubmit,
    setError,
    setValue,
    getValues,
    formState: { errors },
  } = useForm<AssignValues>({
    resolver: zodResolver(schema) as Resolver<AssignValues>,
    defaultValues: { primary: null, secondary: null, priority: '' },
  })
  const primary = useWatch({ control, name: 'primary' })

  const c = detail.data
  const canSetPriority = c?.permissions.includes(SET_PRIORITY) ?? false
  const loadError = detail.error ?? lawyers.error
  const options = lawyers.data ?? []

  const onSubmit = handleSubmit(async (x) => {
    if (assign.isPending || !c || !x.primary) return
    setFormError(null)
    setConflict(null)
    // the priority is sent only when it was changed (and may be changed)
    const priority = canSetPriority && x.priority !== '' && x.priority !== c.header.priority ? x.priority : null
    try {
      await assign.mutateAsync({
        primaryUserPublicId: x.primary.publicId,
        secondaryUserPublicId: x.secondary?.publicId ?? null,
        priority,
        rowVersion: Number(c.header.rowVersion),
      })
      onAssigned(x.primary.fullName)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setConflict(problemMessage(error))
      } else if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
        const unmatched: string[] = []
        for (const [key, messages] of Object.entries(error.problem.errors)) {
          const path = SERVER_FIELDS[key]
          if (path) setError(path, { type: 'server', message: messages.join(' ') })
          else unmatched.push(...messages)
        }
        if (unmatched.length > 0) setFormError(unmatched.join(' '))
      } else {
        setFormError(problemMessage(error))
      }
    }
  })

  // reloads the case (new rowVersion, status) and the workload; the chosen lawyers stay in the form
  const refresh = async () => {
    setRefreshing(true)
    try {
      await Promise.all([detail.refetch(), lawyers.refetch()])
      setConflict(null)
    } finally {
      setRefreshing(false)
    }
  }

  const picker = (name: 'primary' | 'secondary', label: string, required: boolean) => (
    <Controller
      name={name}
      control={control}
      render={({ field }) => (
        <Autocomplete<AssignableLawyer>
          options={options}
          value={field.value}
          onChange={(_, picked) => {
            field.onChange(picked)
            // the secondary cannot be the new primary
            if (name === 'primary' && picked && getValues('secondary')?.publicId === picked.publicId) {
              setValue('secondary', null)
            }
          }}
          getOptionLabel={lawyerLabel}
          isOptionEqualToValue={(a, b) => a.publicId === b.publicId}
          getOptionDisabled={name === 'secondary' ? (o) => o.publicId === primary?.publicId : undefined}
          filterOptions={(all, state) => all.filter((o) => matches(state.inputValue, o.fullName))}
          noOptionsText={sq.search.noOptions}
          renderOption={({ key, ...props }, o) => (
            <Box component="li" key={key} {...props}>
              <Box>
                <Typography variant="body1">{lawyerLabel(o)}</Typography>
                <Typography variant="caption" component="p" color="text.secondary">
                  {name === 'secondary' && o.publicId === primary?.publicId
                    ? t.sameAsPrimary
                    : t.workload(Number(o.workload.activePrimary), Number(o.workload.activeSecondary))}
                </Typography>
              </Box>
            </Box>
          )}
          renderInput={(params) => (
            <TextField
              {...params}
              inputRef={field.ref}
              label={label}
              required={required}
              autoFocus={name === 'primary'}
              error={!!errors[name]}
              helperText={
                errors[name]?.message ??
                (field.value
                  ? t.workload(Number(field.value.workload.activePrimary), Number(field.value.workload.activeSecondary))
                  : undefined)
              }
            />
          )}
        />
      )}
    />
  )

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="assign-title">
      <Box component="form" onSubmit={onSubmit} noValidate>
        <DialogTitle id="assign-title">{t.title}</DialogTitle>
        <DialogContent>
          {loadError ? (
            <Alert severity="error">{problemMessage(loadError)}</Alert>
          ) : !c || lawyers.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : (
            <Stack spacing={2}>
              <Box>
                <CaseCode code={c.header.caseCode} />
                <Typography variant="body2" color="text.secondary">
                  <strong>{d.court}:</strong> {c.data.courtName}
                </Typography>
                <Typography
                  variant="body2"
                  color="text.secondary"
                  title={c.data.objectText}
                  sx={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}
                >
                  <strong>{d.object}:</strong> {c.data.objectText}
                </Typography>
              </Box>

              {conflict && (
                <Alert
                  severity="warning"
                  action={
                    <Button color="inherit" size="small" onClick={() => void refresh()} disabled={refreshing}>
                      {t.refresh}
                    </Button>
                  }
                >
                  {conflict}
                </Alert>
              )}
              {formError && <Alert severity="error">{formError}</Alert>}
              {options.length === 0 && <Alert severity="info">{t.noLawyers}</Alert>}

              {picker('primary', t.primary, true)}
              {picker('secondary', t.secondary, false)}

              {canSetPriority && (
                <Controller
                  name="priority"
                  control={control}
                  render={({ field }) => (
                    <TextField
                      select
                      label={t.priority}
                      value={field.value || c.header.priority}
                      onChange={field.onChange}
                      inputRef={field.ref}
                      error={!!errors.priority}
                      helperText={errors.priority?.message}
                    >
                      {Object.entries(sq.cases.priority).map(([code, name]) => (
                        <MenuItem key={code} value={code}>
                          {name}
                        </MenuItem>
                      ))}
                    </TextField>
                  )}
                />
              )}
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button
            type="submit"
            variant="contained"
            color="secondary"
            disabled={!c || !!loadError || assign.isPending || refreshing}
          >
            {t.confirm}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}
