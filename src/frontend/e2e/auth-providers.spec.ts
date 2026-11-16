import { expect, test, type Page } from '@playwright/test'
import { USERS } from './support/api'
import { login } from './support/login'

/**
 * Task 051 — the login page with identity providers and the idle logout. No real tenant: GET /api/auth/config and the
 * provider's OpenID metadata are answered by the test (page.route), so what is checked is the app's side — one button
 * per provider, a single provider redirecting at once, MSAL's auth-code + PKCE request to the provider.
 */
const AUTHORITY = 'https://idp.example.test/ash-tenant/v2.0'

const provider = (name: string, displayName: string) => ({
  name,
  kind: name === 'b2c' ? 'B2C' : 'EntraId',
  displayName,
  authority: AUTHORITY,
  clientId: `spa-${name}`,
  scopes: ['api://ash-api/access'],
})

async function fakeProviders(page: Page, providers: ReturnType<typeof provider>[]) {
  await page.route('**/api/auth/config', (route) => route.fulfill({ json: { mode: 'oidc', providers } }))
  await page.route('https://idp.example.test/**', (route) => {
    const url = route.request().url()
    if (url.includes('/.well-known/openid-configuration')) {
      return route.fulfill({
        json: {
          issuer: AUTHORITY,
          authorization_endpoint: 'https://idp.example.test/ash-tenant/oauth2/v2.0/authorize',
          token_endpoint: 'https://idp.example.test/ash-tenant/oauth2/v2.0/token',
          end_session_endpoint: 'https://idp.example.test/ash-tenant/oauth2/v2.0/logout',
          jwks_uri: 'https://idp.example.test/ash-tenant/discovery/v2.0/keys',
          response_types_supported: ['code'],
        },
      })
    }

    return route.fulfill({ contentType: 'text/html', body: '<html><body>Faqja e ofruesit (test)</body></html>' })
  })
}

test('two providers: one button each, and a click starts an auth-code + PKCE redirect to that provider', async ({ page }) => {
  await fakeProviders(page, [
    provider('entra', 'Hyr me llogarinë e institucionit (Microsoft)'),
    provider('b2c', 'Hyr me email dhe fjalëkalim'),
  ])
  await page.goto('/login')

  await expect(page.getByRole('button', { name: 'Hyr me llogarinë e institucionit (Microsoft)' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Hyr me email dhe fjalëkalim' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Përdoruesi' })).toHaveCount(0) // no dev login

  const authorize = page.waitForRequest((r) => r.url().startsWith('https://idp.example.test/ash-tenant/oauth2/v2.0/authorize'))
  await page.getByRole('button', { name: 'Hyr me email dhe fjalëkalim' }).click()
  const url = new URL((await authorize).url())
  expect(url.searchParams.get('client_id')).toBe('spa-b2c')
  expect(url.searchParams.get('response_type')).toBe('code')
  expect(url.searchParams.get('code_challenge_method')).toBe('S256')
  expect(url.searchParams.get('code_challenge')).toBeTruthy()
  expect(url.searchParams.get('redirect_uri')).toMatch(/\/auth\/callback$/)
  expect(url.searchParams.get('scope')).toContain('api://ash-api/access')
})

test('a single provider redirects at once, without a button', async ({ page }) => {
  await fakeProviders(page, [provider('entra', 'Hyr me llogarinë e institucionit (Microsoft)')])
  const authorize = page.waitForRequest((r) => r.url().startsWith('https://idp.example.test/ash-tenant/oauth2/v2.0/authorize'))
  await page.goto('/login')

  expect(new URL((await authorize).url()).searchParams.get('client_id')).toBe('spa-entra')
})

test('30 minutes idle: a 60-second warning, "Vazhdo punën" keeps the session, then the logout', async ({ page }) => {
  await page.clock.install()
  await login(page, USERS.kryetar)
  await page.goto('/dashboard')
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  await page.clock.fastForward('30:01')
  const warning = page.getByRole('dialog', { name: 'Jeni ende aty?' })
  await expect(warning).toBeVisible()
  await warning.getByRole('button', { name: 'Vazhdo punën' }).click()
  await expect(warning).toHaveCount(0)

  // idle again: the warning, then after 60 seconds the logout (dev mode: back to the login page)
  await page.clock.fastForward('30:01')
  await expect(warning).toBeVisible()
  for (let i = 0; i < 61; i++) await page.clock.fastForward(1_000)
  await expect(page).toHaveURL(/\/login/)
})

test('the administrator sees a user\'s sign-in identities', async ({ page }) => {
  await login(page, USERS.admin)
  await page.goto('/admin/users')
  const row = page.getByRole('row').filter({ hasText: 'kryetar@ash.local' })
  await row.getByRole('button', { name: /^Identitetet:/ }).click()
  const dialog = page.getByRole('dialog', { name: /^Identitetet e hyrjes — / })
  // the dev login links nothing: no provider account yet
  await expect(dialog.getByText('Përdoruesi nuk ka hyrë ende me asnjë ofrues.')).toBeVisible()
})
