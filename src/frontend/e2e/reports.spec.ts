import { readFileSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'
import { assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of both tabs and the print preview (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/** Today in Tirane as yyyy-MM-dd (the export's file name carries it). */
const tiraneToday = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Tirane', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())

/** Adds "Kodi unik" with "+ Shto filtër" and types into it (a filter change: the table appears). */
async function filterByCode(page: Page, text: string) {
  await page.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Kodi unik' }).click()
  await page.getByRole('textbox', { name: 'Kodi unik' }).fill(text)
}

/**
 * Task 053o: "Raporte & Statistika" — two main tabs with their sub-tabs (the new numbering); on entering, every
 * filter at "Të gjitha" and NO table; the first filter change shows it and every further one updates it (debounced,
 * the older request aborted — never one request per key); no "Gjenero"; the export in the chosen format.
 */
test('Tabela 1: tabs, no table before a filter, real-time without a request storm, export', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  await page.getByRole('navigation', { name: /./ }).getByRole('link', { name: 'Raporte & Statistika' }).click()
  await expect(page.getByRole('heading', { name: 'Raporte dhe statistika', level: 1 })).toBeVisible()
  const main = page.getByRole('tablist', { name: 'Raporte & Statistika' })
  await expect(main.getByRole('tab', { name: 'Raportet' })).toHaveAttribute('aria-selected', 'true')
  await expect(main.getByRole('tab', { name: 'Statistikat' })).toBeVisible()
  const sub = page.getByRole('tablist', { name: 'Tabelat' })
  for (const name of ['Tabela 1 — Aktive', 'Tabela 2 — Të mbyllura', 'Raportet e mia']) {
    await expect(sub.getByRole('tab', { name })).toBeVisible()
  }

  await expect(page.getByText('Zgjidhni të paktën një filtër për të shfaqur të dhënat')).toBeVisible()
  await expect(page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Gjenero' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Eksporto' })).toBeDisabled()

  // count the row requests while typing fast: the debounce sends at most one per pause, not one per key
  const requests: string[] = []
  page.on('request', (r) => {
    if (r.url().includes('/api/reports/builder/active')) requests.push(r.url())
  })
  await filterByCode(page, '')
  await page.getByRole('textbox', { name: 'Kodi unik' }).pressSequentially('2026-00', { delay: 30 })
  const table = page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' })
  await expect(table).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/[?&]f=/)
  await page.waitForTimeout(1000)
  expect(requests.length).toBeGreaterThan(0)
  expect(requests.length).toBeLessThanOrEqual(2)

  // the client's columns, the grouped "Vlera e padisë" over M2 | Monedha
  await expect(table.getByRole('columnheader', { name: 'Vlera e padisë' })).toBeVisible()
  await expect(table.getByRole('columnheader', { name: 'Kategoria dhe lloji i çështjes' })).toBeVisible()

  // clear the code: the table follows (every case of the scope)
  await page.getByRole('textbox', { name: 'Kodi unik' }).fill('')
  await expect(table.getByText(/^Gjithsej: [\d.]+ rreshta$/)).toBeVisible({ timeout: 15_000 })
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/raportet-tabela-1-1536.png` })

  for (const [format, label] of [
    ['xlsx', 'Excel (.xlsx)'],
    ['csv', 'CSV (.csv)'],
  ] as const) {
    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Eksporto' }).click()
    await page.getByRole('menuitem', { name: label }).click()
    const file = await download
    expect(file.suggestedFilename()).toBe(`raporti-ceshtjet-aktive-${tiraneToday()}.${format}`)
    const bytes = readFileSync(await file.path())
    if (format === 'xlsx') expect(bytes.subarray(0, 2).toString('latin1')).toBe('PK')
    else expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf])
  }
})

/** Task 053o: the page size changes the request and goes back to page 1; Para / Pas move the cursor; the range follows. */
test('Tabela 1: rows per page and Para / Pas page through the rows', async ({ page }) => {
  // enough cases for three pages of 10
  for (let i = 0; i < 3; i++) await registerCase()
  await login(page, USERS.kryetar)
  await page.goto('/reports?tab=aktive')
  await filterByCode(page, '2026')
  const footer = page.getByRole('navigation', { name: 'Faqet' }).or(page.locator('nav').filter({ hasText: 'Rreshta për faqe' }))
  const range = footer.getByText(/^\d+–\d+ nga \d+$/)
  await expect(range).toHaveText(/^1–10 nga \d+$/, { timeout: 15_000 })
  // textContent: the range is briefly hidden (not removed) while a page loads
  const total = Number(((await range.textContent()) ?? '').split(' nga ')[1])
  test.skip(total <= 20, 'needs more than 20 active cases')

  await footer.getByRole('button', { name: /Pas/ }).click()
  await expect(range).toHaveText(new RegExp(`^11–20 nga ${total}$`), { timeout: 15_000 })
  await footer.getByRole('button', { name: /Para/ }).click()
  await expect(range).toHaveText(new RegExp(`^1–10 nga ${total}$`), { timeout: 15_000 })

  await footer.getByRole('button', { name: /Pas/ }).click()
  await expect(range).toHaveText(/^11–20/, { timeout: 15_000 })
  // a new size: the first page again, with the new number of rows
  await footer.getByRole('combobox', { name: 'Rreshta për faqe' }).click()
  await page.getByRole('option', { name: '25' }).click()
  await expect(range).toHaveText(new RegExp(`^1–${Math.min(25, total)} nga ${total}$`), { timeout: 15_000 })
  await expect(page).toHaveURL(/size=25/)
  const rows = page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' }).locator('tbody tr')
  await expect(rows).toHaveCount(Math.min(25, total) + 1, { timeout: 15_000 }) // + the totals row
})

/** Task 053o: "Printo" prints ONLY the table, with its header (title, filters, at / by) — the app itself is hidden. */
test('Tabela 1: Printo puts only the table and its header on paper', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/reports?tab=aktive')
  await filterByCode(page, '2026')
  await expect(page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' })).toBeVisible({ timeout: 15_000 })

  await page.evaluate(() => {
    ;(window as unknown as { printed: number }).printed = 0
    window.print = () => {
      ;(window as unknown as { printed: number }).printed += 1
    }
  })
  await page.getByRole('button', { name: 'Printo' }).click()
  await expect.poll(() => page.evaluate(() => (window as unknown as { printed: number }).printed), { timeout: 10_000 }).toBe(1)

  await page.emulateMedia({ media: 'print' })
  const printout = page.locator('#report-print')
  await expect(printout).toBeVisible()
  await expect(page.locator('#root')).toBeHidden()
  await expect(printout).toContainText('Tabela 1 — Raporti i çështjeve aktive')
  await expect(printout).toContainText('Kodi unik: 2026')
  await expect(printout).toContainText(/Gjeneruar më .+ nga Kryetar/)
  await expect(printout.getByRole('table')).toBeVisible()
  await expect(printout.getByRole('button')).toHaveCount(0) // no buttons, no menu, no filters
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/raportet-print-preview.png`, fullPage: true })
  await page.emulateMedia({ media: 'screen' })
})

/** Statistikat: the client's table; a number opens a drawer with the same count of cases; print asks for the sections. */
test('Tabela 3: the client table, a number opens its cases, the print dialog', async ({ page }) => {
  // a case registered (and assigned) today: this year's "Ardhur gjatë vitit" is at least 1
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await login(page, USERS.kryetar)
  await page.goto('/reports?tab=statistika')
  await expect(page.getByRole('tab', { name: 'Statistikat' })).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByRole('tab', { name: 'Tabela 3 — Çështje aktive' })).toHaveAttribute('aria-selected', 'true')
  await expect(page.getByRole('tab', { name: 'Tabela 4 — Institucionale' })).toBeVisible()
  await expect(page.getByText('Zgjidhni të paktën një filtër për të shfaqur të dhënat')).toBeVisible()

  // a filter change: an office, then "Të gjitha" again (the table shows every case of the scope)
  await page.getByRole('combobox', { name: 'Zyra' }).click()
  await page.getByRole('option').filter({ hasNotText: 'Të gjitha' }).first().click()
  await page.getByRole('combobox', { name: 'Zyra' }).click()
  await page.getByRole('option', { name: 'Të gjitha' }).click()

  const table = page.getByRole('table', { name: /^Tabela 3 — Çështje aktive — \d{4}$/ })
  await expect(table).toBeVisible({ timeout: 15_000 })
  await expect(table.getByRole('columnheader', { name: /Gjithsej çështje ardhur në Avokaturën e Shtetit/ })).toBeVisible()
  await expect(table.getByRole('columnheader', { name: /Rezultati i çështjeve të përfunduara/ })).toBeVisible()
  const number = table.getByRole('button', { name: /Ardhur GJATË VITIT, TOTALI: shiko çështjet/ })
  const shown = Number((await number.innerText()).replace(/\./g, ''))
  expect(shown).toBeGreaterThan(0)
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/statistikat-tabela-3-1536.png` })

  await number.click()
  const drawer = page.getByRole('presentation').filter({ has: page.getByRole('heading', { name: 'Ardhur GJATË VITIT — TOTALI' }) })
  await expect(drawer.getByText(shown === 1 ? '1 çështje' : `${shown} çështje`, { exact: true })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(drawer).toHaveCount(0)

  // Printo: the table always, "Rezultati" / "Në dorë" only when chosen
  await page.evaluate(() => {
    window.print = () => undefined
  })
  await page.getByRole('button', { name: 'Printo' }).click()
  const dialog = page.getByRole('dialog', { name: 'Printo statistikat' })
  await dialog.getByLabel('Rezultati (sipas çdo rezultati të vendimit)').check()
  await dialog.getByRole('button', { name: 'Printo' }).click()
  await page.emulateMedia({ media: 'print' })
  const printout = page.locator('#report-print')
  await expect(printout).toBeVisible()
  await expect(printout).toContainText('Rezultati i çështjeve të përfunduara')
  await expect(printout).not.toContainText('kërkon një institucion')
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/statistikat-print-preview.png`, fullPage: true })
  await page.emulateMedia({ media: 'screen' })
})

/** "+ Shto filtër", "Ruaj raportin", "Raportet e mia" → "Hap" shows it at once, "Fshi" removes it. */
test('a filtered report is saved, rerun from "Raportet e mia" and deleted', async ({ page }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  const name = `E2E raport ${Date.now()}`
  await login(page, USERS.kryetar)
  await page.goto('/reports')

  await filterByCode(page, created.code)
  const table = page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' })
  await expect(table.getByText('Gjithsej: 1 rreshta')).toBeVisible({ timeout: 15_000 })

  await page.getByRole('button', { name: 'Ruaj raportin' }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: 'Emri i raportit' }).fill(name)
  await page.getByRole('dialog').getByRole('button', { name: 'Ruaj raportin' }).click()
  await expect(page.getByText(`Raporti „${name}” u ruajt.`)).toBeVisible()

  await page.getByRole('tab', { name: 'Raportet e mia' }).click()
  const row = page.getByRole('row').filter({ hasText: name })
  await row.getByRole('button', { name: 'Hap' }).click()
  await expect(page.getByRole('table', { name: 'Tabela 1 — Raporti i çështjeve aktive' }).getByText(created.code)).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('textbox', { name: 'Kodi unik' })).toHaveValue(created.code)

  await page.getByRole('tab', { name: 'Raportet e mia' }).click()
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: 'Fshi' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Fshi' }).click()
  await expect(page.getByText(`Raporti „${name}” u fshi.`)).toBeVisible()
})
