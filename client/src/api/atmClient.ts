import type { Account, Transaction } from '@/types'

type History = { items: Transaction[]; nextCursor: string | null }
type TransferReceipt = { correlationId: string; withdrawal: Transaction; deposit: Transaction }
type ProblemDetails = { title?: string; detail?: string }

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

async function request<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api/atm${path}`, body === undefined
    ? undefined
    : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  if (!res.ok) {
    const problem: ProblemDetails = await res.json().catch(() => ({}))
    throw new ApiError(res.status, problem.detail ?? problem.title ?? res.statusText)
  }
  return res.json() as Promise<T>
}

export const atmClient = {
  getAccounts: () => request<Account[]>('/accounts'),
  getHistory: (accountId: string) => request<History>(`/accounts/${accountId}/history`),
  deposit: (accountId: string, amount: number) =>
    request<Transaction>('/deposit', { accountId, amount }),
  withdraw: (accountId: string, amount: number) =>
    request<Transaction>('/withdraw', { accountId, amount }),
  transfer: (fromAccountId: string, toAccountId: string, amount: number) =>
    request<TransferReceipt>('/transfer', { fromAccountId, toAccountId, amount }),
}
