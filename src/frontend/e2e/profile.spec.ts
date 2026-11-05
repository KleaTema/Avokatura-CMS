import { expect, test } from '@playwright/test'
import { USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the profile page (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/**
 * Task 048: the profile. The phone is changed inline (an invalid one is refused with the server's message, a valid
 * one is shown formatted), then cleared again; a mandatory notification is on, disabled.
 */
test('the phone is changed inline, shown formatted, then cleared; mandatory notifications are locked', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')
  await page.getByRole('button', { name: 'Menyja e përdoruesit' }).click()
  await page.getByRole('menuitem', { name: 'Profili' }).click()
  await expect(page).toHaveURL(/\/profile$/)
  await expect(page.getByRole('heading', { name: 'Profili', level: 1 })).toBeVisible()
  await expect(page.getByText('Emri dhe email-i menaxhohen nga sistemi i identifikimit')).toBeVisible()

  const edit = page.getByRole('button', { name: 'Ndrysho telefonin' })
  const field = page.getByRole('textbox', { name: 'Telefoni' })

  await edit.click()
  await field.fill('12345')
  await page.getByRole('button', { name: 'Ruaj' }).click()
  await expect(page.getByText(/Numri i telefonit nuk është i vlefshëm/)).toBeVisible()

  await field.fill('069 123 4567')
  await page.getByRole('button', { name: 'Ruaj' }).click()
  await expect(page.getByText('+355 69 123 4567')).toBeVisible()
  await expect(page.getByText('Telefoni u ruajt.')).toBeVisible()

  // mandatory: on and disabled; optional: can be switched
  await expect(page.getByRole('switch', { name: 'Caktimi si avokat në një çështje' })).toBeDisabled()
  await expect(page.getByRole('switch', { name: 'Caktimi si avokat në një çështje' })).toBeChecked()
  await expect(page.getByRole('switch', { name: 'Seancë nesër' })).toBeEnabled()
  if (SHOTS) {
    await page.screenshot({ path: `${SHOTS}/profile-1536.png`, fullPage: true })
    await page.setViewportSize({ width: 390, height: 844 })
    await page.screenshot({ path: `${SHOTS}/profile-390.png`, fullPage: true })
    await page.setViewportSize({ width: 1536, height: 1024 })
  }

  // leave the seeded user as it was
  await edit.click()
  await field.fill('')
  await page.getByRole('button', { name: 'Ruaj' }).click()
  await expect(page.getByText('Nuk keni vendosur numër telefoni.')).toBeVisible()
})
