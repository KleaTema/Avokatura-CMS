import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Skeleton,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { Lock, Pencil } from 'lucide-react'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { NotificationIcon } from '../components/NotificationIcon'
import { PageHeader } from '../components/ui/PageHeader'
import { SectionCard } from '../components/ui/SectionCard'
import { SuccessToast } from '../components/ui/SuccessToast'
import { useMe } from '../features/identity/api'
import { useProfile, useSetNotification, useUpdatePhone, type Profile } from '../features/identity/profile'
import { sq } from '../i18n/sq'
import { formatDateTime } from '../lib/dates'
import { formatPhone } from '../lib/phone'
import { problemMessage } from '../lib/problem'

const t = sq.profile

/** Shown only to administrators: the partition alert goes to them alone. */
const ADMIN_ONLY_TYPES = ['PARTITION_ALERT']

/** A read-only label / value pair. */
function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="p">
        {label}
      </Typography>
      <Typography variant="body1">{children}</Typography>
    </Box>
  )
}

/** "Kontakti": the phone, shown formatted, edited inline (Enter saves, Esc cancels). */
function PhoneCard({ phone, onSaved }: { phone: string | null | undefined; onSaved: () => void }) {
  const update = useUpdatePhone()
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState('')
  const helpId = useId()

  const start = () => {
    update.reset()
    setValue(phone ? formatPhone(phone) : '')
    setEditing(true)
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    update.mutate(value.trim(), {
      onSuccess: () => {
        setEditing(false)
        onSaved()
      },
    })
  }

  return (
    <SectionCard title={t.contact}>
      {editing ? (
        <Box component="form" onSubmit={submit} noValidate>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { sm: 'flex-start' } }}>
            <TextField
              label={t.phone}
              value={value}
              onChange={(e) => setValue(e.target.value)}
              onKeyDown={(e) => e.key === 'Escape' && setEditing(false)}
              placeholder={t.phonePlaceholder}
              autoFocus
              type="tel"
              autoComplete="tel"
              error={update.isError}
              helperText={update.isError ? problemMessage(update.error) : t.phoneHelp}
              slotProps={{ htmlInput: { maxLength: 30, 'aria-describedby': helpId }, formHelperText: { id: helpId } }}
              sx={{ flex: 1, maxWidth: { sm: 420 } }}
            />
            <Stack direction="row" spacing={1} sx={{ pt: { sm: 1 } }}>
              <Button type="submit" variant="contained" disabled={update.isPending}>
                {t.save}
              </Button>
              <Button onClick={() => setEditing(false)} disabled={update.isPending}>
                {t.cancel}
              </Button>
            </Stack>
          </Stack>
        </Box>
      ) : (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Box sx={{ flex: 1 }}>
            <Field label={t.phone}>
              {phone ? (
                formatPhone(phone)
              ) : (
                <Typography component="span" color="text.secondary">
                  {t.noPhone}
                </Typography>
              )}
            </Field>
          </Box>
          <Tooltip title={t.editPhone}>
            <IconButton onClick={start} aria-label={t.editPhone}>
              <Pencil size={20} aria-hidden />
            </IconButton>
          </Tooltip>
        </Stack>
      )}
    </SectionCard>
  )
}

/** "Njoftimet": one switch per type; mandatory ones on, disabled, with a lock and a tooltip. */
function NotificationsCard({ profile, isAdmin, onSaved }: { profile: Profile; isAdmin: boolean; onSaved: () => void }) {
  const set = useSetNotification()
  const items = profile.notifications.filter((n) => isAdmin || !ADMIN_ONLY_TYPES.includes(n.type))

  return (
    <SectionCard title={t.notificationsTitle} padded={false}>
      <Typography variant="body2" color="text.secondary" sx={{ px: 2.5, pt: 2 }}>
        {t.notificationsNote}
      </Typography>
      {set.isError && (
        <Alert severity="error" sx={{ mx: 2.5, mt: 1.5 }}>
          {problemMessage(set.error)}
        </Alert>
      )}
      <List>
        {items.map((n) => {
          const label = t.types[n.type] ?? n.type
          const toggle = (
            <Switch
              edge="end"
              checked={n.enabled}
              disabled={n.mandatory || set.isPending}
              onChange={(e) => set.mutate({ type: n.type, enabled: e.target.checked }, { onSuccess: onSaved })}
              slotProps={{ input: { 'aria-label': label } }}
            />
          )
          return (
            <ListItem
              key={n.type}
              sx={{ px: 2.5 }}
              secondaryAction={
                n.mandatory ? (
                  <Tooltip title={t.mandatory}>
                    {/* a disabled switch gets no pointer events: the tooltip sits on its wrapper */}
                    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', color: 'text.secondary' }} tabIndex={0} aria-label={`${label}: ${t.mandatory}`}>
                      <Lock size={18} aria-hidden />
                      {toggle}
                    </Stack>
                  </Tooltip>
                ) : (
                  toggle
                )
              }
            >
              <ListItemIcon sx={{ minWidth: 36, color: 'text.secondary' }}>
                <NotificationIcon type={n.type} />
              </ListItemIcon>
              <ListItemText primary={label} slotProps={{ primary: { variant: 'body2' } }} sx={{ pr: n.mandatory ? 10 : 6 }} />
            </ListItem>
          )
        })}
      </List>
    </SectionCard>
  )
}

/**
 * "Profili" (task 048): the user's own data, read-only (they come from the identity provider), the phone — the only
 * editable field (TODO(client-q3): which fields per role are editable) — and the optional notification types.
 */
export function ProfilePage() {
  const profile = useProfile()
  const me = useMe(true)
  const [toast, setToast] = useState<string | null>(null)
  const isAdmin = me.data?.permissions.includes('menu.admin') ?? false
  const p = profile.data

  return (
    <>
      <PageHeader title={t.title} subtitle={t.subtitle} />
      {profile.isError && <Alert severity="error">{t.loadError}</Alert>}
      {profile.isPending && <Skeleton variant="rounded" height={240} />}
      {p && (
        <Box sx={{ display: 'grid', gap: 2.5, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, alignItems: 'start' }}>
          <Stack spacing={2.5}>
            <SectionCard title={t.data}>
              <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' } }}>
                <Field label={t.firstName}>{p.firstName}</Field>
                <Field label={t.lastName}>{p.lastName}</Field>
                <Field label={t.email}>{p.email}</Field>
                <Field label={t.lastLogin}>{p.lastLoginAt ? formatDateTime(p.lastLoginAt) : t.neverLoggedIn}</Field>
              </Box>
              <Alert severity="info" sx={{ mt: 2 }}>
                {t.dataNote}
              </Alert>
            </SectionCard>
            <PhoneCard phone={p.phone} onSaved={() => setToast(t.phoneSaved)} />
            <SectionCard title={t.rolesTitle}>
              <Stack spacing={2}>
                {p.office && <Field label={t.office}>{p.office.name}</Field>}
                {p.institution && <Field label={t.institution}>{p.institution.name}</Field>}
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                  {p.roles.map((r) => (
                    <Chip key={r.code} label={r.name} />
                  ))}
                  {p.headOf && <Chip color="primary" variant="outlined" label={t.headOf(p.headOf)} />}
                </Stack>
              </Stack>
            </SectionCard>
          </Stack>
          <NotificationsCard profile={p} isAdmin={isAdmin} onSaved={() => setToast(t.notificationsSaved)} />
        </Box>
      )}
      <SuccessToast message={toast} onClose={() => setToast(null)} />
    </>
  )
}
