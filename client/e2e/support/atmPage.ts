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
  readonly confirmDialog: Locator

  constructor(readonly page: Page) {
    page.on('console', (m) => {
      if (m.type() === 'error') this.consoleErrors.push(m.text())
    })
    page.on('pageerror', (e) => this.consoleErrors.push(e.message))
    this.panel = page.getByTestId('action-panel')
    this.ledger = page.getByTestId('ledger')
    this.ledgerRows = this.ledger.getByTestId('ledger-row')
    this.amountInput = this.panel.getByLabel('Amount (USD)')
    this.confirmDialog = page.getByRole('dialog', { name: 'Confirm transfer' })
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

  toast(text: string | RegExp) {
    return this.page.locator('[data-sonner-toast]').filter({ hasText: text })
  }

  accountSelect(label: 'Account' | 'From' | 'To' | 'Ledger account') {
    const scope = label === 'Ledger account' ? this.ledger : this.panel
    return scope.getByRole('combobox', { name: label, exact: true })
  }

  submitButton(op: OperationLabel) {
    return this.panel.getByRole('button', { name: op, exact: true })
  }

  ledgerRow(index: number) {
    const row = this.ledgerRows.nth(index)
    const cells = row.getByRole('cell')
    return {
      row,
      type: cells.nth(1),
      description: cells.nth(2),
      amount: row.getByTestId('ledger-amount'),
      balance: row.getByTestId('ledger-balance'),
    }
  }

  async startOperation(name: AccountName, op: OperationLabel) {
    await this.card(name).getByRole('button', { name: op, exact: true }).click()
  }

  async fillAmount(amount: string) {
    await this.amountInput.fill(amount)
  }

  async submit(op: OperationLabel) {
    await this.submitButton(op).click()
  }

  async chooseAccount(label: 'Account' | 'From' | 'To' | 'Ledger account', name: AccountName) {
    await this.accountSelect(label).click()
    await this.page.getByRole('option', { name: new RegExp(`^${name} \\(`) }).click()
  }

  async chooseTransferTarget(name: AccountName) {
    await this.chooseAccount('To', name)
  }

  async selectLedger(name: AccountName) {
    await this.chooseAccount('Ledger account', name)
  }

  async confirmTransfer() {
    await this.confirmDialog.getByRole('button', { name: 'Confirm transfer' }).click()
  }

  async cancelTransfer() {
    await this.confirmDialog.getByRole('button', { name: 'Cancel' }).click()
  }

  async withdraw(name: AccountName, amount: string) {
    await this.startOperation(name, 'Withdraw')
    await this.fillAmount(amount)
    await this.submit('Withdraw')
  }

  async deposit(name: AccountName, amount: string) {
    await this.startOperation(name, 'Deposit')
    await this.fillAmount(amount)
    await this.submit('Deposit')
  }
}
