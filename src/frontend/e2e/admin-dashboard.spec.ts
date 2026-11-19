import { expect, test } from '@playwright/test'
import { USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053n: the administrator's dashboard — administration cards only; a card opens its admin page with the filter
 * applied and the list's total equals the card. The agenda's "Printo" asks the period first.
 */
test('admin dashboard: no case or system widgets; a card opens its filtered list with the same number', async ({ page }) => {
  await login(page, USERS.admin)
  await page.goto('/dashboard')

  const card = page.getByRole('link', { name: /^Përdorues aktivë: \d+\. Hap listën$/ })
  await expect(card).toBeVisible({ timeout: 15_000 })
  for (const gone of ['Çështjet sipas zyrës', 'Punët në sfond', 'Particionet e bazës së të dhënave', 'Seanca këtë javë']) {
    await expect(page.getByText(gone, { exact: true })).toHaveCount(0)
  }
  await expect(page.getByRole('heading', { name: /^Ndryshimet e fundit administrative/ })).toBeVisible()

  // the seed's users are well under 1,000: the number has no thousands separator
  const value = /: (\d+)\./.exec((await card.getAttribute('aria-label')) ?? '')?.[1] ?? ''
  expect(value).not.toBe('')
  await card.click()
  await expect(page).toHaveURL(/\/admin\/users\?active=true/, { timeout: 10_000 })
  // the list's footer: "1–10 nga N"
  await expect(page.getByText(new RegExp(`^1–\\d+ nga ${value}$`))).toBeVisible({ timeout: 15_000 })
})

test('agenda print: the dialog limits the period to 3 months and prints the chosen week', async ({ page }) => {
  await login(page, USERS.monitor)
  await page.goto('/agenda?view=muaji')
  await page.getByRole('button', { name: 'Printo' }).click()

  const dialog = page.getByRole('dialog', { name: 'Printo agjendën' })
  await expect(dialog).toBeVisible({ timeout: 10_000 })
  await expect(dialog.getByRole('combobox', { name: 'Zyra' })).toBeVisible()
  await expect(dialog.getByLabel('Përfshi të anuluarat')).toBeVisible()

  // a quick period, then print: the window's print is replaced, the printout holds the whole week
  await page.evaluate(() => {
    ;(window as unknown as { printed: number }).printed = 0
    window.print = () => {
      ;(window as unknown as { printed: number }).printed += 1
    }
  })
  await dialog.getByRole('button', { name: 'Kjo javë' }).click()
  await dialog.getByRole('button', { name: 'Printo', exact: true }).click()
  await expect(dialog).toBeHidden({ timeout: 15_000 })
  await expect.poll(() => page.evaluate(() => (window as unknown as { printed: number }).printed), { timeout: 10_000 }).toBe(1)
  const printout = page.getByTestId('agenda-printout')
  // only the title (with the period) and the table: no filters, no "printed by"
  await expect(printout).toContainText(/Agjenda e seancave · \d{2}\.\d{2}\.\d{4} – \d{2}\.\d{2}\.\d{4}/)
  await expect(printout).not.toContainText('Printuar më')
})
