import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Link,
  Stack,
} from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { OptionPicker } from '../../components/OptionPicker'
import { useOfficeHeadCandidates, useSetOfficeHead, type AdminOffice } from '../../features/admin/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { useForbiddenRedirect } from './lists/helpers'

const t = sq.adminOffices

/** Picks the head among the active users of this office with role OFFICE_HEAD, or clears it. */
export function OfficeHeadDialog({ office, onClose }: { office: AdminOffice; onClose: () => void }) {
  const candidates = useOfficeHeadCandidates(office.publicId)
  const setHead = useSetOfficeHead()
  const redirectIfForbidden = useForbiddenRedirect()
  const [selected, setSelected] = useState(office.head?.publicId ?? '')
  const [formError, setFormError] = useState<string | null>(null)

  const options = (candidates.data ?? []).map((u) => ({ publicId: u.publicId, name: `${u.firstName} ${u.lastName}` }))
  // keep the current head selectable even if the list does not contain it
  if (office.head && !options.some((o) => o.publicId === office.head?.publicId)) options.unshift(office.head)
  const noCandidates = candidates.isSuccess && options.length === 0

  const save = async () => {
    setFormError(null)
    try {
      await setHead.mutateAsync({ publicId: office.publicId, userPublicId: selected || null })
      onClose()
    } catch (error) {
      if (!redirectIfForbidden(error)) setFormError(problemMessage(error))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" aria-labelledby="office-head-title">
      <DialogTitle id="office-head-title">{t.head.title(office.name)}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {(formError ?? candidates.isError) && (
            <Alert severity="error">{formError ?? problemMessage(candidates.error)}</Alert>
          )}
          {candidates.isPending ? (
            <CircularProgress aria-label={sq.errors.loading} />
          ) : noCandidates ? (
            <Alert severity="info">
              {t.head.noCandidates}.{' '}
              <Link component={RouterLink} to="/admin/users">
                {t.head.goToUsers}
              </Link>
            </Alert>
          ) : (
            <OptionPicker
              options={options.map((o) => ({ value: o.publicId, label: o.name }))}
              value={selected}
              onChange={setSelected}
              label={t.head.label}
              helperText={selected ? t.head.hint : `${t.head.none}. ${t.head.hint}`}
              autoFocus
            />
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button
          variant="contained"
          onClick={() => void save()}
          disabled={setHead.isPending || candidates.isPending || noCandidates}
        >
          {sq.common.save}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
