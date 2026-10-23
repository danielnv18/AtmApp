import { expect, mockedTest as test } from '../support/test'

test('a pending withdrawal blocks a second submit', async ({ atm, fake }) => {
  await atm.open()
  const release = fake.hold('withdraw')

  await atm.startOperation('Checking', 'Withdraw')
  await atm.fillAmount('20')
  await atm.submitButton('Withdraw').dblclick()

  await expect(atm.submitButton('Withdraw'), 'submit is disabled while pending').toBeDisabled()
  release()

  await expect(atm.toast('Withdrew $20.00 from Checking')).toBeVisible()
  await expect(atm.balanceLabel('Checking')).toHaveText('$980.00')
  expect(fake.requestCount('withdraw'), 'exactly one withdrawal was sent').toBe(1)
  expect(fake.balance('Checking'), 'server debited once').toBe(980)
})

test('a slow history response for a ledger the user left is dropped', async ({ atm, fake, page }) => {
  const release = fake.hold('history')
  await atm.open()

  await atm.selectLedger('Savings')
  await expect(atm.ledgerRows, 'Savings history shows').toHaveCount(1)
  await expect(atm.ledgerRow(0).balance).toHaveText('$500.00')

  const checkingId = fake.accountId('Checking')
  const slow = page.waitForResponse((r) => r.url().includes(`/accounts/${checkingId}/history`))
  release()
  await (await slow).finished()
  // Let the page settle the stale response and render before asserting it changed nothing.
  await page.evaluate('new Promise((r) => requestAnimationFrame(() => setTimeout(r)))')

  await expect(atm.ledgerRows, 'only the Savings rows remain').toHaveCount(1)
  await expect(atm.ledgerRow(0).balance).toHaveText('$500.00')
  await expect(atm.accountSelect('Ledger account')).toHaveText('Savings ($500.00)')
})
