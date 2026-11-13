import { expect, test } from '@playwright/test'
import { USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the profile page (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/**
 * Tasks 048 / 053g: the profile is read-only — name, e-mail, position (Organika), role(s), office. Client decision E:
 * no phone and no notification switches.
 */
test('the profile shows the data read-only, with no phone and no notification switches', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')
  await page.getByRole('button', { name: 'Menyja e përdoruesit' }).click()
  await page.getByRole('menuitem', { name: 'Profili' }).click()
  await expect(page).toHaveURL(/\/profile$/)
  await expect(page.getByRole('heading', { name: 'Profili', level: 1 })).toBeVisible()
  await expect(page.getByText('Emri dhe email-i menaxhohen nga sistemi i identifikimit')).toBeVisible()
  await expect(page.getByText('Pozicioni, njësia dhe eprori vendosen nga administratori përmes Organikës.')).toBeVisible()
  await expect(page.getByText('kryetar@ash.local')).toBeVisible()

  // gone (client decision E)
  await expect(page.getByRole('button', { name: 'Ndrysho telefonin' })).toHaveCount(0)
  await expect(page.getByRole('switch')).toHaveCount(0)
  if (SHOTS) {
    await page.screenshot({ path: `${SHOTS}/profile-1536.png`, fullPage: true })
    await page.setViewportSize({ width: 390, height: 844 })
    await page.screenshot({ path: `${SHOTS}/profile-390.png`, fullPage: true })
  }
})

/** Task 053g: Administrimi → Organika opens with the template, the upload and the list of imports. */
test('the administrator opens Organika', async ({ page }) => {
  await login(page, USERS.admin)
  await page.goto('/admin/organika')
  await expect(page.getByRole('heading', { name: 'Organika', level: 1 })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Shkarko shabllonin' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Ngarko skedarin' })).toBeVisible()

  // a sheet with an unknown e-mail: the preview says so, and there is nothing to apply
  await page.locator('input[type=file]').setInputFiles({
    name: 'organika-e2e.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('email;pozicioni;njesia;eprori_email\nnuk.ekziston.e2e@ash.local;Avokat;Sektori A;\n'),
  })
  await expect(page.getByRole('heading', { name: 'Pamja paraprake — organika-e2e.csv' })).toBeVisible()
  await expect(page.getByText('E-mail i panjohur: 1')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Apliko' })).toBeDisabled()
})
