import { expect, type Locator, type Page } from '@playwright/test'

export type AccountName = 'Checking' | 'Savings'
export type OperationLabel = 'Deposit' | 'Withdraw' | 'Transfer'

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
export const money = (amount: number) => usd.format(amount)
const parseMoney = (text: string) => Number(text.replace(/[^0-9.-]/g, ''))

export class AtmPage {
  readonly consoleErrors: string[] = []
  readonly panel: Locator
  readonly ledger: Locator
  readonly ledgerRows: Locator
  readonly amountInput: Locator
  readonly amountHelp: Locator
  readonly receipt: Locator

  constructor(readonly page: Page) {
    page.on('console', (m) => {
      if (m.type() === 'error') this.consoleErrors.push(m.text())
    })
    page.on('pageerror', (e) => this.consoleErrors.push(e.message))
    this.panel = page.getByTestId('action-panel')
    this.ledger = page.getByTestId('ledger')
    this.ledgerRows = this.ledger.getByTestId('ledger-row')
    this.amountInput = this.panel.getByLabel('Amount (USD)')
    this.amountHelp = this.panel.getByTestId('amount-help')
    this.receipt = this.panel.getByTestId('receipt')
  }

  async goto() {
    await this.page.goto('/')
  }

  async open() {
    await this.goto()
    await expect(this.balanceLabel('Checking'), 'accounts load').toBeVisible()
  }

  card(name: AccountName) {
    return this.page.locator(`[data-testid="account-card"][data-account-name="${name}"]`)
  }

  balanceLabel(name: AccountName) {
    return this.card(name).getByTestId('balance')
  }

  async balance(name: AccountName) {
    return parseMoney(await this.balanceLabel(name).innerText())
  }

  async balances(): Promise<Record<AccountName, number>> {
    return { Checking: await this.balance('Checking'), Savings: await this.balance('Savings') }
  }

  alert(text: string | RegExp) {
    return this.page.getByRole('alert').filter({ hasText: text })
  }

  reviewButton(op: OperationLabel) {
    return this.panel.getByRole('button', { name: `Review ${op.toLowerCase()}`, exact: true })
  }

  ledgerRow(index: number) {
    const row = this.ledgerRows.nth(index)
    return {
      row,
      type: row.getByTestId('ledger-type'),
      description: row.getByTestId('ledger-description'),
      amount: row.getByTestId('ledger-amount'),
      balance: row.getByTestId('ledger-balance'),
    }
  }

  /** Selects the account card and the operation; the activity list follows the account. */
  async startOperation(name: AccountName, op: OperationLabel) {
    await this.card(name).click()
    await this.panel.getByRole('button', { name: op, exact: true }).click()
  }

  async fillAmount(amount: string) {
    await this.amountInput.fill(amount)
  }

  async review(op: OperationLabel) {
    await this.reviewButton(op).click()
  }

  confirmButton() {
    return this.panel.getByRole('button', { name: 'Confirm', exact: true })
  }

  async confirm() {
    await this.confirmButton().click()
  }

  async back() {
    await this.panel.getByRole('button', { name: 'Back', exact: true }).click()
  }

  async withdraw(name: AccountName, amount: string) {
    await this.startOperation(name, 'Withdraw')
    await this.fillAmount(amount)
    await this.review('Withdraw')
    await this.confirm()
  }

  async deposit(name: AccountName, amount: string) {
    await this.startOperation(name, 'Deposit')
    await this.fillAmount(amount)
    await this.review('Deposit')
    await this.confirm()
  }
}
