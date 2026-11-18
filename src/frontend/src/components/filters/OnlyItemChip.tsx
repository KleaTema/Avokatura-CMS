import { Chip } from '@mui/material'
import { useSearchParams } from 'react-router'
import { sq } from '../../i18n/sq'

/**
 * Task 053n — a list opened on ONE item (a row of the admin dashboard's "Ndryshimet e fundit"): "Vetëm: {name}" with ×,
 * which shows the whole list again. The page shows the item whatever its status (inactive ones clearly marked).
 */
export function OnlyItemChip({ param, name }: { param: string; name: string | undefined }) {
  const [params, setParams] = useSearchParams()
  if (!params.get(param)) return null
  return (
    <Chip
      sx={{ mb: 2 }}
      label={sq.filters.only(name ?? sq.filters.oneItem)}
      onDelete={() =>
        setParams(
          (prev) => {
            const next = new URLSearchParams(prev)
            next.delete(param)
            next.delete('page')
            return next
          },
          { replace: true },
        )
      }
    />
  )
}
