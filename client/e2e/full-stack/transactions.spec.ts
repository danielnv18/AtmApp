import { expect, money, test } from '../support/test'

test.beforeEach(async ({ atm }) => {
  await atm.open()
})

test('deposit credits the card and tops the activity list', async ({ atm }) => {
  const start = await atm.balances()

  await atm.deposit('Checking', '37.25')

  await expect(atm.receipt, 'receipt confirms the deposit').toContainText('$37.25 deposited to Checking')
  await expect(atm.balanceLabel('Checking'), 'Checking rises by the deposit').toHaveText(money(start.Checking + 37.25))
  await expect(atm.balanceLabel('Savings'), 'Savings is untouched').toHaveText(money(start.Savings))
  const top = atm.ledgerRow(0)
  await expect(top.type, 'top row is the deposit').toHaveText('Deposit')
  await expect(top.amount).toHaveText('+$37.25')
  await expect(top.balance, 'top row carries the new balance').toHaveText(money(start.Checking + 37.25))
})

test('quick $20 withdrawal from the Checking card', async ({ atm }) => {
  const start = await atm.balances()
  const expected = money(start.Checking - 20)

  await atm.startOperation('Checking', 'Withdraw')
  await atm.panel.getByRole('button', { name: '$20', exact: true }).click()
  await expect(atm.amountInput, 'quick amount fills the input').toHaveValue('20')
  await expect(atm.amountHelp, 'projected balance is shown').toHaveText(`New balance: ${expected}`)
  await atm.review('Withdraw')
  await atm.confirm()

  await expect(atm.receipt).toContainText('$20.00 withdrawn from Checking')
  await expect(atm.balanceLabel('Checking'), 'Checking drops by 20').toHaveText(expected)
  await expect(atm.balanceLabel('Savings'), 'Savings is untouched').toHaveText(money(start.Savings))
  const top = atm.ledgerRow(0)
  await expect(top.balance, 'top row shows the new balance').toHaveText(expected)
  await expect(top.type, 'top row is a withdrawal').toHaveText('Withdrawal')
  await expect(top.amount).toHaveText('−$20.00')
  expect(atm.consoleErrors, 'no console errors').toEqual([])
})

test.describe('transfer', () => {
  test.beforeEach(async ({ atm }) => {
    await atm.startOperation('Checking', 'Transfer')
    await expect(atm.panel.getByTestId('transfer-to'), 'destination is the other account').toHaveText('Savings')
    await atm.fillAmount('15.50')
    await atm.review('Transfer')
    await expect(atm.panel.getByTestId('review'), 'review names the move').toContainText('$15.50')
  })

  test('confirmed transfer moves money and records both legs', async ({ atm }) => {
    const start = await atm.balances()

    await atm.confirm()

    await expect(atm.receipt).toContainText('$15.50 moved from Checking to Savings')
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking - 15.5))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings + 15.5))

    const out = atm.ledgerRow(0)
    await expect(out.type, 'Checking activity shows the outgoing leg').toHaveText('Transfer out')
    await expect(out.description).toHaveText('Transfer to Savings')
    await expect(out.amount).toHaveText('−$15.50')
    await expect(out.balance).toHaveText(money(start.Checking - 15.5))

    await atm.card('Savings').click()
    const into = atm.ledgerRow(0)
    await expect(into.type, 'Savings activity shows the incoming leg').toHaveText('Transfer in')
    await expect(into.description).toHaveText('Transfer from Checking')
    await expect(into.amount).toHaveText('+$15.50')
    await expect(into.balance).toHaveText(money(start.Savings + 15.5))
  })

  test('backing out of the review changes nothing', async ({ atm, page }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.back()

    await expect(atm.amountInput, 'amount is kept').toHaveValue('15.50')
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings))
    await expect(atm.ledgerRows, 'activity is unchanged').toHaveText(rowsBefore)

    await page.reload()
    await expect(atm.balanceLabel('Checking'), 'server balance is unchanged').toHaveText(money(start.Checking))
    await expect(atm.balanceLabel('Savings')).toHaveText(money(start.Savings))
  })
})

test.describe('blocked and rejected amounts', () => {
  test('an overdraw is blocked before it reaches the server', async ({ atm }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.startOperation('Checking', 'Withdraw')
    await atm.fillAmount((start.Checking + 1).toFixed(2))

    await expect(atm.amountHelp, 'shortfall is explained').toContainText('$1.00 more than is available')
    await expect(atm.reviewButton('Withdraw'), 'cannot be reviewed').toBeDisabled()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.ledgerRows, 'activity is unchanged').toHaveText(rowsBefore)
  })

  test('amount above the server maximum surfaces as invalid amount', async ({ atm }) => {
    const start = await atm.balances()
    const rowsBefore = await atm.ledgerRows.allTextContents()

    await atm.startOperation('Checking', 'Deposit')
    await atm.fillAmount('2000000000')
    await expect(atm.reviewButton('Deposit'), 'client validation accepts the amount').toBeEnabled()
    await atm.review('Deposit')
    await atm.confirm()

    await expect(atm.alert('Invalid amount'), 'server 400 reaches the user').toBeVisible()
    await expect(atm.balanceLabel('Checking')).toHaveText(money(start.Checking))
    await expect(atm.ledgerRows, 'activity is unchanged').toHaveText(rowsBefore)
  })
})

test('malformed amounts keep review disabled', async ({ atm }) => {
  await atm.startOperation('Checking', 'Deposit')

  for (const input of ['abc', '1.234', '0']) {
    await atm.fillAmount(input)
    await expect(atm.amountInput, `"${input}" is marked invalid`).toHaveAttribute('aria-invalid', 'true')
    await expect(atm.reviewButton('Deposit'), `"${input}" cannot be reviewed`).toBeDisabled()
  }

  await atm.fillAmount('12.34')
  await expect(atm.amountInput, 'a valid amount clears the mark').toHaveAttribute('aria-invalid', 'false')
  await expect(atm.reviewButton('Deposit'), 'a valid amount can be reviewed').toBeEnabled()
})
