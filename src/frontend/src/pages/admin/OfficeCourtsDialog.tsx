import {
  Alert,
  Autocomplete,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useOfficeCourts, useSetOfficeCourts, type AdminOffice } from '../../features/admin/api'
import { useCourtLevels, useCourts, type Court } from '../../features/reference/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { matches } from '../../lib/search'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminOffices

/**
 * "Gjykatat që mbulon" (task 053c; task 053j: a summons of one of them pre-fills this office at registration): the courts this office handles, so a case of one of them reaches this office's
 * Protokoll. A court of another office is shown but cannot be picked ("Mbulohet nga …"); the server refuses it too.
 */
export function OfficeCourtsDialog({ office, onClose }: { office: AdminOffice; onClose: () => void }) {
  const courts = useCourts()
  const levels = useCourtLevels()
  const mappings = useOfficeCourts()
  const save = useSetOfficeCourts()
  const redirectIfForbidden = useForbiddenRedirect()
  const [formError, setFormError] = useState<string | null>(null)
  const [selected, setSelected] = useState<number[] | null>(null)

  const own = (mappings.data ?? []).filter((m) => m.officePublicId === office.publicId).map((m) => Number(m.courtId))
  const takenBy = new Map(
    (mappings.data ?? []).filter((m) => m.officePublicId !== office.publicId).map((m) => [Number(m.courtId), m.officeName]),
  )
  const chosen = selected ?? own
  const levelName = (code: string | null | undefined) => (levels.data ?? []).find((l) => l.code === code)?.name ?? ''
  const options = [...(courts.data ?? [])].sort(
    (a, b) => levelName(a.courtLevelCode).localeCompare(levelName(b.courtLevelCode)) || a.name.localeCompare(b.name),
  )
  const loading = courts.isPending || mappings.isPending || levels.isPending

  const submit = async () => {
    setFormError(null)
    try {
      await save.mutateAsync({ publicId: office.publicId, courtIds: chosen })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="office-courts-title">
      <DialogTitle id="office-courts-title">{t.courtsTitle(office.name)}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <DialogContentText>{t.courtsIntro}</DialogContentText>
          {(formError ?? mappings.isError) && <Alert severity="error">{formError ?? problemMessage(mappings.error)}</Alert>}
          {loading ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : (
            <Autocomplete<Court, true, false, false>
              multiple
              disableCloseOnSelect
              options={options}
              value={options.filter((c) => chosen.includes(Number(c.id)))}
              onChange={(_, value) => setSelected(value.map((c) => Number(c.id)))}
              getOptionLabel={(c) => c.name}
              isOptionEqualToValue={(a, b) => a.id === b.id}
              groupBy={(c) => levelName(c.courtLevelCode)}
              getOptionDisabled={(c) => takenBy.has(Number(c.id))}
              filterOptions={(all, state) => all.filter((c) => matches(state.inputValue, c.name))}
              noOptionsText={sq.search.noOptions}
              renderOption={(props, c, { selected: isSelected }) => {
                const { key, ...rest } = props
                return (
                  <li key={key} {...rest}>
                    <Checkbox checked={isSelected} size="small" sx={{ mr: 1 }} />
                    <Stack>
                      <span>{c.name}</span>
                      {takenBy.has(Number(c.id)) && (
                        <Typography variant="body2" color="text.secondary">
                          {t.courtsTakenBy(takenBy.get(Number(c.id)) ?? '')}
                        </Typography>
                      )}
                    </Stack>
                  </li>
                )
              }}
              renderInput={(params) => (
                <TextField
                  {...params}
                  label={t.courts}
                  placeholder={t.courtsSearch}
                  helperText={chosen.length === 0 ? t.courtsNone : t.courtsCount(chosen.length)}
                  autoFocus
                />
              )}
            />
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={save.isPending || loading}>
          {sq.common.save}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
