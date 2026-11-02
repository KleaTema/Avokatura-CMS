import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormLabel,
  Paper,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { Controller, useForm, useWatch, type Path, type Resolver } from 'react-hook-form'
import { z } from 'zod'
import {
  caseKeys,
  useAssignableLawyers,
  useCase,
  useTransferLawyer,
  type AssignableLawyer,
  type TransferLawyerRequest,
} from '../features/cases/api'
import { sq } from '../i18n/sq'
import { problemMessage } from '../lib/problem'
import { matches } from '../lib/search'
import { useSaveOutcome } from './caseData/useSaveOutcome'
import { CaseCode } from './ui/CaseCode'
import { preventEnterSubmit, useFullScreenDialog } from './ui/dialog'

const t = sq.cases.transfer
const MAX_REASON = 500

type SecondaryAction = 'keep' | 'change' | 'remove'

/** An option of "Avokati i ri primar": a lawyer, or the quick "the secondary moves up" entry. */
interface PrimaryOption {
  lawyer: AssignableLawyer
  promote: boolean
}

interface TransferValues {
  newPrimary: PrimaryOption | null
  secondaryAction: SecondaryAction
  newSecondary: AssignableLawyer | null
  oldPrimaryToSecondary: boolean
  reason: string
}

const schema = z
  .object({
    newPrimary: z.custom<PrimaryOption | null>(),
    secondaryAction: z.enum(['keep', 'change', 'remove']),
    newSecondary: z.custom<AssignableLawyer | null>(),
    oldPrimaryToSecondary: z.boolean(),
    reason: z.string().refine((s) => s.trim().length <= MAX_REASON, t.reasonTooLong),
  })
  .superRefine((x, ctx) => {
    if (!x.oldPrimaryToSecondary && x.secondaryAction === 'change' && x.newSecondary === null) {
      ctx.addIssue({ code: 'custom', path: ['newSecondary'], message: t.secondaryRequired })
    }
  })

const SERVER_FIELDS: Record<string, Path<TransferValues>> = {
  newPrimaryUserPublicId: 'newPrimary',
  'secondary.userPublicId': 'newSecondary',
  reason: 'reason',
}

const lawyerLabel = (x: AssignableLawyer) => (x.isSelf ? `${x.fullName} ${sq.cases.assign.self}` : x.fullName)
const workload = (x: AssignableLawyer) =>
  sq.cases.assign.workload(Number(x.workload.activePrimary), Number(x.workload.activeSecondary))

/**
 * The Kryetar changes the lawyers of an ACTIVE case (task 024): a new primary (or the secondary moves up), and/or the
 * secondary kept, changed or removed — or the current primary becomes the secondary. A summary shows the result before
 * confirming. A 409 keeps the choices; "Rifresko" reloads the case and the workload.
 */
export function TransferDialog({
  casePublicId,
  onClose,
  onTransferred,
}: {
  casePublicId: string
  onClose: () => void
  onTransferred: () => void
}) {
  const detail = useCase(casePublicId)
  const lawyers = useAssignableLawyers(casePublicId)
  const transfer = useTransferLawyer(casePublicId)
  const fullScreen = useFullScreenDialog()
  const {
    control,
    handleSubmit,
    setError,
    setValue,
    formState: { errors },
  } = useForm<TransferValues>({
    resolver: zodResolver(schema) as Resolver<TransferValues>,
    defaultValues: { newPrimary: null, secondaryAction: 'keep', newSecondary: null, oldPrimaryToSecondary: false, reason: '' },
  })
  const [newPrimary, secondaryAction, newSecondary, oldPrimaryToSecondary] = useWatch({
    control,
    name: ['newPrimary', 'secondaryAction', 'newSecondary', 'oldPrimaryToSecondary'],
  })
  // with "the current primary becomes secondary" there is no secondary picker to put its error under
  const serverFields: Record<string, Path<TransferValues>> = oldPrimaryToSecondary
    ? { newPrimaryUserPublicId: 'newPrimary', reason: 'reason' }
    : SERVER_FIELDS
  // "Rifresko" also reloads the workload; the choices stay in the form
  const outcome = useSaveOutcome(casePublicId, setError, serverFields, [caseKeys.picker(casePublicId, 'lawyers')])

  const c = detail.data
  const all = lawyers.data ?? []
  const currentPrimary = c?.team.find((m) => m.role === 'PRIMARY_LAWYER') ?? null
  const currentSecondary = c?.team.find((m) => m.role === 'SECONDARY_LAWYER') ?? null
  const secondaryInList = currentSecondary ? all.find((x) => x.publicId === currentSecondary.userPublicId) : undefined
  const primaryInList = currentPrimary ? all.some((x) => x.publicId === currentPrimary.userPublicId) : false

  // the current primary is not offered; the quick entry first when the secondary can move up
  const primaryOptions: PrimaryOption[] = [
    ...(secondaryInList ? [{ lawyer: secondaryInList, promote: true }] : []),
    ...all.filter((x) => x.publicId !== currentPrimary?.userPublicId).map((lawyer) => ({ lawyer, promote: false })),
  ]

  // ---------------------------------------------------------------- the result (summary and request)
  const finalPrimary = newPrimary
    ? { id: newPrimary.lawyer.publicId, name: newPrimary.lawyer.fullName }
    : currentPrimary && { id: currentPrimary.userPublicId, name: currentPrimary.name }
  const keptSecondary =
    currentSecondary && currentSecondary.userPublicId !== finalPrimary?.id
      ? { id: currentSecondary.userPublicId, name: currentSecondary.name }
      : null
  const finalSecondary = oldPrimaryToSecondary && newPrimary && currentPrimary
    ? { id: currentPrimary.userPublicId, name: currentPrimary.name }
    : secondaryAction === 'change'
      ? newSecondary
        ? { id: newSecondary.publicId, name: newSecondary.fullName }
        : keptSecondary // nothing picked yet
      : secondaryAction === 'remove'
        ? null
        : keptSecondary
  const primaryLine = currentPrimary
    ? newPrimary
      ? t.changed(currentPrimary.name, newPrimary.lawyer.fullName)
      : t.unchanged(currentPrimary.name)
    : t.none
  const secondaryLine =
    (currentSecondary?.userPublicId ?? null) === (finalSecondary?.id ?? null)
      ? currentSecondary
        ? t.unchanged(currentSecondary.name)
        : t.none
      : finalSecondary
        ? t.changed(currentSecondary?.name ?? t.none, finalSecondary.name)
        : t.removed(currentSecondary?.name ?? t.none)
  const changes = newPrimary !== null || (currentSecondary?.userPublicId ?? null) !== (finalSecondary?.id ?? null)

  const onSubmit = handleSubmit(async (x) => {
    // nothing to change: the confirm button is disabled then
    if (transfer.isPending || !c || !currentPrimary || !changes) return
    outcome.reset()

    const secondary: TransferLawyerRequest['secondary'] =
      x.oldPrimaryToSecondary && x.newPrimary
        ? { action: 'set', userPublicId: currentPrimary.userPublicId }
        : x.secondaryAction === 'change'
          ? { action: 'set', userPublicId: x.newSecondary?.publicId ?? null }
          : { action: x.secondaryAction, userPublicId: null }
    try {
      await transfer.mutateAsync({
        newPrimaryUserPublicId: x.newPrimary?.lawyer.publicId ?? null,
        secondary,
        reason: x.reason.trim() || null,
        rowVersion: Number(c.header.rowVersion),
      })
      onTransferred()
    } catch (error) {
      outcome.fail(error)
    }
  })

  const loadError = detail.error ?? lawyers.error

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="transfer-title">
      <Box component="form" onSubmit={onSubmit} onKeyDown={preventEnterSubmit} noValidate>
        <DialogTitle id="transfer-title">{t.title}</DialogTitle>
        <DialogContent>
          {loadError ? (
            <Alert severity="error">{problemMessage(loadError)}</Alert>
          ) : !c || lawyers.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : (
            <Stack spacing={2.5}>
              <Box>
                <CaseCode code={c.header.caseCode} />
              </Box>

              {outcome.feedback}

              {/* the new primary */}
              <Controller
                name="newPrimary"
                control={control}
                render={({ field }) => (
                  <Autocomplete<PrimaryOption>
                    options={primaryOptions}
                    value={field.value}
                    onChange={(_, picked) => {
                      field.onChange(picked)
                      if (!picked) setValue('oldPrimaryToSecondary', false)
                    }}
                    getOptionLabel={(o) => (o.promote ? t.promoteSecondary(o.lawyer.fullName) : lawyerLabel(o.lawyer))}
                    getOptionKey={(o) => `${o.promote ? 'promote' : 'lawyer'}-${o.lawyer.publicId}`}
                    isOptionEqualToValue={(a, b) => a.lawyer.publicId === b.lawyer.publicId && a.promote === b.promote}
                    filterOptions={(options, state) => options.filter((o) => matches(state.inputValue, o.lawyer.fullName))}
                    noOptionsText={sq.search.noOptions}
                    renderOption={({ key, ...props }, o) => (
                      <Box component="li" key={key} {...props}>
                        <Box>
                          <Typography variant="body1" sx={o.promote ? { fontWeight: 600 } : undefined}>
                            {o.promote ? t.promoteSecondary(o.lawyer.fullName) : lawyerLabel(o.lawyer)}
                          </Typography>
                          <Typography variant="caption" component="p" color="text.secondary">
                            {workload(o.lawyer)}
                          </Typography>
                        </Box>
                      </Box>
                    )}
                    renderInput={(params) => (
                      <TextField
                        {...params}
                        inputRef={field.ref}
                        label={t.newPrimary}
                        autoFocus
                        error={!!errors.newPrimary}
                        helperText={errors.newPrimary?.message ?? (field.value ? workload(field.value.lawyer) : t.newPrimaryHint)}
                      />
                    )}
                  />
                )}
              />

              {/* the secondary */}
              <FormControl component="fieldset" disabled={oldPrimaryToSecondary}>
                <FormLabel component="legend">
                  {t.secondary}
                  {currentSecondary ? `: ${currentSecondary.name}` : `: ${t.none}`}
                </FormLabel>
                <Controller
                  name="secondaryAction"
                  control={control}
                  render={({ field }) => (
                    <RadioGroup row value={field.value} onChange={(e) => field.onChange(e.target.value)}>
                      <FormControlLabel value="keep" control={<Radio />} label={t.keep} />
                      <FormControlLabel value="change" control={<Radio />} label={t.change} />
                      {/* its own `disabled` overrides the fieldset's, so both conditions are here */}
                      <FormControlLabel
                        value="remove"
                        control={<Radio />}
                        label={t.remove}
                        disabled={!currentSecondary || oldPrimaryToSecondary}
                      />
                    </RadioGroup>
                  )}
                />
              </FormControl>

              {secondaryAction === 'change' && !oldPrimaryToSecondary && (
                <Controller
                  name="newSecondary"
                  control={control}
                  render={({ field }) => (
                    <Autocomplete<AssignableLawyer>
                      options={all}
                      value={field.value}
                      onChange={(_, picked) => field.onChange(picked)}
                      getOptionLabel={lawyerLabel}
                      isOptionEqualToValue={(a, b) => a.publicId === b.publicId}
                      getOptionDisabled={(o) =>
                        o.publicId === finalPrimary?.id || o.publicId === currentSecondary?.userPublicId
                      }
                      filterOptions={(options, state) => options.filter((o) => matches(state.inputValue, o.fullName))}
                      noOptionsText={sq.search.noOptions}
                      renderOption={({ key, ...props }, o) => (
                        <Box component="li" key={key} {...props}>
                          <Box>
                            <Typography variant="body1">{lawyerLabel(o)}</Typography>
                            <Typography variant="caption" component="p" color="text.secondary">
                              {o.publicId === finalPrimary?.id
                                ? t.isFinalPrimary
                                : o.publicId === currentSecondary?.userPublicId
                                  ? t.isCurrentSecondary
                                  : workload(o)}
                            </Typography>
                          </Box>
                        </Box>
                      )}
                      renderInput={(params) => (
                        <TextField
                          {...params}
                          inputRef={field.ref}
                          label={t.newSecondary}
                          required
                          error={!!errors.newSecondary}
                          helperText={errors.newSecondary?.message ?? (field.value ? workload(field.value) : undefined)}
                        />
                      )}
                    />
                  )}
                />
              )}

              {/* the old primary as the secondary: only with a new primary, and only if he can be assigned */}
              {primaryInList && (
                <Controller
                  name="oldPrimaryToSecondary"
                  control={control}
                  render={({ field }) => (
                    <FormControlLabel
                      control={
                        <Checkbox
                          checked={field.value}
                          onChange={(e) => field.onChange(e.target.checked)}
                          disabled={!newPrimary}
                        />
                      }
                      label={currentPrimary ? `${t.oldPrimaryBecomesSecondary} (${currentPrimary.name})` : t.oldPrimaryBecomesSecondary}
                    />
                  )}
                />
              )}

              <Controller
                name="reason"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    inputRef={field.ref}
                    label={t.reason}
                    multiline
                    minRows={2}
                    error={!!errors.reason}
                    helperText={errors.reason?.message}
                    slotProps={{ htmlInput: { maxLength: MAX_REASON } }}
                  />
                )}
              />

              {/* what the case will look like */}
              <Paper variant="outlined" sx={{ p: 2, bgcolor: 'background.default' }} role="status">
                <Typography variant="subtitle2" component="p" sx={{ mb: 0.5 }}>
                  {t.summaryTitle}
                </Typography>
                <Typography variant="body2">
                  <strong>{t.primaryLine}:</strong> {primaryLine}
                </Typography>
                <Typography variant="body2">
                  <strong>{t.secondaryLine}:</strong> {secondaryLine}
                </Typography>
              </Paper>
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button
            type="submit"
            variant="contained"
            color="secondary"
            disabled={!c || !!loadError || transfer.isPending || outcome.refreshing || !changes}
          >
            {t.confirm}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}
