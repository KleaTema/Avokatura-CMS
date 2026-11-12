import { expect, test } from '@playwright/test'
import { registerCase, USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 053b: the "Vazhdo çështje ekzistuese" search — separate fields from the server's filter registry, combined,
 * kept in the URL, with a short object snippet per result.
 */
test('the registration search combines its fields, keeps them in the URL and shows the object snippet', async ({ page }) => {
  const created = await registerCase() // REGISTERED: found, but "E pambyllur në sistem"
  const tag = created.actNumber.slice('E2E-'.length)
  await login(page, USERS.protokoll)
  await page.goto('/cases/continue')

  const search = page.getByRole('search')
  for (const label of ['Kodi unik / kodi i çështjes', 'Nr. i aktit', 'Paditësi', 'I padituri / person i tretë', 'Objekti']) {
    await expect(search.getByLabel(label)).toBeVisible()
  }
  await expect(page.getByText(/Plotësoni të paktën një fushë/)).toBeVisible()

  // the plaintiff alone finds it
  await search.getByLabel('Paditësi').fill(`E2E Paditës ${tag}`)
  const card = page.getByRole('listitem').filter({ hasText: created.code })
  await expect(card).toBeVisible()
  await expect(card.getByText(`Objekti: E2E test ${tag}`)).toBeVisible()
  await expect(card.getByText('E pambyllur në sistem')).toBeVisible()
  await expect(page).toHaveURL(/plaintiff=/)

  // the same name as a defendant: nothing (the fields combine)
  await search.getByLabel('I padituri / person i tretë').fill(`E2E Paditës ${tag}`)
  await expect(page.getByText('Asnjë dosje nuk përputhet me fushat e plotësuara.')).toBeVisible()

  // "Pastro fushat" empties every field
  await page.getByRole('button', { name: 'Pastro fushat' }).first().click()
  await expect(search.getByLabel('Paditësi')).toHaveValue('')

  // ?actNumber= (the duplicate dialog's link) fills the field and searches
  await page.goto(`/cases/continue?actNumber=${encodeURIComponent(created.actNumber)}`)
  await expect(page.getByRole('search').getByLabel('Nr. i aktit')).toHaveValue(created.actNumber)
  await expect(page.getByRole('listitem').filter({ hasText: created.code })).toBeVisible()
})
