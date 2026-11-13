import { expect, type Page } from '@playwright/test'

/** Logs in through the dev login page (/login), as a user would: type the e-mail, press "Hyr". */
export async function login(page: Page, email: string) {
  await page.goto('/login')
  // already logged in (a second user in the same test): /login sends a known user away, so forget the dev user first
  // (the key of authConfig.ts) and open the login page again
  await page.evaluate(() => sessionStorage.removeItem('ash.devUser'))
  await page.goto('/login')
  // the input itself (the open suggestion list carries the same label)
  const user = page.getByRole('combobox', { name: 'Përdoruesi' })
  await user.fill(email)
  await user.press('Escape') // close the suggestion list, which covers the button
  await page.getByRole('button', { name: 'Hyr' }).click()
  await expect(page).not.toHaveURL(/\/login/)
}
