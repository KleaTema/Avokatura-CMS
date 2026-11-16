import { readFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the builder and the print preview (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/** Today in Tirane as yyyy-MM-dd (the export's file name carries it). */
const tiraneToday = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Tirane', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())

/**
 * Tasks 045 / 053h: nothing runs before "Gjenero"; then the rows with the totals row, and the export in the chosen
 * format — an .xlsx (a zip: "PK") and a .csv with the UTF-8 BOM.
 */
test('the Kryetar generates Tabela 1 and exports it as Excel and CSV', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  await page.getByRole('navigation', { name: /./ }).getByRole('link', { name: 'Raporte' }).click()
  await expect(page.getByRole('heading', { name: 'Raporte dhe statistika', level: 1 })).toBeVisible()
  await expect(page.getByText('Zgjidhni filtrat dhe shtypni „Gjenero”.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Eksporto' })).toBeDisabled()

  await page.getByRole('button', { name: 'Gjenero' }).click()
  const table = page.getByRole('table', { name: 'Tabela 1 — Aktive' })
  await expect(table).toBeVisible()
  await expect(table.getByText(/^Gjithsej: [\d.]+ rreshta$/)).toBeVisible()
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/report-builder-1536.png` })

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

/** A number of the statistics opens a drawer with the cases behind it — exactly as many as the number. */
test('a statistics number opens a drawer with the same count of cases', async ({ page }) => {
  // a case registered (and assigned) today: this year's "Ardhur gjatë vitit" is at least 1
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await login(page, USERS.kryetar)
  await page.goto('/reports?tab=tabela5')
  await page.getByRole('button', { name: 'Gjenero' }).click()

  const table = page.getByRole('table', { name: /^Tabela 5 — \d{4}$/ })
  await expect(table).toBeVisible()
  const number = table.getByRole('button', { name: /Ardhur gjatë vitit, Gjithsej: shiko çështjet/ })
  const shown = Number((await number.innerText()).replace(/\./g, ''))
  expect(shown).toBeGreaterThan(0)
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/report-builder-statistics-1536.png` })

  await number.click()
  const drawer = page.getByRole('presentation').filter({ has: page.getByRole('heading', { name: 'Ardhur gjatë vitit — Gjithsej' }) })
  await expect(drawer.getByText(shown === 1 ? '1 çështje' : `${shown} çështje`, { exact: true })).toBeVisible()
  await expect(drawer.getByRole('link').first()).toHaveAttribute('href', /^\/cases\//)
  await page.keyboard.press('Escape')
  await expect(drawer).toHaveCount(0)
})

/** "+ Shto filtër", "Ruaj raportin", "Raportet e mia" → "Hap" reruns it at once, "Fshi" removes it. */
test('a filtered report is saved, rerun from "Raportet e mia" and deleted', async ({ page }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  const name = `E2E raport ${Date.now()}`
  await login(page, USERS.kryetar)
  await page.goto('/reports')

  await page.getByRole('button', { name: 'Shto filtër' }).click()
  await page.getByRole('menuitem', { name: 'Kodi unik' }).click()
  await page.getByRole('textbox', { name: 'Kodi unik' }).fill(created.code)
  await page.getByRole('button', { name: 'Gjenero' }).click()
  const table = page.getByRole('table', { name: 'Tabela 1 — Aktive' })
  await expect(table.getByText('Gjithsej: 1 rreshta')).toBeVisible()

  await page.getByRole('button', { name: 'Ruaj raportin' }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: 'Emri i raportit' }).fill(name)
  await page.getByRole('dialog').getByRole('button', { name: 'Ruaj raportin' }).click()
  await expect(page.getByText(`Raporti „${name}” u ruajt.`)).toBeVisible()

  await page.getByRole('tab', { name: 'Raportet e mia' }).click()
  const row = page.getByRole('row').filter({ hasText: name })
  await row.getByRole('button', { name: 'Hap' }).click()
  await expect(page.getByRole('table', { name: 'Tabela 1 — Aktive' }).getByText(created.code)).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Kodi unik' })).toHaveValue(created.code)

  await page.getByRole('tab', { name: 'Raportet e mia' }).click()
  await page.getByRole('row').filter({ hasText: name }).getByRole('button', { name: 'Fshi' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Fshi' }).click()
  await expect(page.getByText(`Raporti „${name}” u fshi.`)).toBeVisible()
})
