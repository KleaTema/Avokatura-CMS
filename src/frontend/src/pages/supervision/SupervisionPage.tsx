import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  Link as MuiLink,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../../api/client'
import { OptionPicker } from '../../components/OptionPicker'
import { AppSelect } from '../../components/ui/AppSelect'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { SuccessToast } from '../../components/ui/SuccessToast'
import { preventEnterSubmit, useFullScreenDialog } from '../../components/ui/dialog'
import {
  useCreateSupervision,
  useEndSupervision,
  useSupervisionPeople,
  useSupervisions,
  type Supervision,
} from '../../features/supervision/api'
import { sq } from '../../i18n/sq'
import { formatDate } from '../../lib/dates'
import { problemMessage } from '../../lib/problem'

const t = sq.supervision

/** What a supervision covers, in words: the lawyer, or the case code. */
const targetOf = (s: Supervision) => (s.kind === 'LAWYER' ? (s.lawyer?.name ?? '') : (s.case?.caseCode ?? ''))

/**
 * "Mbikëqyrja" (task 053d, client decision C, default D3): the Monitor gives a Kryetar a lawyer (every case where
 * the lawyer is the current primary, now and later) or single cases, and ends it. The Kryetar is notified and finds
 * them under "Në mbikëqyrje".
 */
export function SupervisionPage() {
  const [includeEnded, setIncludeEnded] = useState(false)
  const list = useSupervisions(includeEnded)
  const [adding, setAdding] = useState(false)
  const [ending, setEnding] = useState<Supervision | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const rows = list.data ?? []

  return (
    <Stack spacing={3}>
      <PageHeader
        title={t.title}
        actions={
          <Button variant="contained" color="secondary" onClick={() => setAdding(true)}>
            {t.add}
          </Button>
        }
      />
      <Typography color="text.secondary">{t.intro}</Typography>
      <FormControlLabel
        control={<Switch checked={includeEnded} onChange={(e) => setIncludeEnded(e.target.checked)} />}
        label={t.showEnded}
      />
      {list.isPending && <CircularProgress aria-label={sq.errors.loading} />}
      {list.isError && <Alert severity="error">{problemMessage(list.error)}</Alert>}
      {list.isSuccess && rows.length === 0 && <EmptyState icon={ShieldCheck} title={t.empty} />}
      {rows.length > 0 && (
        <Paper variant="outlined" sx={{ overflowX: 'auto' }}>
          <Table size="small" aria-label={t.title}>
            <TableHead>
              <TableRow>
                <TableCell>{t.head}</TableCell>
                <TableCell>{t.kind}</TableCell>
                <TableCell>{t.target}</TableCell>
                <TableCell align="right">{t.covered}</TableCell>
                <TableCell>{t.since}</TableCell>
                <TableCell>{t.by}</TableCell>
                <TableCell align="right">{sq.common.actions}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((s) => (
                <TableRow key={s.publicId}>
                  <TableCell>
                    {s.head.name}
                    {s.head.officeName && (
                      <Typography variant="body2" color="text.secondary">
                        {s.head.officeName}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell>{t.kindShort[s.kind] ?? s.kind}</TableCell>
                  <TableCell>
                    {s.case ? (
                      <MuiLink component={Link} to={`/cases/${s.case.publicId}`}>
                        {s.case.caseCode}
                      </MuiLink>
                    ) : (
                      <>
                        {s.lawyer?.name}
                        {s.lawyer?.officeName && (
                          <Typography variant="body2" color="text.secondary">
                            {s.lawyer.officeName}
                          </Typography>
                        )}
                      </>
                    )}
                  </TableCell>
                  <TableCell align="right">{s.coveredCases}</TableCell>
                  <TableCell>{formatDate(s.createdAt)}</TableCell>
                  <TableCell>{s.createdBy}</TableCell>
                  <TableCell align="right">
                    {s.endedAt ? (
                      <Typography variant="body2" color="text.secondary">
                        {t.endedAt} {formatDate(s.endedAt)}
                      </Typography>
                    ) : (
                      <Button size="small" color="error" onClick={() => setEnding(s)} aria-label={t.endFor(targetOf(s))}>
                        {t.end}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Paper>
      )}

      {adding && (
        <AddSupervisionDialog
          onClose={() => setAdding(false)}
          onCreated={() => {
            setAdding(false)
            setToast(t.created)
          }}
        />
      )}
      {ending && (
        <EndSupervisionDialog
          supervision={ending}
          onClose={() => setEnding(null)}
          onEnded={() => {
            setEnding(null)
            setToast(t.ended)
          }}
        />
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </Stack>
  )
}

function AddSupervisionDialog({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const fullScreen = useFullScreenDialog()
  const people = useSupervisionPeople()
  const create = useCreateSupervision()
  const [head, setHead] = useState('')
  const [kind, setKind] = useState('LAWYER')
  const [lawyer, setLawyer] = useState('')
  const [caseCode, setCaseCode] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const complete = head !== '' && (kind === 'LAWYER' ? lawyer !== '' : caseCode.trim() !== '')

  const option = (p: { publicId: string; name: string; officeName?: string | null }) => ({
    value: p.publicId,
    label: p.officeName ? `${p.name} — ${p.officeName}` : p.name,
  })

  const save = async () => {
    setErrors({})
    setFormError(null)
    try {
      await create.mutateAsync({
        headUserPublicId: head,
        kind,
        lawyerUserPublicId: kind === 'LAWYER' ? lawyer : null,
        caseCode: kind === 'CASE' ? caseCode.trim() : null,
      })
      onCreated()
    } catch (error) {
      const found: Record<string, string> = {}
      const fields = error instanceof ApiError ? (error.problem?.errors ?? {}) : {}
      for (const [key, messages] of Object.entries(fields)) found[key] = messages.join(' ')
      setErrors(found)
      if (Object.keys(found).length === 0) setFormError(problemMessage(error))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm" fullScreen={fullScreen} aria-labelledby="add-supervision-title">
      <Box
        component="form"
        noValidate
        onKeyDown={preventEnterSubmit}
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <DialogTitle id="add-supervision-title">{t.addTitle}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            {formError && <Alert severity="error">{formError}</Alert>}
            {people.isError && <Alert severity="error">{problemMessage(people.error)}</Alert>}
            <OptionPicker
              required
              label={t.head}
              options={(people.data?.heads ?? []).map(option)}
              value={head}
              onChange={setHead}
              error={!!errors.headUserPublicId}
              helperText={errors.headUserPublicId}
              autoFocus
            />
            <AppSelect
              required
              label={t.kind}
              value={kind}
              onChange={setKind}
              options={Object.entries(t.kinds).map(([value, label]) => ({ value, label }))}
              error={!!errors.kind}
              helperText={errors.kind}
            />
            {kind === 'LAWYER' ? (
              <OptionPicker
                required
                label={t.lawyer}
                options={(people.data?.lawyers ?? []).map(option)}
                value={lawyer}
                onChange={setLawyer}
                error={!!errors.lawyerUserPublicId}
                helperText={errors.lawyerUserPublicId}
              />
            ) : (
              <TextField
                required
                label={t.caseCode}
                value={caseCode}
                onChange={(e) => setCaseCode(e.target.value)}
                error={!!errors.caseCode}
                helperText={errors.caseCode ?? t.caseCodeHint}
                slotProps={{ htmlInput: { maxLength: 40 } }}
              />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{sq.common.cancel}</Button>
          <Button type="submit" variant="contained" color="secondary" disabled={!complete || create.isPending}>
            {t.add}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}

function EndSupervisionDialog({
  supervision,
  onClose,
  onEnded,
}: {
  supervision: Supervision
  onClose: () => void
  onEnded: () => void
}) {
  const end = useEndSupervision()
  const [error, setError] = useState<string | null>(null)
  const confirm = async () => {
    setError(null)
    try {
      await end.mutateAsync(supervision.publicId)
      onEnded()
    } catch (e) {
      setError(problemMessage(e))
    }
  }

  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="end-supervision-title">
      <DialogTitle id="end-supervision-title">{t.endTitle}</DialogTitle>
      <DialogContent>
        {error && <Alert severity="error">{error}</Alert>}
        <DialogContentText>{t.endText(supervision.head.name, targetOf(supervision))}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" color="error" onClick={() => void confirm()} disabled={end.isPending}>
          {t.end}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
