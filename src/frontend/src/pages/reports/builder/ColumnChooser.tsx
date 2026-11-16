import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  List,
  ListItem,
  Stack,
} from '@mui/material'
import { ArrowDown, ArrowUp } from 'lucide-react'
import { useState } from 'react'
import type { ReportColumnMeta } from '../../../features/reports/builder'
import { sq } from '../../../i18n/sq'

const t = sq.reports.builder

/**
 * "Kolonat" (task 053h): which columns the results and the exports show, and in which order. Every column of the
 * table is listed in the current order (chosen first, then the rest); checking adds, the arrows move. At least one.
 */
export function ColumnChooser({
  columns,
  chosen,
  onApply,
  onClose,
}: {
  columns: ReportColumnMeta[]
  /** the chosen keys in their order */
  chosen: string[]
  onApply: (keys: string[]) => void
  onClose: () => void
}) {
  const [order, setOrder] = useState<string[]>(() => [
    ...chosen.filter((k) => columns.some((c) => c.key === k)),
    ...columns.map((c) => c.key).filter((k) => !chosen.includes(k)),
  ])
  const [checked, setChecked] = useState<Set<string>>(() => new Set(chosen))
  const label = (key: string) => columns.find((c) => c.key === key)?.header ?? key

  const move = (index: number, by: -1 | 1) =>
    setOrder((current) => {
      const next = [...current]
      const [item] = next.splice(index, 1)
      next.splice(index + by, 0, item)
      return next
    })

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-labelledby="columns-title">
      <DialogTitle id="columns-title">{t.columnsTitle}</DialogTitle>
      <DialogContent dividers>
        <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
          <Button size="small" onClick={() => setChecked(new Set(order))}>
            {t.columnsAll}
          </Button>
          <Button size="small" onClick={() => setChecked(new Set())}>
            {t.columnsNone}
          </Button>
        </Stack>
        <List dense disablePadding>
          {order.map((key, index) => (
            <ListItem
              key={key}
              disableGutters
              secondaryAction={
                <Stack direction="row">
                  <IconButton size="small" disabled={index === 0} onClick={() => move(index, -1)} aria-label={t.moveUp(label(key))}>
                    <ArrowUp size={16} aria-hidden />
                  </IconButton>
                  <IconButton
                    size="small"
                    disabled={index === order.length - 1}
                    onClick={() => move(index, 1)}
                    aria-label={t.moveDown(label(key))}
                  >
                    <ArrowDown size={16} aria-hidden />
                  </IconButton>
                </Stack>
              }
            >
              <FormControlLabel
                control={
                  <Checkbox
                    checked={checked.has(key)}
                    onChange={(e) =>
                      setChecked((current) => {
                        const next = new Set(current)
                        if (e.target.checked) next.add(key)
                        else next.delete(key)
                        return next
                      })
                    }
                  />
                }
                label={label(key)}
              />
            </ListItem>
          ))}
        </List>
        {checked.size === 0 && (
          <Alert severity="warning" sx={{ mt: 1 }}>
            {t.atLeastOneColumn}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{sq.common.cancel}</Button>
        <Button variant="contained" disabled={checked.size === 0} onClick={() => onApply(order.filter((k) => checked.has(k)))}>
          {t.apply}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
