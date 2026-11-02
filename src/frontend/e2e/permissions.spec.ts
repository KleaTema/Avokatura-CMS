import { expect, test, type Locator, type Page } from '@playwright/test'
import { assignLawyers, permissionsOf, registerCase, USERS, type CreatedCase } from './support/api'
import { login } from './support/login'

/**
 * Task 029: on the case page every action button is shown exactly when its code is in permissions[] of
 * GET /api/cases/{id} for that user — no role names, no status checks in the UI. Checked on a REGISTERED and an
 * ACTIVE case (the state rules of CaseAccessService decide the state-dependent actions).
 */
const ACTIONS: { permission: string | string[]; buttons: (page: Page) => Locator[] }[] = [
  { permission: 'case.assign_lawyer', buttons: (p) => [p.getByRole('button', { name: 'Cakto avokatin', exact: true })] },
  { permission: 'case.transfer', buttons: (p) => [p.getByRole('button', { name: 'Transfero', exact: true })] },
  { permission: 'assistant.manage', buttons: (p) => [p.getByRole('button', { name: 'Shto ndihmës' })] },
  { permission: 'case.delegate', buttons: (p) => [p.getByRole('button', { name: /^(Delego \/ Relato|Anulo delegimin)$/ })] },
  {
    permission: 'case.edit_data',
    buttons: (p) => [
      p.getByRole('button', { name: 'Ndrysho: Vlera e padisë' }),
      p.getByRole('button', { name: 'Ndrysho: Masa e sigurimit' }),
    ],
  },
  { permission: 'case.set_category', buttons: (p) => [p.getByRole('button', { name: 'Ndrysho: Kategoria' })] },
  { permission: 'case.set_status', buttons: (p) => [p.getByRole('button', { name: 'Ndrysho: Statusi' })] },
  {
    permission: ['case.set_priority', 'case.set_priority_any'],
    buttons: (p) => [p.getByRole('button', { name: 'Ndrysho: Prioriteti' })],
  },
  { permission: 'case.set_priority_any', buttons: (p) => [p.getByRole('combobox', { name: 'Prioriteti' })] },
  { permission: 'case.change_judge', buttons: (p) => [p.getByRole('button', { name: 'Ndrysho: Gjyqtari' })] },
  { permission: 'case.change_object', buttons: (p) => [p.getByRole('button', { name: 'Ndrysho: Objekti' })] },
  { permission: 'institution.assign_jurist', buttons: (p) => [p.getByRole('button', { name: /^Cakto juristin/ })] },
]

const PEOPLE = [USERS.protokoll, USERS.avokat1, USERS.avokat2, USERS.kryetar, USERS.monitor]

async function check(page: Page, created: CreatedCase, user: string) {
  const permissions = await permissionsOf(created.id, user)
  await page.goto(`/cases/${created.id}`)
  if (permissions === null) {
    // the API says the user cannot see it: the page says so, without telling whether the case exists
    await expect(page.getByText('Çështja nuk u gjet ose nuk keni akses.')).toBeVisible()
    return
  }

  await expect(page.getByRole('heading', { name: created.code })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Fletëthirrja' })).toBeVisible()
  for (const action of ACTIONS) {
    const codes = Array.isArray(action.permission) ? action.permission : [action.permission]
    const allowed = codes.some((code) => permissions.includes(code))
    for (const button of action.buttons(page)) {
      await expect(button, `${user}: ${codes.join(' / ')} ${allowed ? 'shown' : 'hidden'}`).toHaveCount(allowed ? 1 : 0)
    }
  }
}

for (const state of ['REGISTERED', 'ACTIVE'] as const) {
  test(`${state} case: the buttons are exactly the permissions of each user`, async ({ browser }) => {
    const created = await registerCase()
    if (state === 'ACTIVE') await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')

    for (const user of PEOPLE) {
      const context = await browser.newContext()
      const page = await context.newPage()
      await login(page, user)
      await check(page, created, user)
      await context.close()
    }
  })
}
