import { expect, test } from '@playwright/test'
import { registerCase, USERS } from './support/api'
import { login } from './support/login'

/**
 * Tasks 053b / 053j: "Vazhdo çështje ekzistuese" — a table of every dossier of the country (default "Mund të
 * vazhdohen"), the client's filters from the server's registry, the status, "+ Shto filtër", all kept in the URL.
 */
test('the table filters by its fields, the status and an added filter, and keeps them in the URL', async ({ page }) => {
  const created = await registerCase() // REGISTERED: listed under "Të gjitha", with "E pambyllur në sistem"
  const tag = created.actNumber.slice('E2E-'.length)
  await login(page, USERS.protokoll)
  await page.goto('/cases/continue')

  const search = page.getByRole('search')
  for (const label of ['Kodi unik', 'Nr. i aktit', 'Shkalla', 'Paditësi', 'I padituri', 'Objekti', 'Statusi']) {
    await expect(search.getByLabel(label, { exact: true })).toBeVisible()
  }
  const table = page.getByRole('table', { name: 'Dosjet e të gjitha zyrave' })
  await expect(table).toBeVisible()

  // the plaintiff finds it — but only with "Të gjitha": by default only the cases that can continue are listed
  await search.getByLabel('Paditësi', { exact: true }).fill(`E2E Paditës ${tag}`)
  await expect(page.getByText('Asnjë dosje nuk përputhet me filtrat.')).toBeVisible()
  await search.getByLabel('Statusi', { exact: true }).click()
  await page.getByRole('option', { name: 'Të gjitha' }).click()
  const row = table.getByRole('row').filter({ hasText: created.code })
  await expect(row).toBeVisible()
  await expect(row.getByText(`E2E test ${tag}`)).toBeVisible()
  await expect(row.getByText('E pambyllur në sistem')).toBeVisible()
  await expect(row.getByRole('button', { name: /^Vazhdo/ })).toHaveCount(0)
  await expect(page).toHaveURL(/plaintiff=/)
  await expect(page).toHaveURL(/status=ALL/)

  // "+ Shto filtër" → Zyra: the report builder's filter, with the offices that have cases
  await page.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Zyra', exact: true }).click()
  await expect(page.getByLabel('Zyra', { exact: true })).toBeVisible()

  // "Pastro filtrat" empties every field and goes back to "Mund të vazhdohen"
  await page.getByRole('button', { name: 'Pastro filtrat' }).click()
  await expect(search.getByLabel('Paditësi', { exact: true })).toHaveValue('')
  await expect(page).not.toHaveURL(/status=/)

  // ?actNumber= (the duplicate dialog's link) fills the field
  await page.goto(`/cases/continue?actNumber=${encodeURIComponent(created.actNumber)}&status=ALL`)
  await expect(page.getByRole('search').getByLabel('Nr. i aktit', { exact: true })).toHaveValue(created.actNumber)
  await expect(page.getByRole('table').getByRole('row').filter({ hasText: created.code })).toBeVisible()
})
