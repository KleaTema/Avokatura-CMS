import { Button, Stack } from '@mui/material'
import { sq } from '../../i18n/sq'

/** "Anulo" and "Ruaj" under an inline editor (blue: the page's one gold button is elsewhere). */
export function EditorActions({ onCancel, busy }: { onCancel: () => void; busy: boolean }) {
  return (
    <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
      <Button onClick={onCancel}>{sq.cases.edit.cancel}</Button>
      <Button type="submit" variant="contained" disabled={busy}>
        {sq.cases.edit.save}
      </Button>
    </Stack>
  )
}
