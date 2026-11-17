import { Stack } from '@mui/material'
import { ImportPanel } from '../../components/imports/ImportPanel'
import { PageHeader } from '../../components/ui/PageHeader'
import { sq } from '../../i18n/sq'

/**
 * "Organika" (task 053g, client decision E) on the shared import pipeline (task 053l): the instructions, the template,
 * the upload with its preview that writes NOTHING, "Apliko", the result report and the kept imports.
 * TODO(akshi-q-hrmis): an integration with the HR system would replace the sheet.
 */
export function OrganikaPage() {
  return (
    <Stack spacing={3}>
      <PageHeader title={sq.organika.title} />
      <ImportPanel kind="organika" />
    </Stack>
  )
}
