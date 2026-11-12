import { expect, test, type Page } from '@playwright/test'
import { addAssistant, assignLawyers, markAllChatRead, registerCase, removeAssistant, USERS } from './support/api'
import { login } from './support/login'

/** Optional: a folder for the screenshots of the chat page and the case's Chat tab (E2E_SHOTS=…). */
const SHOTS = process.env.E2E_SHOTS

const composer = (page: Page) => page.getByRole('textbox', { name: 'Shkruani një mesazh' })
const log = (page: Page) => page.getByRole('log', { name: 'Mesazhet' })
const chatMenu = (page: Page) => page.getByRole('navigation', { name: /./ }).getByRole('link', { name: /Chat/ })

async function send(page: Page, text: string) {
  await composer(page).fill(text)
  await composer(page).press('Enter')
}

/**
 * Task 050 acceptance: avokat1 and avokat2, in two browser contexts, exchange messages live in the case's Chat tab
 * (no reload). After a reload avokat2's Chat badge is still right, and reading the conversation clears it.
 */
test('two lawyers exchange messages live; after a reload the unread badge is right', async ({ browser }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await markAllChatRead(USERS.avokat1)
  await markAllChatRead(USERS.avokat2)

  const c1 = await browser.newContext()
  const c2 = await browser.newContext()
  try {
    const a1 = await c1.newPage()
    const a2 = await c2.newPage()
    await login(a1, USERS.avokat1)
    await login(a2, USERS.avokat2)
    await a1.goto(`/cases/${created.id}/chat`)
    await expect(composer(a1)).toBeEnabled()
    await a2.goto(`/cases/${created.id}/chat`)
    await expect(composer(a2)).toBeEnabled()
    await a1.waitForLoadState('networkidle')
    await a2.waitForLoadState('networkidle')

    await send(a1, 'Përshëndetje!\nSeanca u shty për të hënën.')
    await expect(log(a2).getByText('Seanca u shty për të hënën.', { exact: false })).toBeVisible({ timeout: 5_000 })
    // my own message once (the optimistic copy and the hub's echo are one)
    await expect(log(a1).getByText('Seanca u shty për të hënën.', { exact: false })).toHaveCount(1)

    await send(a2, 'Faleminderit, e shënova. https://example.com/seanca')
    await expect(log(a1).getByText('Faleminderit, e shënova.', { exact: false })).toBeVisible({ timeout: 5_000 })
    const link = log(a1).getByRole('link', { name: 'https://example.com/seanca' })
    await expect(link).toHaveAttribute('target', '_blank')
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    if (SHOTS) await a1.screenshot({ path: `${SHOTS}/chat-case-tab-1536.png` })

    // avokat2 leaves the thread; a new message raises their Chat badge live and after a reload
    await a2.goto('/dashboard')
    await a2.waitForLoadState('networkidle')
    await send(a1, 'Një pyetje tjetër për dosjen.')
    await expect(chatMenu(a2)).toContainText('1', { timeout: 5_000 })
    await a2.reload()
    await expect(chatMenu(a2)).toContainText('1')

    // reading it on the chat page clears the badge — only in the window that has focus (task 050, item 4)
    await a2.bringToFront()
    await chatMenu(a2).click()
    await a2.getByRole('button', { name: new RegExp(created.code.replace('/', '\\/')) }).click()
    await expect(log(a2).getByText('Një pyetje tjetër për dosjen.')).toBeVisible()
    // MUI's Badge keeps its last number in the DOM while it hides: check that it is hidden, not that the text is gone
    await expect(chatMenu(a2).locator('.MuiBadge-badge')).toHaveClass(/MuiBadge-invisible/, { timeout: 5_000 })
    if (SHOTS) {
      await a2.screenshot({ path: `${SHOTS}/chat-page-1536.png` })
      await a2.setViewportSize({ width: 390, height: 844 })
      // the phone layout shows the thread on its own (it loads again)
      await expect(log(a2).getByText('Një pyetje tjetër për dosjen.')).toBeVisible()
      await a2.screenshot({ path: `${SHOTS}/chat-page-390.png` })
      await a2.getByRole('button', { name: 'Kthehu te bisedat' }).click()
      await a2.screenshot({ path: `${SHOTS}/chat-list-390.png` })
    }
  } finally {
    await c1.close()
    await c2.close()
  }
})

/**
 * A removed assistant loses the conversation: live in the open thread (participants.changed on their own connection),
 * and — the fallback when a push is missed — after a reload the API's 403 shows the same message, and the
 * conversation has left their list.
 */
test('a removed assistant loses the conversation, live and after a reload', async ({ browser }) => {
  const created = await registerCase()
  await assignLawyers(created.id, 'Avokat1 Dev', 'Avokat2 Dev')
  await addAssistant(created.id, USERS.avokat1, USERS.ndihmes)

  const context = await browser.newContext()
  try {
    const page = await context.newPage()
    await login(page, USERS.ndihmes)
    await page.goto(`/cases/${created.id}/chat`)
    await expect(composer(page)).toBeEnabled()
    await send(page, 'Ndihmësi është këtu.')
    await expect(log(page).getByText('Ndihmësi është këtu.')).toBeVisible()
    const conversationUrl = await (async () => {
      await page.goto('/chat')
      await page.getByRole('button', { name: new RegExp(created.code.replace('/', '\\/')) }).click()
      await expect(composer(page)).toBeEnabled()
      return page.url()
    })()
    await page.waitForLoadState('networkidle')

    await removeAssistant(created.id, USERS.avokat1, USERS.ndihmes)

    // live
    await expect(page.getByText('Nuk jeni më pjesë e kësaj bisede.').first()).toBeVisible({ timeout: 5_000 })
    await expect(composer(page)).toHaveCount(0)

    // after a reload: the 403 path, and the list no longer has it
    await page.goto(conversationUrl)
    await expect(page.getByText('Nuk jeni më pjesë e kësaj bisede.').first()).toBeVisible()
    await expect(page.getByRole('button', { name: new RegExp(created.code.replace('/', '\\/')) })).toHaveCount(0)
  } finally {
    await context.close()
  }
})
