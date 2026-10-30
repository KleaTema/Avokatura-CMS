import { Button, Stack, Typography } from '@mui/material'
import { Link } from 'react-router'
import { useMe } from '../../features/identity/api'
import { sq } from '../../i18n/sq'

const t = sq.cases

/** /cases until the list (task 022): the registration actions the user may use. */
export function CasesHome() {
  const me = useMe(true)
  const permissions = me.data?.permissions ?? []
  return (
    <Stack spacing={3}>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}
      >
        <Typography variant="h4" component="h1">
          {t.title}
        </Typography>
        <Stack direction="row" spacing={2}>
          {permissions.includes('case.create') && (
            <Button variant="contained" component={Link} to="/cases/register">
              {t.registerNew}
            </Button>
          )}
          {permissions.includes('case.link_previous') && (
            <Button variant="outlined" component={Link} to="/cases/continue">
              {t.continueExisting}
            </Button>
          )}
        </Stack>
      </Stack>
      <Typography>{t.listComingSoon}</Typography>
    </Stack>
  )
}
