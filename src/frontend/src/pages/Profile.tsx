import { Alert, Box, Chip, Skeleton, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { PageHeader } from '../components/ui/PageHeader'
import { SectionCard } from '../components/ui/SectionCard'
import { useProfile } from '../features/identity/profile'
import { sq } from '../i18n/sq'
import { formatDateTime } from '../lib/dates'

const t = sq.profile

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

/** A value that may be missing: the dash in muted ink. */
function Optional({ value }: { value: string | null | undefined }) {
  return value ? (
    <>{value}</>
  ) : (
    <Typography component="span" color="text.secondary">
      {t.notSet}
    </Typography>
  )
}

/**
 * "Profili" (tasks 048 / 053g): the user's own data, read-only — name and e-mail (identity provider), position, unit
 * and manager (Organika), role(s), office or institution. Client decision E: no phone and no notification switches;
 * every notification is mandatory.
 */
export function ProfilePage() {
  const profile = useProfile()
  const p = profile.data

  return (
    <>
      <PageHeader title={t.title} subtitle={t.subtitle} />
      {profile.isError && <Alert severity="error">{t.loadError}</Alert>}
      {profile.isPending && <Skeleton variant="rounded" height={240} />}
      {p && (
        <Box sx={{ display: 'grid', gap: 2.5, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, alignItems: 'start' }}>
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
          <Stack spacing={2.5}>
            <SectionCard title={t.positionTitle}>
              <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' } }}>
                <Field label={t.position}>
                  <Optional value={p.position} />
                </Field>
                <Field label={t.orgUnit}>
                  <Optional value={p.orgUnit} />
                </Field>
                <Field label={t.manager}>
                  <Optional value={p.manager} />
                </Field>
              </Box>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
                {t.positionNote}
              </Typography>
            </SectionCard>
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
        </Box>
      )}
    </>
  )
}
