import { Button } from '@mui/material'
import { Pencil } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { sq } from '../../i18n/sq'
import { SectionCard } from '../ui/SectionCard'

/**
 * A data card that edits in place: the read-only view with "Ndrysho" (only when `canEdit`), or the editor. The editor
 * gets `close` — called after a successful save or on "Anulo".
 */
export function EditableSection({
  title,
  canEdit,
  view,
  editor,
}: {
  title: string
  canEdit: boolean
  view: ReactNode
  editor: (close: () => void) => ReactNode
}) {
  const [editing, setEditing] = useState(false)
  const close = () => setEditing(false)

  return (
    <SectionCard
      title={title}
      actions={
        canEdit &&
        !editing && (
          <Button
            variant="outlined"
            size="small"
            startIcon={<Pencil size={16} aria-hidden />}
            onClick={() => setEditing(true)}
            aria-label={sq.cases.edit.editSection(title)}
          >
            {sq.cases.edit.edit}
          </Button>
        )
      }
    >
      {editing && canEdit ? editor(close) : view}
    </SectionCard>
  )
}
