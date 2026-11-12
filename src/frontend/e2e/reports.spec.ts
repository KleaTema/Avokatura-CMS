import { readFileSync } from 'node:fs'
import { expect, test } from '@playwright/test'
import { assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the three tabs and the print preview (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

/** Today in Tirane as yyyy-MM-dd (the export's file name carries it). */
const tiraneToday = () =>
  new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Tirane', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())

/**
 * Task 045 acceptance: the Kryetar exports the active cases in two clicks (menu "Raporte", then "Eksporto në Excel");
 * the file is an .xlsx (a zip: it starts with "PK") with the expected name.
 */
test('the Kryetar exports the active cases in two clicks', async ({ page }) => {
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')

  await page.getByRole('navigation', { name: /./ }).getByRole('link', { name: 'Raporte' }).click()
  await expect(page.getByRole('heading', { name: 'Raporte dhe statistika', level: 1 })).toBeVisible()
  await expect(page.getByText(/Do të eksportohen [\d.]+ rreshta/)).toBeVisible()
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Eksporto në Excel' }).click()
  const file = await download

  expect(file.suggestedFilename()).toBe(`raporti-ceshtjet-aktive-${tiraneToday()}.xlsx`)
  const bytes = readFileSync(await file.path())
  expect(bytes.subarray(0, 2).toString('latin1')).toBe('PK')
  await expect(page.getByText(/^U shkarkua raporti-ceshtjet-aktive/)).toBeVisible()
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/reports-active-1536.png` })

  await page.getByRole('tab', { name: 'Çështjet e mbyllura' }).click()
  await expect(page.getByRole('table', { name: 'Raporti i çështjeve të mbyllura' })).toBeVisible()
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/reports-closed-1536.png` })
})

/** A number of the statistics opens a drawer with the cases behind it — exactly as many as the number. */
test('a statistics number opens a drawer with the same count of cases', async ({ page }) => {
  // a case registered (and assigned) today: this year's "Ardhur gjatë vitit" is at least 1
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await login(page, USERS.kryetar)
  await page.goto('/reports?tab=statistika')

  const table = page.getByRole('table', { name: /^Tabela 5 — \d{4}$/ })
  await expect(table).toBeVisible()
  await expect(page.getByText('Viti aktual — shifra të përditësuara')).toBeVisible()
  // the TOTAL row's "Ardhur gjatë vitit"
  const number = table.getByRole('button', { name: /Ardhur gjatë vitit, Gjithsej: shiko çështjet/ })
  const shown = Number((await number.innerText()).replace(/\./g, ''))
  expect(shown).toBeGreaterThan(0)
  if (SHOTS) await page.screenshot({ path: `${SHOTS}/reports-statistics-1536.png` })

  await number.click()
  const drawer = page.getByRole('presentation').filter({ has: page.getByRole('heading', { name: 'Ardhur gjatë vitit — Gjithsej' }) })
  await expect(drawer.getByText(shown === 1 ? '1 çështje' : `${shown} çështje`, { exact: true })).toBeVisible()
  await expect(drawer.getByRole('link').first()).toHaveAttribute('href', /^\/cases\//)
  await page.keyboard.press('Escape')
  await expect(drawer).toHaveCount(0)

  if (SHOTS) {
    // A4 landscape at 96 dpi: what the printed page holds
    await page.setViewportSize({ width: 1123, height: 794 })
    await page.emulateMedia({ media: 'print' })
    await page.screenshot({ path: `${SHOTS}/reports-statistics-print.png`, fullPage: true })
    await page.emulateMedia({ media: 'screen' })
  }
})
