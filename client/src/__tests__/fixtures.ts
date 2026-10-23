import type { Account, Transaction, TransferReceipt } from '@/types'

let seq = 0
const nextId = (prefix: string) => `${prefix}-${++seq}`

export function account(overrides: Partial<Account> = {}): Account {
  return { id: nextId('acc'), name: 'Checking', balance: 100, ...overrides }
}

export function ledgerRow(overrides: Partial<Transaction> = {}): Transaction {
  return {
    id: nextId('tx'),
    accountId: 'acc-0',
    type: 'Deposit',
    amount: 10,
    balanceAfter: 110,
    correlationId: null,
    timestampUtc: '2026-01-15T12:00:00Z',
    description: null,
    ...overrides,
  }
}

export function transferReceipt(
  from: { accountId: string; balanceAfter: number },
  to: { accountId: string; balanceAfter: number },
  amount: number,
): TransferReceipt {
  const correlationId = nextId('corr')
  return {
    correlationId,
    withdrawal: ledgerRow({ ...from, type: 'TransferOut', amount, correlationId }),
    deposit: ledgerRow({ ...to, type: 'TransferIn', amount, correlationId }),
  }
}

export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}
