import { Button, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { sq } from '../i18n/sq'

function ErrorPage({ message }: { message: string }) {
  return (
    <div>
      <Typography variant="h5" gutterBottom>
        {message}
      </Typography>
      <Button component={RouterLink} to="/">
        {sq.errors.backHome}
      </Button>
    </div>
  )
}

export const ForbiddenPage = () => <ErrorPage message={sq.errors.forbidden} />
export const NotFoundPage = () => <ErrorPage message={sq.errors.notFound} />
export const GenericErrorPage = () => <ErrorPage message={sq.errors.generic} />
