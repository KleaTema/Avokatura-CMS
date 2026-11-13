import { expect, test } from '@playwright/test'
import { USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053d: the Monitor's "Mbikëqyrja" screen. The local data has one office only, so a real supervision (a lawyer
 * or case of ANOTHER office) is covered by the integration tests (SupervisionTests); here: the menu, the page, the
 * dialog, and the API's rule shown on its field (a lawyer of the Kryetar's own office is refused).
 */
test('the Monitor opens "Mbikëqyrja" and the rules come back on the fields', async ({ page }) => {
  await login(page, USERS.monitor)
  await page.getByRole('link', { name: 'Mbikëqyrja' }).click()
  await expect(page.getByRole('heading', { name: 'Mbikëqyrja', level: 1 })).toBeVisible()

  await page.getByRole('button', { name: 'Shto mbikëqyrje' }).click()
  const dialog = page.getByRole('dialog', { name: 'Shto mbikëqyrje' })
  const add = dialog.getByRole('button', { name: 'Shto mbikëqyrje' })
  await expect(add).toBeDisabled()

  await dialog.getByRole('combobox', { name: /Kryetari/ }).click()
  await page.getByRole('option', { name: /^Kryetar Dev/ }).click()
  await dialog.getByRole('combobox', { name: /^Avokati/ }).click()
  await page.getByRole('option', { name: /^Avokat1 Dev/ }).click()
  await add.click()
  await expect(dialog.getByText('Avokati është i zyrës së këtij Kryetari: çështjet e tij i sheh tashmë.')).toBeVisible()

  // a case: an unknown code
  await dialog.getByRole('combobox', { name: /Çfarë mbikëqyr/ }).click()
  await page.getByRole('option', { name: 'Një çështje' }).click()
  await dialog.getByLabel(/Kodi i çështjes/).fill('NUK-EKZISTON/1')
  await add.click()
  await expect(dialog.getByText('Çështja me këtë kod nuk u gjet.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Anulo' }).click()

  // the Kryetar has no such menu
  await login(page, USERS.kryetar)
  await expect(page.getByRole('link', { name: 'Mbikëqyrja' })).toHaveCount(0)
})
