import { Typography } from '@mui/material'
import { sq } from '../i18n/sq'

export function PlaceholderPage({ menu }: { menu: string }) {
  return (
    <>
      <Typography variant="h4" component="h1" gutterBottom>
        {sq.menu[menu] ?? menu}
      </Typography>
      <Typography>{sq.placeholder.comingSoon}</Typography>
    </>
  )
}
