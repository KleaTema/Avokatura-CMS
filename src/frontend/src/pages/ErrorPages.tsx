import { Button, Paper } from '@mui/material'
import { CircleAlert } from 'lucide-react'
import { Link as RouterLink } from 'react-router'
import { EmptyState } from '../components/ui/EmptyState'
import { sq } from '../i18n/sq'

function ErrorPage({ message }: { message: string }) {
  return (
    <Paper variant="outlined">
      <EmptyState
        icon={CircleAlert}
        title={message}
        action={
          <Button variant="outlined" component={RouterLink} to="/">
            {sq.errors.backHome}
          </Button>
        }
      />
    </Paper>
  )
}

export const ForbiddenPage = () => <ErrorPage message={sq.errors.forbidden} />
export const NotFoundPage = () => <ErrorPage message={sq.errors.notFound} />
export const GenericErrorPage = () => <ErrorPage message={sq.errors.generic} />
