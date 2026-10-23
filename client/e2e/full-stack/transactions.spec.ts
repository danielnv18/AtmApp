import { expect, money, test } from '../support/test'

test.beforeEach(async ({ atm }) => {
  await atm.open()
})

test('deposit credits the card and tops the ledger', async ({ atm }) => {
  const start = await atm.balances()

  await atm.deposit('Checking', '37.25')

  await expect(atm.toast('Deposited $37.25 to Checking'), 'success toast').toBeVisible()
  await expect(atm.balanceLabel('Checking'), 'Checking rises by the deposit').toHaveText(money(start.Checking + 37.25))
  await expect(atm.balanceLabel('Savings'), 'Savings is untouched').toHaveText(money(start.Savings))
  const top = atm.ledgerRow(0)
  await expect(top.type, 'top ledger row is the deposit').toHaveText('Deposit')
  await expect(top.amount).toHaveText('+$37.25')
  await expect(top.balance, 'top ledger row carries the new balance').toHaveText(money(start.Checking + 37.25))
})

test('quick $20 withdrawal from the Checking card', async ({ atm }) => {
  const start = await atm.balances()
  const expected = money(start.Checking - 20)

  await atm.selectLedger('Checking')
  await atm.startOperation('Checking', 'Withdraw')
  await atm.panel.getByRole('button', { name: '$20', exact: true }).click()
  await expect(atm.amountInput, 'quick amount fills the input').toHaveValue('20')
  await atm.submit('Withdraw')

  await expect(atm.toast('Withdrew $20.00 from Checking'), 'success toast').toBeVisible()
  await expect(atm.balanceLabel('Checking'), 'Checking drops by 20').toHaveText(expected)
  await expect(atm.balanceLabel('Savings'), 'Savings is untouched').toHaveText(money(start.Savings))
  await expect(atm.accountSelect('Account'), 'account select shows the live balance').toHaveText(
    `Checking (${expected})`,
  )
  const top = atm.ledgerRow(0)
  await expect(top.balance, 'top ledger row shows the new balance').toHaveText(expected)
  await expect(top.type, 'top ledger row is a withdrawal').toHaveText('Withdrawal')
  await expect(top.amount).toHaveText('−$20.00')
  expect(atm.consoleErrors, 'no console errors').toEqual([])
})

test.describe('transfer', () => {
  test.beforeEach(async ({ atm }) => {
    await atm.startOperation('Checking', 'Transfer')
    await atm.chooseTransferTarget('Savings')
    await atm.fillAmount('15.50')
    await atm.submit('Transfer')
    await expect(atm.confirmDialog, 'confirm dialog names the move').toContainText(
      'Move $15.50 from Checking to Savings?',
    )
  })

  test('confirmed transfer moves money and records both legs', async ({ atm }) => {
    const start = await atm.balances()

    await atm.confirmTransfer()

    await expect(atm.toast('Transferred $15.50 from Checking to Savings'), 'success toast').toBeVisible()
    await expect(atm.confirmDialog, 'dialog closes').toBeHidden()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking - 15.5))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings + 15.5))

    const out = atm.ledgerRow(0)
    await expect(out.type, 'Checking ledger shows the outgoing leg').toHaveText('Transfer Out')
    await expect(out.description).toHaveText('Transfer to Savings')
    await expect(out.amount).toHaveText('−$15.50')
    await expect(out.balance).toHaveText(money(start.Checking - 15.5))

    await atm.selectLedger('Savings')
    const into = atm.ledgerRow(0)
    await expect(into.type, 'Savings ledger shows the incoming leg').toHaveText('Transfer In')
    await expect(into.description).toHaveText('Transfer from Checking')
    await expect(into.amount).toHaveText('+$15.50')
    await expect(into.balance).toHaveText(money(start.Savings + 15.5))
  })

  test('cancelled transfer changes nothing', async ({ atm, page }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.cancelTransfer()

    await expect(atm.confirmDialog, 'dialog closes').toBeHidden()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings))
    await expect(atm.ledgerRows, 'ledger is unchanged').toHaveText(rowsBefore)

    await page.reload()
    await expect(atm.balanceLabel('Checking'), 'server balance is unchanged').toHaveText(money(start.Checking))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings))
  })
})

test.describe('rejected by the server', () => {
  test('overdraw shows insufficient funds and changes nothing', async ({ atm }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.withdraw('Checking', (start.Checking + 1).toFixed(2))

    await expect(atm.toast('Insufficient funds'), 'insufficient funds toast').toBeVisible()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.ledgerRows, 'ledger is unchanged').toHaveText(rowsBefore)
  })

  test('amount above the server maximum surfaces as invalid amount', async ({ atm }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.startOperation('Checking', 'Deposit')
    await atm.fillAmount('2000000000')
    await expect(atm.submitButton('Deposit'), 'client validation accepts the amount').toBeEnabled()
    await atm.submit('Deposit')

    await expect(atm.toast('Invalid amount'), 'server 400 reaches the user').toBeVisible()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.ledgerRows, 'ledger is unchanged').toHaveText(rowsBefore)
  })
})

test('malformed amounts keep submit disabled', async ({ atm }) => {
  await atm.startOperation('Checking', 'Deposit')

  for (const input of ['abc', '1.234', '0']) {
    await atm.fillAmount(input)
    await expect(atm.amountInput, `"${input}" is marked invalid`).toHaveAttribute('aria-invalid', 'true')
    await expect(atm.submitButton('Deposit'), `"${input}" cannot be submitted`).toBeDisabled()
  }

  await atm.fillAmount('12.34')
  await expect(atm.amountInput, 'a valid amount clears the mark').toHaveAttribute('aria-invalid', 'false')
  await expect(atm.submitButton('Deposit'), 'a valid amount can be submitted').toBeEnabled()
})
