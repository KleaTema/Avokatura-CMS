import { expect, test } from '@playwright/test'
import { unlinkAssistant, USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053e: the Kryetar's "Ndihmësit ligjorë" — link the assistant to a lawyer (the chip shows how many cases it
 * brings), then remove the link again (the local data stays as it was).
 */
test('the Kryetar links an assistant to a lawyer and removes the link', async ({ page }) => {
  await unlinkAssistant(USERS.ndihmes, USERS.avokat2) // a link left by an interrupted run
  try {
    await login(page, USERS.kryetar)
    await page.getByRole('link', { name: 'Ndihmësit ligjorë' }).click()
    await expect(page.getByRole('heading', { name: 'Ndihmësit ligjorë', level: 1 })).toBeVisible()

    const card = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Ndihmës Dev', exact: true }) })
    await card.getByRole('button', { name: 'Lidh Ndihmës Dev me një avokat' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: /Avokati/ }).click()
    await page.getByRole('option', { name: 'Avokat2 Dev' }).click()
    await dialog.getByRole('button', { name: 'Lidh', exact: true }).click()
    await expect(page.getByText(/^U lidh me Avokat2 Dev: \d+ çështje\.$/)).toBeVisible()
    const chip = card.getByRole('button', { name: /^Avokat2 Dev · / })
    await expect(chip).toBeVisible()

    // remove: the chip's delete, then the confirmation
    await chip.press('Delete')
    await page.getByRole('dialog').getByRole('button', { name: /^Hiq lidhjen e / }).click()
    await expect(page.getByText('Lidhja u hoq.')).toBeVisible()
    await expect(card.getByRole('button', { name: /^Avokat2 Dev · / })).toHaveCount(0)

    // the primary lawyer no longer has "Shto ndihmës"
    await login(page, USERS.avokat1)
    await expect(page.getByRole('link', { name: 'Ndihmësit ligjorë' })).toHaveCount(0)
  } finally {
    await unlinkAssistant(USERS.ndihmes, USERS.avokat2)
  }
})
