import { expect, mockedTest as test } from '../support/test'

const serverError = { status: 500, title: 'Unexpected error', detail: 'An unexpected error occurred.' }

test('accounts outage shows an alert and Retry recovers', async ({ atm, fake, page }) => {
  fake.fail('accounts', serverError)

  await atm.goto()

  const alert = page.getByRole('alert').filter({ hasText: 'Could not load accounts' })
  await expect(alert, 'outage alert').toBeVisible()
  await expect(atm.card('Checking'), 'no cards without accounts').toHaveCount(0)

  await alert.getByRole('button', { name: 'Retry' }).click()

  await expect(alert, 'alert clears after a good retry').toBeHidden()
  await expect(atm.balanceLabel('Checking')).toHaveText('$1,000.00')
  await expect(atm.balanceLabel('Savings')).toHaveText('$500.00')
})

test('history outage leaves the cards working', async ({ atm, fake, page }) => {
  fake.fail('history', serverError)

  await atm.open()

  await expect(page.getByRole('alert').filter({ hasText: 'Could not load history' }), 'history alert').toBeVisible()

  await atm.deposit('Savings', '25')

  await expect(atm.toast('Deposited $25.00 to Savings'), 'deposit still works').toBeVisible()
  await expect(atm.balanceLabel('Savings')).toHaveText('$525.00')
})

test('network failure during a withdrawal leaves the balance alone', async ({ atm, fake }) => {
  fake.fail('withdraw', 'abort')
  await atm.open()

  await atm.withdraw('Checking', '20')

  await expect(atm.toast('Could not reach the server'), 'connection error toast').toBeVisible()
  await expect(atm.balanceLabel('Checking')).toHaveText('$1,000.00')
  await expect(atm.submitButton('Withdraw'), 'user can try again').toBeEnabled()
  await expect(atm.amountInput, 'amount is kept for the retry').toHaveValue('20')
})

test('a conflict re-fetches the balance another client changed', async ({ atm, fake }) => {
  await atm.open()
  fake.withdrawElsewhere('Checking', 100)
  fake.fail('withdraw', {
    status: 409,
    title: 'Concurrent update',
    detail: 'The account was changed by another request. Reload and try again.',
  })

  await atm.withdraw('Checking', '20')

  await expect(atm.toast('The balance changed while you were working'), 'conflict toast').toBeVisible()
  await expect(atm.balanceLabel('Checking'), 'card shows the refreshed server balance').toHaveText('$900.00')
})
