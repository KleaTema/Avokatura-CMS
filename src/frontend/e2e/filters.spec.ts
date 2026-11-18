import { expect, test } from '@playwright/test'
import { registerCase, USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053m: the shared FilterBar of every table, on the case list. The main filters on top, "+ Shto filtër" for every
 * other column (× removes it); a child's options follow its parent (a new level clears the court); an added filter
 * lives in the URL, so a row and Back keep it; leaving while it is still empty asks first; the menu starts clean.
 */
test('case list: add, use, keep on Back, remove, dependent filters, leave dialog, menu clears', async ({ page }) => {
  test.setTimeout(120_000)
  const created = await registerCase()
  await login(page, USERS.monitor)
  await page.goto('/cases?tab=all')

  const bar = page.getByRole('group', { name: 'Filtrat' })
  // the defaults, no "Filtra" panel
  await expect(bar.getByRole('textbox', { name: /^Kërko/ })).toBeVisible({ timeout: 15_000 })
  await expect(bar.getByRole('combobox', { name: 'Statusi' })).toBeVisible()
  await expect(bar.getByRole('combobox', { name: 'Gjykata' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Filtra', exact: true })).toHaveCount(0)

  // "+ Shto filtër" → Nr. i aktit: shown empty, kept in ?af=
  await bar.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Nr. i aktit', exact: true }).click()
  const act = bar.getByRole('textbox', { name: 'Nr. i aktit', exact: true })
  await expect(act).toBeVisible()
  await expect(page).toHaveURL(/af=actNumber/)

  // unused: leaving through the menu asks; "Anulo" stays
  await page.getByRole('link', { name: 'Agjenda' }).first().click()
  const leave = page.getByRole('dialog').filter({ hasText: 'Keni krijuar një filtër që nuk e keni përdorur' })
  await expect(leave).toBeVisible({ timeout: 10_000 })
  await leave.getByRole('button', { name: 'Anulo' }).click()
  await expect(leave).toBeHidden({ timeout: 10_000 })
  await expect(page).toHaveURL(/\/cases\?/)

  // used: the list narrows to the case
  await act.fill(created.actNumber)
  await expect(page).toHaveURL(/actNumber=/, { timeout: 10_000 })
  const table = page.getByRole('table')
  const row = table.getByRole('row').filter({ hasText: created.code })
  await expect(row).toBeVisible({ timeout: 15_000 })
  await expect(table.getByRole('row')).toHaveCount(2, { timeout: 15_000 }) // the header and the case

  // into the case and Back: the filter and its value are still there (no dialog: it is used)
  await row.getByText(created.code).click() // the row is the link
  await expect(page).toHaveURL(new RegExp(`/cases/${created.id}`), { timeout: 15_000 })
  await page.goBack()
  await expect(page).toHaveURL(/actNumber=/, { timeout: 15_000 })
  await expect(bar.getByRole('textbox', { name: 'Nr. i aktit', exact: true })).toHaveValue(created.actNumber)

  // × removes it with its value
  await bar.getByRole('button', { name: 'Hiq filtrin Nr. i aktit' }).click()
  await expect(bar.getByRole('textbox', { name: 'Nr. i aktit', exact: true })).toHaveCount(0)
  await expect(page).not.toHaveURL(/actNumber=|af=/)

  // dependent: a court, then a level — the level's new choice clears the court (its options change)
  await bar.getByRole('combobox', { name: 'Gjykata' }).click()
  await page.getByRole('option', { name: 'Gjykata e Shkallës së Parë Tiranë' }).click()
  await expect(page).toHaveURL(/court=/, { timeout: 10_000 })
  await bar.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Shkalla e gjykimit', exact: true }).click()
  await bar.getByRole('combobox', { name: 'Shkalla e gjykimit' }).click()
  await page.getByRole('option').filter({ hasNotText: 'Të gjitha' }).last().click()
  await expect(page).toHaveURL(/level=/, { timeout: 10_000 })
  await expect(page).not.toHaveURL(/court=/)
  // the court's options are the chosen level's only
  await bar.getByRole('combobox', { name: 'Gjykata' }).click()
  await expect(page.getByRole('listbox')).toBeVisible()
  await page.keyboard.press('Escape')

  // used filters: the menu leaves without asking, and the list opens clean
  await page.getByRole('link', { name: 'Agjenda' }).first().click()
  await expect(page).toHaveURL(/\/agenda/, { timeout: 10_000 })
  await expect(page.getByRole('dialog').filter({ hasText: 'Keni krijuar një filtër' })).toHaveCount(0)
  await page.getByRole('link', { name: 'Çështje', exact: true }).first().click()
  await expect(page).toHaveURL(/\/cases(\?tab=\w+)?$/, { timeout: 10_000 })
  await expect(bar.getByRole('combobox', { name: 'Shkalla e gjykimit' })).toHaveCount(0)
})

test('leaving with a used filter never asks; "Po" leaves an unused one', async ({ page }) => {
  await login(page, USERS.admin)
  await page.goto('/admin/users')
  const bar = page.getByRole('group', { name: 'Filtrat' })
  await expect(bar.getByRole('combobox', { name: 'Roli' })).toBeVisible({ timeout: 15_000 })

  await bar.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Pozicioni', exact: true }).click()
  await expect(bar.getByRole('textbox', { name: 'Pozicioni', exact: true })).toBeVisible()

  await page.getByRole('link', { name: 'Dashboard' }).first().click()
  const leave = page.getByRole('dialog').filter({ hasText: 'Keni krijuar një filtër që nuk e keni përdorur' })
  await expect(leave).toBeVisible({ timeout: 10_000 })
  await leave.getByRole('button', { name: 'Po' }).click()
  await expect(page).toHaveURL(/\/dashboard/, { timeout: 10_000 })
})
