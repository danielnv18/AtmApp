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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`/api/atm${path}`, init)
  if (!res.ok) {
    const problem: ProblemDetails = await res.json().catch(() => ({}))
    throw new ApiError(res.status, problem.title ?? res.statusText, problem.detail ?? null)
  }
  return res.json() as Promise<T>
}

// The server runs a write once per key and answers a repeat with the first response, so a
// caller that retries with the same key cannot move the money twice.
const post = <T>(path: string, body: unknown, idempotencyKey: string) =>
  request<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(body),
  })

export const atmClient = {
  getAccounts: () => request<Account[]>('/accounts'),
  getHistory: (accountId: string, limit = 50, cursor?: string | null) => {
    const query = new URLSearchParams({ limit: String(limit) })
    if (cursor) query.set('cursor', cursor)
    return request<HistoryPage>(`/accounts/${accountId}/history?${query}`)
  },
  deposit: (accountId: string, amount: number, idempotencyKey: string) =>
    post<Transaction>('/deposit', { accountId, amount }, idempotencyKey),
  withdraw: (accountId: string, amount: number, idempotencyKey: string) =>
    post<Transaction>('/withdraw', { accountId, amount }, idempotencyKey),
  transfer: (fromAccountId: string, toAccountId: string, amount: number, idempotencyKey: string) =>
    post<TransferReceipt>('/transfer', { fromAccountId, toAccountId, amount }, idempotencyKey),
}
