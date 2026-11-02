import { Box, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { useChangeJudge, useChangeObject, type CaseDetail } from '../../features/cases/api'
import { useCourts } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { JudgePicker, type PickedJudge } from '../JudgePicker'
import { EditorActions } from './EditorActions'
import { useSaveOutcome } from './useSaveOutcome'

const e = sq.cases.edit
const MAX_OBJECT = 4000

// ---------------------------------------------------------------- Gjyqtari

interface JudgeValues {
  judge: PickedJudge | null
  showAllJudges: boolean
}

/**
 * The judge, picked like at registration (JudgePicker: the case court's active judges, or all of them). A judge of
 * another court is saved with a warning — passed to `onSaved` so the card can show it.
 */
export function JudgeEditor({
  detail,
  onSaved,
  onCancel,
}: {
  detail: CaseDetail
  onSaved: (warnings: string[]) => void
  onCancel: () => void
}) {
  const caseId = detail.header.publicId
  const save = useChangeJudge(caseId)
  const courts = useCourts().data ?? []
  // court names are unique (uq_courts_name); the case page carries no numeric ids
  const court = courts.find((c) => c.name === detail.data.courtName)
  const courtName = (id: string | null) => courts.find((c) => String(c.id) === id)?.name
  const {
    control,
    handleSubmit,
    setError,
    setValue,
    getValues,
    formState: { errors },
  } = useForm<JudgeValues>({ defaultValues: { judge: null, showAllJudges: false } })
  const showAll = useWatch({ control, name: 'showAllJudges' })
  const outcome = useSaveOutcome(caseId, setError, { judgeId: 'judge' })

  const toggleShowAll = (all: boolean) => {
    setValue('showAllJudges', all)
    const judge = getValues('judge')
    if (!all && judge && judge.courtId !== (court ? String(court.id) : null)) setValue('judge', null)
  }

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    if (!x.judge) {
      setError('judge', { type: 'required', message: e.judge.required })
      return
    }
    try {
      const result = await save.mutateAsync({ judgeId: Number(x.judge.id), rowVersion: Number(detail.header.rowVersion) })
      onSaved(result.warnings.map((w) => w.message))
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Typography variant="body2" color="text.secondary">
        {e.judge.court(detail.data.courtName)}
      </Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: { md: 'flex-start' } }}>
        <Box sx={{ flex: 2 }}>
          <Controller
            name="judge"
            control={control}
            render={({ field }) => (
              <JudgePicker
                courtId={court ? Number(court.id) : null}
                showAll={showAll}
                courtName={courtName}
                value={field.value}
                onChange={field.onChange}
                inputRef={field.ref}
                label={e.judge.label}
                required
                error={!!errors.judge}
                helperText={errors.judge?.message}
              />
            )}
          />
        </Box>
        <Controller
          name="showAllJudges"
          control={control}
          render={({ field }) => (
            <FormControlLabel
              sx={{ flex: 1, mt: { md: 1 } }}
              control={<Checkbox checked={field.value} onChange={(ev) => toggleShowAll(ev.target.checked)} />}
              label={sq.cases.register.fields.showAllJudges}
            />
          )}
        />
      </Stack>
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing} />
    </Stack>
  )
}

// ---------------------------------------------------------------- Objekti

interface ObjectValues {
  objectText: string
}

/** The object as one multiline text with a counter; the whole old text goes to the history. */
export function ObjectEditor({ detail, onSaved, onCancel }: { detail: CaseDetail; onSaved: () => void; onCancel: () => void }) {
  const caseId = detail.header.publicId
  const save = useChangeObject(caseId)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ObjectValues>({ defaultValues: { objectText: detail.data.objectText } })
  const text = useWatch({ control, name: 'objectText' })
  const outcome = useSaveOutcome(caseId, setError, { objectText: 'objectText' })
  const length = text.trim().length

  const onSubmit = handleSubmit(async (x) => {
    if (save.isPending) return
    outcome.reset()
    const trimmed = x.objectText.trim()
    if (trimmed.length === 0) {
      setError('objectText', { type: 'required', message: e.object.required })
      return
    }
    if (trimmed.length > MAX_OBJECT) {
      setError('objectText', { type: 'maxLength', message: e.object.tooLong })
      return
    }
    try {
      await save.mutateAsync({ objectText: trimmed, rowVersion: Number(detail.header.rowVersion) })
      onSaved()
    } catch (error) {
      outcome.fail(error)
    }
  })

  return (
    <Stack component="form" spacing={2} onSubmit={onSubmit} noValidate>
      {outcome.feedback}
      <Controller
        name="objectText"
        control={control}
        render={({ field }) => (
          <TextField
            {...field}
            inputRef={field.ref}
            label={e.object.label}
            required
            multiline
            minRows={4}
            error={!!errors.objectText || length > MAX_OBJECT}
            helperText={errors.objectText?.message ?? e.object.counter(length)}
            slotProps={{ htmlInput: { maxLength: MAX_OBJECT + 200 }, formHelperText: { sx: { textAlign: 'right' } } }}
          />
        )}
      />
      {/* nothing to save while the text is what is stored */}
      <EditorActions onCancel={onCancel} busy={save.isPending || outcome.refreshing || text.trim() === detail.data.objectText} />
    </Stack>
  )
}
