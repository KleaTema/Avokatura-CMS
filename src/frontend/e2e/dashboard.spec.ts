import { expect, test } from '@playwright/test'
import { registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the dashboard screenshots per role (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/**
 * Task 046 acceptance: the Kryetar's "Pa avokat" card and the list it opens show the same number — one source of
 * truth (the card is counted with the list's own query).
 */
test('the "Pa avokat" card opens a list with exactly as many cases as it shows', async ({ page }) => {
  await registerCase() // at least one case waits for the Kryetar
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  // the number is grouped the Albanian way (a dot or a narrow space between thousands)
  const card = page.getByRole('link', { name: /^Pa avokat: [\d.\s  ]+\. Hap listën$/ })
  await expect(card).toBeVisible()
  const label = (await card.getAttribute('aria-label')) ?? ''
  const shown = Number(/: ([\d.\s  ]+)\./.exec(label)?.[1]?.replace(/\D/g, ''))
  expect(shown).toBeGreaterThan(0)

  await card.click()
  await expect(page).toHaveURL(/\/cases\?tab=unassigned$/)
  // the shared footer: "1–10 nga N"
  const range = page.getByText(/^1–\d+ nga \d+$/)
  await expect(range).toBeVisible()
  const total = Number(/nga (\d+)$/.exec((await range.textContent()) ?? '')?.[1])
  expect(total).toBe(shown)
})

test('each role sees its own dashboard (screenshots with E2E_SHOTS)', async ({ browser }) => {
  test.setTimeout(180_000)
  for (const [name, email] of [
    ['protokoll', USERS.protokoll],
    ['kryetar', USERS.kryetar],
    ['avokat1', USERS.avokat1],
    ['institucion', USERS.institucion],
    ['monitor', USERS.monitor],
  ] as const) {
    // a fresh browser per role: the dev login lives in the tab's session
    const context = await browser.newContext({ viewport: { width: 1536, height: 1024 } })
    const page = await context.newPage()
    await login(page, email)
    await page.goto('/dashboard')
    await expect(page.getByRole('heading', { level: 1, name: /^Mirëdita/ })).toBeVisible()
    // loaded: no skeletons left, no failed widget
    await expect(page.locator('.MuiSkeleton-root')).toHaveCount(0)
    await expect(page.getByText('Ky element nuk u ngarkua.')).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Regjistro/ })).toHaveCount(0)
    if (SHOTS) await page.screenshot({ path: `${SHOTS}/dashboard-${name}-1536.png`, fullPage: true })
    if (name === 'kryetar') {
      // a phone: one card per row, nothing wider than the screen (the chart and the table included)
      await page.setViewportSize({ width: 390, height: 844 })
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
      if (SHOTS) await page.screenshot({ path: `${SHOTS}/dashboard-kryetar-390.png`, fullPage: true })
    }
    await context.close()
  }
})
