import { expect, test, type Browser, type Page } from '@playwright/test'
import { assignLawyers, claimOf, registerCase, USERS } from './support/api'
import { login } from './support/login'

async function openCase(browser: Browser, user: string, caseId: string): Promise<Page> {
  const context = await browser.newContext()
  const page = await context.newPage()
  await login(page, user)
  await page.goto(`/cases/${caseId}`)
  await expect(page.getByRole('heading', { name: 'Vlera e padisë' })).toBeVisible()
  return page
}

/**
 * Task 029 acceptance: a concurrent edit shows the reload message. avokat1 (primary) and avokat2 (secondary) both
 * have the case open and edit the claim; avokat1 saves first, avokat2's save gets the ConflictAlert; "Rifresko"
 * reloads the case and avokat2's typed value stays in the field.
 */
test('the second of two concurrent claim edits gets the conflict message and keeps the typed value', async ({ browser }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')

  const first = await openCase(browser, USERS.avokat1, created.id)
  const second = await openCase(browser, USERS.avokat2, created.id)

  // both start editing the same version of the case
  for (const page of [first, second]) {
    await page.getByRole('button', { name: 'Ndrysho: Vlera e padisë' }).click()
  }
  await first.getByLabel('Shuma').fill('1000')
  await second.getByLabel('Shuma').fill('2000')

  // avokat1 saves first
  await first.getByRole('button', { name: 'Ruaj' }).first().click()
  await expect(first.getByText('Ndryshimi u ruajt.')).toBeVisible()
  expect(await claimOf(created.id, USERS.avokat1)).toBe('1000.00')

  // avokat2 saves with the version it read: the shared ConflictAlert
  await second.getByRole('button', { name: 'Ruaj' }).first().click()
  const conflict = second.getByRole('alert').filter({ hasText: 'Të dhënat u ndryshuan nga një përdorues tjetër.' })
  await expect(conflict).toBeVisible()
  await expect(second.getByLabel('Shuma')).toHaveValue('2.000')

  // "Rifresko" reloads the case; the editor stays open with what avokat2 typed
  await conflict.getByRole('button', { name: 'Rifresko' }).click()
  await expect(conflict).toBeHidden()
  await expect(second.getByLabel('Shuma')).toHaveValue('2.000')
  expect(await claimOf(created.id, USERS.avokat2)).toBe('1000.00') // nothing of avokat2's was saved

  // saving again now goes on top of the fresh case
  await second.getByRole('button', { name: 'Ruaj' }).first().click()
  await expect(second.getByText('Ndryshimi u ruajt.')).toBeVisible()
  expect(await claimOf(created.id, USERS.avokat2)).toBe('2000.00')

  await first.context().close()
  await second.context().close()
})
