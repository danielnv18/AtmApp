// Usage: node scripts/verify-e2e.mjs [baseUrl]
// Withdraws $20 from Checking through the UI, so point it at a disposable API.
// VERIFY_ROUTES=<module> installs that module's default export on the page first (e.g. request mocks).
import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { chromium } from 'playwright'

const baseUrl = process.argv[2] ?? process.env.BASE_URL ?? 'http://localhost:5173'
const outDir = fileURLToPath(new URL('../.verify/', import.meta.url))
const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const parseMoney = (text) => Number(text.replace(/[^0-9.-]/g, ''))

const failures = []
async function check(name, fn) {
  try {
    await fn()
    console.log(`PASS ${name}`)
  } catch (e) {
    failures.push(name)
    console.log(`FAIL ${name}\n     ${e.message.split('\n')[0]}`)
  }
}
function assertEqual(actual, expected) {
  if (actual !== expected) throw new Error(`expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`)
}

const browser = await chromium.launch()
const page = await browser.newPage({ viewport: { width: 1280, height: 1100 } })
const consoleErrors = []
page.on('console', (m) => m.type() === 'error' && consoleErrors.push(m.text()))
page.on('pageerror', (e) => consoleErrors.push(e.message))

try {
  if (process.env.VERIFY_ROUTES) {
    const install = await import(pathToFileURL(resolve(process.env.VERIFY_ROUTES)).href)
    await install.default(page)
  }

  await page.goto(baseUrl)
  const card = (name) => page.locator(`[data-testid="account-card"][data-account-name="${name}"]`)
  const balance = (name) => card(name).getByTestId('balance')
  const panel = page.getByTestId('action-panel')
  const ledger = page.getByTestId('ledger')

  await balance('Checking').waitFor()
  const checking = parseMoney(await balance('Checking').innerText())
  const savings = parseMoney(await balance('Savings').innerText())
  const expected = usd.format(checking - 20)
  console.log(`start Checking=${usd.format(checking)} Savings=${usd.format(savings)}`)

  await ledger.getByRole('combobox').click()
  await page.getByRole('option', { name: /^Checking/ }).click()

  await card('Checking').getByRole('button', { name: 'Withdraw' }).click()
  await panel.getByRole('button', { name: '$20', exact: true }).click()
  await panel.getByRole('button', { name: 'Withdraw', exact: true }).click()

  await check('success toast appears', () =>
    page.locator('[data-sonner-toast]', { hasText: 'Withdrew $20.00 from Checking' }).waitFor({ timeout: 5000 }),
  )
  await check(`Checking drops by 20 to ${expected}`, () =>
    balance('Checking').filter({ hasText: expected }).waitFor({ timeout: 5000 }),
  )
  await check('Savings is unchanged', async () => assertEqual(parseMoney(await balance('Savings').innerText()), savings))
  await check('account select shows the live balance', async () =>
    assertEqual((await panel.getByRole('combobox').innerText()).trim(), `Checking (${expected})`),
  )

  const top = ledger.getByTestId('ledger-row').first()
  await check('top ledger row shows the new balance', () =>
    top.getByTestId('ledger-balance').filter({ hasText: expected }).waitFor({ timeout: 5000 }),
  )
  await check('top ledger row is a Withdrawal badge', async () =>
    assertEqual((await top.locator('[data-slot="badge"]').innerText()).trim(), 'Withdrawal'),
  )
  await check('top ledger row shows −$20.00', async () =>
    assertEqual((await top.getByTestId('ledger-amount').innerText()).trim(), '−$20.00'),
  )

  await mkdir(outDir, { recursive: true })
  await page.screenshot({ path: resolve(outDir, 'withdraw.png'), fullPage: true })
  console.log(`screenshot ${resolve(outDir, 'withdraw.png')}`)

  await check('no console errors', async () => assertEqual(consoleErrors.join('\n'), ''))
} catch (e) {
  failures.push(e.message)
  console.log(`FAIL ${e.message}`)
} finally {
  await browser.close()
}

if (failures.length > 0) {
  console.log(`\n${failures.length} check(s) failed`)
  process.exit(1)
}
console.log('\nall checks passed')
