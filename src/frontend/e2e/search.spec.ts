import { expect, test } from '@playwright/test'
import { registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the open typeahead (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/**
 * Task 047: the header search. Ctrl+K focuses it; part of a code finds the case and Enter (on the chosen row) opens
 * it; a party's name shows the snippet "Pala: …" with the name.
 */
test('Ctrl+K, part of a code, Enter opens the case; a party name shows its snippet', async ({ page }) => {
  const created = await registerCase()
  const number = /-(\d+)\//.exec(created.code)?.[1] ?? ''
  expect(number.length).toBeGreaterThanOrEqual(4)
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  await page.keyboard.press('Control+k')
  const box = page.getByRole('combobox', { name: 'Kërko çështje' })
  await expect(box).toBeFocused()

  // a part of the code (the end of its number)
  await box.fill(number.slice(-4))
  const results = page.getByRole('listbox', { name: 'Rezultatet e kërkimit' })
  const option = results.getByRole('option', { name: new RegExp(created.code.replace('/', '\\/')) })
  await expect(option).toBeVisible()
  await expect(results.getByRole('option', { name: /Shiko të gjitha rezultatet/ })).toBeVisible()
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/search-1536.png` })

  // choose it with the keyboard, then Enter
  for (let i = 0; i < 20; i++) {
    if ((await option.getAttribute('aria-selected')) === 'true') break
    await box.press('ArrowDown')
  }
  await expect(option).toHaveAttribute('aria-selected', 'true')
  await box.press('Enter')
  await expect(page).toHaveURL(new RegExp(`/cases/${created.id}`))

  // a party's name: its snippet, the name in bold
  const tag = /E2E-(\w+)/.exec(created.actNumber)?.[1] ?? ''
  await page.keyboard.press('Control+k')
  await box.fill(`Paditës ${tag}`)
  const party = results.getByRole('option', { name: new RegExp(created.code.replace('/', '\\/')) })
  await expect(party).toContainText(`Pala: E2E Paditës ${tag}`)
  await expect(party.locator('strong', { hasText: 'Paditës' })).toBeVisible()

  // Enter with nothing chosen: the case list searched over every status
  await box.press('Escape')
  await box.press('ArrowUp')
  await box.press('Escape')
  await box.fill(tag)
  await expect(results).toBeVisible()
  await box.press('Enter')
  await expect(page).toHaveURL(/\/cases\?(tab=\w+&)?q=.+&status=ALL/)
})

test('on a phone the search icon opens a full-width search sheet', async ({ browser }) => {
  const created = await registerCase()
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } })
  const page = await context.newPage()
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  await page.getByRole('button', { name: 'Hap kërkimin' }).click()
  const box = page.getByRole('dialog').getByRole('combobox', { name: 'Kërko çështje' })
  await expect(box).toBeFocused()
  await box.fill(created.code.slice(-8, -2))
  await expect(page.getByRole('option', { name: new RegExp(created.code.replace('/', '\\/')) })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/search-390.png` })
  await context.close()
})
