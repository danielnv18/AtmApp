import type { Account, HistoryPage, ProblemDetails, Transaction, TransferReceipt } from '@/types'

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
  ) {
    super(detail ?? title)
    this.name = 'ApiError'
  }
}

async function request<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(
    `/api/atm${path}`,
    body === undefined
      ? undefined
      : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
  )
  if (!res.ok) {
    const problem: ProblemDetails = await res.json().catch(() => ({}))
    throw new ApiError(res.status, problem.title ?? res.statusText, problem.detail ?? null)
  }
  return res.json() as Promise<T>
}

export const atmClient = {
  getAccounts: () => request<Account[]>('/accounts'),
  getHistory: (accountId: string, limit = 50) =>
    request<HistoryPage>(`/accounts/${accountId}/history?limit=${limit}`),
  deposit: (accountId: string, amount: number) =>
    request<Transaction>('/deposit', { accountId, amount }),
  withdraw: (accountId: string, amount: number) =>
    request<Transaction>('/withdraw', { accountId, amount }),
  transfer: (fromAccountId: string, toAccountId: string, amount: number) =>
    request<TransferReceipt>('/transfer', { fromAccountId, toAccountId, amount }),
}
