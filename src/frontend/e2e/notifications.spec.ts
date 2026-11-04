import { expect, test, type Page } from '@playwright/test'
import { markAllNotificationsRead, registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the bell popover and the page (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

async function shot(page: Page, name: string) {
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/${name}.png`, fullPage: true })
}

/**
 * Task 040 acceptance: the Kryetar assigns a lawyer in one browser context; in another, already open, the lawyer's
 * bell count goes up within 2 seconds, without a reload (SignalR push after the commit). Then the lawyer opens it
 * from the bell (marked read, the case page opens) and finds it on the "Njoftimet" page.
 */
test('the lawyer sees a new assignment on the bell within 2 seconds, without reloading', async ({ browser }) => {
  const created = await registerCase()
  await markAllNotificationsRead(USERS.avokat1)

  const lawyerContext = await browser.newContext()
  const kryetarContext = await browser.newContext()
  try {
    const lawyer = await lawyerContext.newPage()
    await login(lawyer, USERS.avokat1)
    await lawyer.goto('/dashboard')
    const bell = lawyer.getByRole('button', { name: /^Njoftimet/ })
    await expect(bell).toHaveAccessibleName('Njoftimet')
    await expect(lawyer).toHaveTitle('ASH')
    // the hub is connected before the action: the page is idle and nothing reloads it from now on
    await lawyer.waitForLoadState('networkidle')
    let reloaded = false
    lawyer.on('framenavigated', (frame) => {
      if (frame === lawyer.mainFrame()) reloaded = true
    })

    const kryetar = await kryetarContext.newPage()
    await login(kryetar, USERS.kryetar)
    await kryetar.goto(`/cases?tab=unassigned&q=${encodeURIComponent(created.actNumber)}`)
    await kryetar.getByRole('button', { name: `Cakto avokatin për ${created.code}` }).click()
    const dialog = kryetar.getByRole('dialog')
    await dialog.getByRole('combobox', { name: /Avokati primar/ }).click()
    await kryetar.getByRole('option', { name: /Avokat1 Dev/ }).click()
    await dialog.getByRole('button', { name: 'Cakto avokatin' }).click()
    await expect(kryetar.getByText('Çështja iu caktua Avokat1 Dev')).toBeVisible()

    // the acceptance criterion: within 2 s of the response, in the other context
    await expect(lawyer.getByRole('button', { name: 'Njoftimet, 1 të palexuara' })).toBeVisible({ timeout: 2_000 })
    expect(reloaded).toBe(false)
    await expect(lawyer).toHaveTitle('(1) ASH')
    // the live toast (not shown for what was already there on load)
    await expect(lawyer.getByRole('button', { name: 'Hap' })).toBeVisible()
    await expect(lawyer.getByText('Ju u caktua një çështje').first()).toBeVisible()

    // the popover: the newest first, unread in bold
    await lawyer.getByRole('button', { name: 'Njoftimet, 1 të palexuara' }).click()
    const popover = lawyer.getByRole('dialog', { name: 'Njoftimet e fundit' })
    const item = popover.getByRole('button', { name: /Ju u caktua një çështje \(E palexuar\)/ }).first()
    await expect(item).toBeVisible()
    await expect(item.getByText(created.code, { exact: false })).toBeVisible()
    await expect(popover).toHaveCSS('opacity', '1') // the fade-in is over
    await shot(lawyer, 'bell-popover-1536')

    // a click: read, and the case page opens
    await item.click()
    await expect(lawyer).toHaveURL(new RegExp(`/cases/${created.id}/te-dhenat$`))
    await expect(lawyer.getByRole('button', { name: 'Njoftimet', exact: true })).toBeVisible()
    await expect(lawyer).toHaveTitle('ASH')

    // the page: the same notification, now read; "Të palexuara" is empty
    await lawyer.goto('/notifications')
    await expect(lawyer.getByRole('heading', { level: 1, name: 'Njoftimet' })).toBeVisible()
    const table = lawyer.getByRole('table', { name: 'Lista e njoftimeve' })
    await expect(table.getByRole('button', { name: 'Ju u caktua një çështje' }).first()).toBeVisible()
    await expect(lawyer.getByText(/^1–\d+ nga \d+$/)).toBeVisible()
    await shot(lawyer, 'notifications-page-1536')
    await lawyer.getByRole('combobox', { name: 'Shfaq' }).click()
    await lawyer.getByRole('option', { name: 'Të palexuara' }).click()
    await expect(lawyer).toHaveURL(/show=unread/)
    await expect(lawyer.getByText('Nuk keni njoftime të palexuara.')).toBeVisible()

    if (SHOTS) {
      await lawyer.setViewportSize({ width: 390, height: 844 })
      await lawyer.goto('/notifications')
      await expect(table.getByRole('button', { name: 'Ju u caktua një çështje' }).first()).toBeVisible()
      await shot(lawyer, 'notifications-page-390')
      expect(await lawyer.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
      await lawyer.getByRole('button', { name: /^Njoftimet/ }).click()
      await expect(lawyer.getByRole('dialog', { name: 'Njoftimet e fundit' })).toHaveCSS('opacity', '1')
      await shot(lawyer, 'bell-popover-390')
    }
  } finally {
    await lawyerContext.close()
    await kryetarContext.close()
  }
})
