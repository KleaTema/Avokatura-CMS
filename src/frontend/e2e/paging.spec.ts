import { expect, test } from '@playwright/test'
import { API_URL, registerCase, USERS } from './support/api'
import { login } from './support/login'

/** How many cases the Monitor finds with "E2E" (every E2E case has it in its act number), from the list's total. */
async function e2eCaseCount(): Promise<number> {
  const response = await fetch(`${API_URL}/api/cases?tab=all&q=E2E&limit=1`, { headers: { 'X-Dev-User': USERS.monitor } })
  const page = (await response.json()) as { total: number | string }
  return Number(page.total)
}

/**
 * The case list pages with "‹ Para" / "Pas ›" (10 rows, keyset cursors), keeps the page in the URL, and the case
 * page's "Kthehu te lista e çështjeve" returns to the same page with the same search.
 */
test('the case list pages by 10 and the back link returns to the same page', async ({ page }) => {
  // at least 11 E2E cases, so there is a second page (earlier runs usually left plenty)
  for (let count = await e2eCaseCount(); count < 11; count++) await registerCase()
  const total = await e2eCaseCount()

  await login(page, USERS.monitor)
  await page.goto('/cases?tab=all&q=E2E')
  // every row is a link to its case (the list is a div table, so the rows are found by their links)
  const rows = page.locator('a[href^="/cases/"]').filter({ hasText: /\d{4}-\d+\/\d+/ })
  await expect(page.getByText(`1–10 nga ${total}`)).toBeVisible()
  await expect(rows).toHaveCount(10)

  await page.getByRole('button', { name: 'Pas ›' }).click()
  await expect(page).toHaveURL(/[?&]page=2(&|$)/)
  await expect(page.getByText(new RegExp(`^11–\\d+ nga ${total}$`))).toBeVisible()
  const firstRow = rows.first()
  const code = (await firstRow.textContent())?.match(/\d{4}-\d+\/\d+/)?.[0]
  expect(code).toBeTruthy()

  // into a case, through two of its tabs, and back to the list
  await firstRow.click()
  await expect(page.getByRole('heading', { level: 1 })).toContainText(code ?? '')
  await page.getByRole('tab', { name: /^Palët/ }).click()
  await page.getByRole('button', { name: 'Kthehu te lista e çështjeve' }).click()

  await expect(page).toHaveURL(/\/cases\?/)
  await expect(page).toHaveURL(/[?&]page=2(&|$)/)
  await expect(page).toHaveURL(/[?&]q=E2E(&|$)/)
  await expect(page.getByText(new RegExp(`^11–\\d+ nga ${total}$`))).toBeVisible()
  await expect(rows.first()).toContainText(code ?? '')

  await page.getByRole('button', { name: '‹ Para' }).click()
  await expect(page.getByText(`1–10 nga ${total}`)).toBeVisible()
  await expect(page.getByRole('button', { name: '‹ Para' })).toBeDisabled()
})
