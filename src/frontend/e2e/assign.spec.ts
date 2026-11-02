import { expect, test } from '@playwright/test'
import { registerCase, statusOf, USERS, type CreatedCase } from './support/api'
import { login } from './support/login'

/**
 * Task 029 acceptance: the Kryetar assigns from "Pa avokat" in two steps — the row's button, then the dialog —
 * i.e. exactly: click "Cakto avokatin" → pick the lawyer (open the list, choose) → confirm. Nothing else.
 */
test('Kryetar assigns a lawyer from "Pa avokat" and the case leaves the tab', async ({ page }) => {
  const created: CreatedCase = await registerCase()
  await login(page, USERS.kryetar)

  // the tab, narrowed to the new case by its act number (the search is part of the list's URL)
  await page.goto(`/cases?tab=unassigned&q=${encodeURIComponent(created.actNumber)}`)
  const rowButton = page.getByRole('button', { name: `Cakto avokatin për ${created.code}` })
  await expect(rowButton).toBeVisible()

  const interactions: string[] = []
  const act = async (name: string, action: () => Promise<void>) => {
    interactions.push(name)
    await action()
  }

  await act('click "Cakto avokatin"', () => rowButton.click())
  const dialog = page.getByRole('dialog')
  const primary = dialog.getByRole('combobox', { name: /Avokati primar/ })
  await expect(primary).toBeFocused() // the first field has the focus
  await act('open the lawyer list', () => primary.click())
  await act('choose the lawyer', () => page.getByRole('option', { name: /Avokat1 Dev/ }).click())
  await act('confirm', () => dialog.getByRole('button', { name: 'Cakto avokatin' }).click())

  await expect(page.getByText('Çështja iu caktua Avokat1 Dev')).toBeVisible()
  await expect(dialog).toBeHidden()
  expect(interactions).toEqual(['click "Cakto avokatin"', 'open the lawyer list', 'choose the lawyer', 'confirm'])

  // the case left "Pa avokat" (the list reloaded) and is ACTIVE in the API
  await expect(rowButton).toHaveCount(0)
  await expect(page.getByText(created.code)).toHaveCount(0)
  expect(await statusOf(created.id, USERS.kryetar)).toBe('ACTIVE')
})
