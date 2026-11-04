import { expect, test } from '@playwright/test'
import { assignLawyers, registerCase, statusOf, USERS } from './support/api'
import { login } from './support/login'

/** A small but real PDF (the API checks the first bytes; the browser can show it). */
const pdf = (text: string) =>
  Buffer.from(
    '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n' +
      '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>endobj\n' +
      '4 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n' +
      `5 0 obj<</Length ${text.length + 40}>>stream\nBT /F1 22 Tf 72 760 Td (${text}) Tj ET\nendstream endobj\n` +
      'trailer<</Root 1 0 R>>\n%%EOF\n',
  )

/**
 * Task 039 acceptance: the primary lawyer records the decision with the reasoned decision uploaded inside the dialog,
 * the case closes (its scheduled hearing cancelled with the closing), and the dossier is archived — all on the case
 * page, which is never left.
 */
test('record the decision with an inline PDF, close and archive without leaving the case page', async ({ page }) => {
  test.setTimeout(120_000)
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await login(page, USERS.avokat1)

  const casePath = `/cases/${created.id}/vendimi`
  await page.goto(casePath)
  const navigations: string[] = []
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) navigations.push(new URL(frame.url()).pathname)
  })

  await page.getByRole('button', { name: 'Regjistro vendimin dhe mbyll çështjen' }).click()
  const dialog = page.getByRole('dialog')

  // 1. Rezultati
  await dialog.getByRole('combobox', { name: /Rezultati/ }).click()
  await page.getByRole('option', { name: 'Humbur', exact: true }).click()
  await dialog.getByLabel('Nr. i vendimit').fill('E2E-V-1')
  await dialog.getByRole('button', { name: 'Vazhdo' }).click()

  // 2. Vlerat
  await dialog.getByLabel(/Vlera e humbur — Shuma/).fill('1250000,50')
  await dialog.getByRole('button', { name: 'Vazhdo' }).click()

  // 3. Statusi: final; the first instance's "Ankimuar" is then disabled
  await dialog.getByRole('checkbox', { name: 'Formë e prerë' }).check()
  await expect(dialog.getByRole('checkbox', { name: 'Ankimuar' })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Vazhdo' }).click()

  // 4. Vendimi i arsyetuar: upload the PDF here; it is selected as soon as its check says "Gati"
  await dialog.getByRole('switch', { name: 'Ka ardhur vendimi i arsyetuar' }).check()
  await expect(dialog.getByText(/^Lejohen:/)).toBeVisible() // the allowed types are loaded: files are taken
  await dialog.locator('input[type=file]').setInputFiles({ name: 'vendimi-e2e.pdf', mimeType: 'application/pdf', buffer: pdf('Vendimi E2E') })
  await dialog.getByRole('button', { name: /^Ngarko/ }).click()
  await expect(dialog.getByRole('radio', { name: /vendimi-e2e/ })).toBeChecked({ timeout: 30_000 })

  // the case still has its first hearing: listed, then cancelled with the closing
  await dialog.getByRole('button', { name: 'Mbyll çështjen' }).click()
  await expect(dialog.getByText('Çështja ka seanca të planifikuara')).toBeVisible()
  await dialog.getByRole('button', { name: 'Anuloji dhe mbyll' }).click()

  // closed; the final decision suggests archiving
  const archive = page.getByRole('dialog', { name: 'Arkivo çështjen?' })
  await expect(archive).toBeVisible()
  await archive.getByRole('button', { name: 'Arkivo', exact: true }).click()
  await expect(page.getByText('Dosja është e arkivuar – vetëm për lexim')).toBeVisible()
  await expect(page.getByText(/^E mbyllur më \d{2}\.\d{2}\.\d{4}$/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Plotëso / Ndrysho vendimin' })).toHaveCount(0) // read-only now

  // the summary: the reasoned decision received, with its document
  await expect(page.getByRole('button', { name: /Shiko: vendimi-e2e/ })).toBeVisible()
  expect(await statusOf(created.id, USERS.avokat1)).toBe('CLOSED')
  // never left the case page
  expect(navigations.every((path) => path === casePath)).toBe(true)
  expect(new URL(page.url()).pathname).toBe(casePath)
})
