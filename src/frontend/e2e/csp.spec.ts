import { expect, test, type Page } from '@playwright/test'
import { assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 051 — no Content-Security-Policy violation on the main screens of the BUILT app (run by e2e/csp.config.ts:
 * `npm run e2e:csp`): login, dashboard, the case page with a PDF preview, chat, agenda, a report export. Every
 * violation the browser reports (securitypolicyviolation events and "Refused to …" console errors) fails the test.
 */
const pdf = (text: string) =>
  Buffer.from(
    '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
      '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>endobj\n' +
      '4 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n' +
      `5 0 obj<</Length ${text.length + 40}>>stream\nBT /F1 22 Tf 72 760 Td (${text}) Tj ET\nendstream endobj\n` +
      'trailer<</Root 1 0 R>>\n%%EOF\n',
  )

function watchViolations(page: Page): string[] {
  const violations: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy|Refused to/i.test(message.text())) violations.push(message.text())
  })
  void page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      console.error(`Refused to load (CSP ${e.violatedDirective}): ${e.blockedURI} at ${e.sourceFile}:${e.lineNumber}:${e.columnNumber}`)
    })
  })
  return violations
}

test('the built app has a CSP and no screen violates it', async ({ page }) => {
  const violations = watchViolations(page)
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')

  // the header is the production one
  const loginPage = await page.goto('/login')
  expect(loginPage?.headers()['content-security-policy']).toContain("script-src 'self'")
  expect(loginPage?.headers()['content-security-policy']).toContain("frame-ancestors 'none'")

  await login(page, USERS.avokat1)
  await page.goto('/dashboard')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  // the case page: upload a PDF and preview it (an <iframe> of a blob:)
  await page.goto(`/cases/${created.id}/dokumentet`)
  // the upload area is on the tab itself ("Ngarko dokumente")
  await expect(page.getByRole('heading', { name: 'Ngarko dokumente' })).toBeVisible()
  await page.locator('input[type=file]').setInputFiles({ name: 'csp-e2e.pdf', mimeType: 'application/pdf', buffer: pdf('CSP E2E') })
  await page.getByRole('combobox', { name: 'Lloji i dokumentit' }).click()
  await page.getByRole('option').first().click()
  await page.getByRole('button', { name: /^Ngarko/ }).click()
  const view = page.getByRole('button', { name: /^Shiko csp-e2e/ })
  await expect(view).toBeEnabled({ timeout: 60_000 })
  await view.click()
  await expect(page.locator('iframe[src^="blob:"]')).toBeVisible()
  await page.keyboard.press('Escape')

  await page.goto('/chat')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.goto('/agenda')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  // a report export (a blob download)
  // task 053o: a filter change shows the table (no "Gjenero")
  await page.goto('/reports')
  await page.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Kodi unik' }).click()
  await page.getByRole('textbox', { name: 'Kodi unik' }).fill('2026')
  await expect(page.getByRole('button', { name: 'Eksporto' })).toBeEnabled({ timeout: 15_000 })
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Eksporto' }).click()
  await page.getByRole('menuitem', { name: 'CSV (.csv)' }).click()
  await download

  expect(violations).toEqual([])
})
