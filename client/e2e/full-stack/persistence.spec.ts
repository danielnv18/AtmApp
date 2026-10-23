import { AtmPage } from '../support/atmPage'
import { expect, money, test } from '../support/test'

test('balances and ledger survive a reload', async ({ atm, page }) => {
  await atm.open()
  const start = await atm.balances()

  await atm.deposit('Checking', '5')
  await expect(atm.toast('Deposited $5.00 to Checking')).toBeVisible()

  await page.reload()

  await expect(atm.balanceLabel('Checking'), 'deposit persisted').toHaveText(money(start.Checking + 5))
  await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings))
  const top = atm.ledgerRow(0)
  await expect(top.type, 'ledger persisted').toHaveText('Deposit')
  await expect(top.amount).toHaveText('+$5.00')
  await expect(top.balance).toHaveText(money(start.Checking + 5))
})

test('a stale tab withdraws against the true server balance', async ({ atm, context }) => {
  const tabA = atm
  const tabB = new AtmPage(await context.newPage())
  await tabA.open()
  await tabB.open()
  const start = await tabA.balance('Checking')

  await tabA.withdraw('Checking', '10')
  await expect(tabA.toast('Withdrew $10.00 from Checking')).toBeVisible()
  await expect(tabA.balanceLabel('Checking')).toHaveText(money(start - 10))
  await expect(tabB.balanceLabel('Checking'), 'tab B still shows the old balance').toHaveText(money(start))

  await tabB.withdraw('Checking', '20')

  await expect(tabB.toast('Withdrew $20.00 from Checking')).toBeVisible()
  await expect(tabB.balanceLabel('Checking'), 'tab B reflects both withdrawals').toHaveText(money(start - 30))
  await expect(tabB.ledgerRow(0).balance).toHaveText(money(start - 30))
  await expect(tabB.ledgerRow(1).balance, "tab A's withdrawal is in tab B's ledger").toHaveText(money(start - 10))
})
