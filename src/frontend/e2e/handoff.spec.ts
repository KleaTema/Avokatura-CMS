import { expect, test, type Locator, type Page } from '@playwright/test'
import { API_URL, assignLawyers, registerCase, USERS } from './support/api'
import { login } from './support/login'

const APPEAL_COURT = 'Gjykata e Apelit Tiranë'
const TR_OFFICE = 'Zyra Qendrore Tiranë'

/** A MUI X date/time field: its sections are typed into (the popup is closed again). */
async function typeInto(page: Page, group: Locator, digits: string) {
  await group.getByRole('spinbutton').first().click()
  await page.keyboard.press('Escape') // the field opens its picker on click; typing goes to the sections
  await group.getByRole('spinbutton').first().focus()
  await page.keyboard.type(digits)
}

async function courtLevelName(code: string): Promise<string> {
  const response = await fetch(`${API_URL}/api/reference/court-levels`, { headers: { 'X-Dev-User': USERS.avokat1 } })
  const levels = (await response.json()) as { code: string; name: string }[]
  const level = levels.find((l) => l.code === code)
  if (!level) throw new Error(`Court level ${code} not found`)
  return level.name
}

/**
 * Task 053c: the lawyer closes X/1 (not final) with the destination — "Përcjellja" in the closing dialog — and the
 * receiving office's Protokoll finds it under "Çështjet e ardhura" and continues it to X/2 with level and court
 * already filled in. Locally the appeal court may or may not be mapped to an office: unmapped, the office and a
 * reason are chosen in the dialog.
 */
test('a closed level is handed off and the receiving Protokoll continues it from "Çështjet e ardhura"', async ({ page }) => {
  test.setTimeout(180_000)
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  const appeal = await courtLevelName('APPEAL')

  // 1. the lawyer closes X/1 with the destination
  await login(page, USERS.avokat1)
  await page.goto(`/cases/${created.id}/vendimi`)
  await page.getByRole('button', { name: 'Regjistro vendimin dhe mbyll çështjen' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox', { name: /Rezultati/ }).click()
  await page.getByRole('option', { name: 'Humbur', exact: true }).click()
  await dialog.getByRole('button', { name: 'Vazhdo' }).click() // → Vlerat
  await dialog.getByRole('button', { name: 'Vazhdo' }).click() // → Statusi
  await dialog.getByRole('checkbox', { name: 'Ankimuar' }).check()
  await dialog.getByRole('button', { name: 'Vazhdo' }).click() // → Vendimi i arsyetuar
  await expect(dialog.getByRole('tab', { name: 'Përcjellja' })).toBeVisible() // the extra step, not final (stepper = tabs)
  await dialog.getByRole('button', { name: 'Vazhdo' }).click() // → Përcjellja

  const confirm = dialog.getByRole('button', { name: 'Mbyll çështjen' })
  await expect(confirm).toBeDisabled() // until the destination is complete
  await dialog.getByRole('combobox', { name: /Shkalla e radhës/ }).click()
  await page.getByRole('option', { name: appeal, exact: true }).click()
  await dialog.getByRole('combobox', { name: /Gjykata/ }).click()
  await page.getByRole('option', { name: APPEAL_COURT, exact: true }).click()

  const resolved = dialog.getByText(/^Do t'i përcillet: /)
  const unmapped = dialog.getByText(/nuk i është caktuar asnjë zyre/)
  await expect(resolved.or(unmapped)).toBeVisible()
  if (await unmapped.isVisible()) {
    await dialog.getByRole('combobox', { name: /Zyra që e merr/ }).click()
    await page.getByRole('option', { name: TR_OFFICE, exact: true }).click()
    await dialog.getByLabel(/Arsyeja/).fill('Gjykata e Apelit Tiranë nuk ka zyrë të caktuar (e2e)')
  }

  await expect(confirm).toBeEnabled()
  await confirm.click()
  await expect(dialog.getByText('Çështja ka seanca të planifikuara')).toBeVisible()
  await dialog.getByRole('button', { name: 'Anuloji dhe mbyll' }).click()
  await expect(page.getByRole('heading', { name: 'Përcjellja e çështjes' })).toBeVisible()
  // the level name has parentheses ("Apel (shkalla e dytë)"): compare as plain text, not as a pattern
  await expect(page.getByText(`${appeal} — ${APPEAL_COURT}. Do t'i përcillet: `)).toBeVisible()

  // 2. the receiving Protokoll: "Çështjet e ardhura" → Vazhdo
  await login(page, USERS.protokoll)
  await page.goto('/cases?tab=incoming')
  await expect(page.getByRole('tab', { name: /^Çështjet e ardhura \(\d+\)$/ })).toHaveAttribute('aria-selected', 'true')
  const card = page.getByRole('listitem').filter({ hasText: created.code })
  await expect(card.getByText(`Shkalla e radhës: ${appeal} — ${APPEAL_COURT}`)).toBeVisible()
  await expect(card.getByText('Humbur')).toBeVisible()
  await card.getByRole('link', { name: `Vazhdo ${created.code} në shkallën e radhës` }).click()

  // the next-level form starts with the handoff's level and court
  await expect(page.getByRole('combobox', { name: /Shkalla e gjykimit/ })).toHaveText(appeal)
  await expect(page.getByRole('combobox', { name: /^Gjykata/ })).toHaveValue(APPEAL_COURT)
  const tag = Date.now().toString(36).toUpperCase()
  await page.getByLabel(/Nr\. fletëthirrjes/).fill(`E2E-FL2-${tag}`.slice(0, 20))
  await page.getByLabel(/Nr\. i aktit/).fill(`E2E-A2-${tag}`.slice(0, 20))
  await page.getByRole('combobox', { name: /Gjyqtari/ }).click()
  await page.getByRole('option').first().click()
  const hearing = new Date(Date.now() + 40 * 24 * 3600 * 1000)
  const dd = String(hearing.getDate()).padStart(2, '0')
  const mm = String(hearing.getMonth() + 1).padStart(2, '0')
  await typeInto(page, page.getByRole('group', { name: /Data e seancës/ }), `${dd}${mm}${hearing.getFullYear()}`)
  await typeInto(page, page.getByRole('group', { name: /^Ora/ }), '1000')
  await page.getByRole('button', { name: 'Regjistro' }).click()
  await expect(page.getByText(`${created.code.slice(0, -2)}/2`).first()).toBeVisible({ timeout: 15_000 })

  // accepted: it left "Çështjet e ardhura"
  await page.goto('/cases?tab=incoming')
  await expect(page.getByRole('tab', { name: /^Çështjet e ardhura/ })).toBeVisible()
  await expect(page.getByRole('listitem').filter({ hasText: created.code })).toHaveCount(0)
})
