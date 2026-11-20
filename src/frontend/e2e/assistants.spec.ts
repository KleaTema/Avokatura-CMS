import { expect, test } from '@playwright/test'
import { assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053q: the primary lawyer adds a legal assistant of the office to the case on "Ekipi" and removes them again.
 * The Kryetar has no "Ndihmësit ligjorë" menu any more. A new case, so the local data stays as it was.
 */
test('the primary lawyer adds an assistant to the case and removes them', async ({ page }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')

  await login(page, USERS.kryetar)
  await expect(page.getByRole('link', { name: 'Ndihmësit ligjorë' })).toHaveCount(0)

  await login(page, USERS.avokat1)
  await page.goto(`/cases/${created.id}`)
  await page.getByRole('button', { name: 'Shto ndihmës' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: /Ndihmësi ligjor/ }).click()
  await page.getByRole('option', { name: /Ndihmës Dev/ }).click()
  await dialog.getByRole('button', { name: 'Shto', exact: true }).click()
  await expect(page.getByText('Ndihmës Dev u shtua si ndihmës ligjor.')).toBeVisible()

  await page.getByRole('button', { name: 'Hiq ndihmësin Ndihmës Dev' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Hiq', exact: true }).click()
  await expect(page.getByText('Ndihmës Dev u hoq nga çështja.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Hiq ndihmësin Ndihmës Dev' })).toHaveCount(0)
})
