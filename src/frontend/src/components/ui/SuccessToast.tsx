import { Alert, Snackbar } from '@mui/material'

/** A short confirmation at the bottom of the page after an action; `message` null = hidden. */
export function SuccessToast({ message, onClose }: { message: string | null; onClose: () => void }) {
  return (
    <Snackbar
      open={message !== null}
      autoHideDuration={6000}
      onClose={(_, reason) => reason !== 'clickaway' && onClose()}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
    >
      {/* the tinted (standard) alert: white on the success green would be below 4.5:1 */}
      <Alert severity="success" onClose={onClose} sx={{ width: '100%', boxShadow: 3 }}>
        {message}
      </Alert>
    </Snackbar>
  )
}
