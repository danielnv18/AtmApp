import { beforeEach, describe, expect, test, vi } from 'vitest'
import { ApiError, atmClient } from '@/api/atmClient'
import { account, ledgerRow, transferReceipt } from '@/__tests__/fixtures'

const fetchMock = vi.fn<typeof fetch>()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
})

const json = (body: unknown, init: ResponseInit = {}) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })

const problem = (status: number, statusText: string, body: Record<string, unknown>) =>
  new Response(JSON.stringify({ status, ...body }), {
    status,
    statusText,
    headers: { 'Content-Type': 'application/problem+json' },
  })

function lastCall() {
  const [url, init] = fetchMock.mock.lastCall!
  return {
    url,
    method: init?.method ?? 'GET',
    contentType: new Headers(init?.headers).get('Content-Type'),
    body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
  }
}

describe('reads', () => {
  test('getAccounts GETs /api/atm/accounts and returns the parsed list', async () => {
    const accounts = [account({ name: 'Checking' }), account({ name: 'Savings' })]
    fetchMock.mockResolvedValue(json(accounts))

    await expect(atmClient.getAccounts()).resolves.toEqual(accounts)
    expect(lastCall()).toMatchObject({ url: '/api/atm/accounts', method: 'GET', body: undefined })
  })

  test('getHistory GETs the account history with the default limit of 50', async () => {
    const page = { items: [ledgerRow({ accountId: 'a1' })], nextCursor: null }
    fetchMock.mockResolvedValue(json(page))

    await expect(atmClient.getHistory('a1')).resolves.toEqual(page)
    expect(lastCall()).toMatchObject({ url: '/api/atm/accounts/a1/history?limit=50', method: 'GET' })
  })

  test('getHistory passes an explicit limit', async () => {
    fetchMock.mockResolvedValue(json({ items: [], nextCursor: null }))

    await atmClient.getHistory('a1', 10)
    expect(lastCall().url).toBe('/api/atm/accounts/a1/history?limit=10')
  })
})

describe('writes', () => {
  test('deposit POSTs { accountId, amount } as JSON', async () => {
    const tx = ledgerRow({ accountId: 'a1', amount: 25 })
    fetchMock.mockResolvedValue(json(tx))

    await expect(atmClient.deposit('a1', 25)).resolves.toEqual(tx)
    expect(lastCall()).toEqual({
      url: '/api/atm/deposit',
      method: 'POST',
      contentType: 'application/json',
      body: { accountId: 'a1', amount: 25 },
    })
  })

  test('withdraw POSTs { accountId, amount } as JSON', async () => {
    fetchMock.mockResolvedValue(json(ledgerRow({ type: 'Withdrawal' })))

    await atmClient.withdraw('a1', 20)
    expect(lastCall()).toEqual({
      url: '/api/atm/withdraw',
      method: 'POST',
      contentType: 'application/json',
      body: { accountId: 'a1', amount: 20 },
    })
  })

  test('transfer POSTs { fromAccountId, toAccountId, amount } as JSON', async () => {
    const receipt = transferReceipt({ accountId: 'a1', balanceAfter: 90 }, { accountId: 'a2', balanceAfter: 60 }, 10)
    fetchMock.mockResolvedValue(json(receipt))

    await expect(atmClient.transfer('a1', 'a2', 10)).resolves.toEqual(receipt)
    expect(lastCall()).toEqual({
      url: '/api/atm/transfer',
      method: 'POST',
      contentType: 'application/json',
      body: { fromAccountId: 'a1', toAccountId: 'a2', amount: 10 },
    })
  })
})

describe('errors', () => {
  test.each([
    [400, 'Bad Request', 'Amount must be positive.'],
    [404, 'Not Found', 'Account a9 was not found.'],
    [409, 'Conflict', 'Concurrent update. Retry.'],
    [422, 'Insufficient funds', 'Balance is $5.00.'],
    [500, 'Internal Server Error', 'Something broke.'],
  ])('a %d problem+json becomes an ApiError with status, title and detail', async (status, title, detail) => {
    fetchMock.mockResolvedValue(problem(status, 'Status Text', { title, detail }))

    const error = await atmClient.withdraw('a1', 20).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status, title, detail, message: detail })
  })

  test('a problem without detail has a null detail and uses the title as its message', async () => {
    fetchMock.mockResolvedValue(problem(404, 'Not Found', { title: 'Account not found' }))

    const error = await atmClient.getHistory('a9').catch((e: unknown) => e)
    expect(error).toMatchObject({ status: 404, title: 'Account not found', detail: null, message: 'Account not found' })
  })

  test('a non-JSON error body falls back to statusText with a null detail', async () => {
    fetchMock.mockResolvedValue(
      new Response('<html>Bad Gateway</html>', { status: 502, statusText: 'Bad Gateway' }),
    )

    const error = await atmClient.getAccounts().catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 502, title: 'Bad Gateway', detail: null })
  })

  test('a network failure propagates as-is, not as an ApiError', async () => {
    const failure = new TypeError('Failed to fetch')
    fetchMock.mockRejectedValue(failure)

    const error = await atmClient.getAccounts().catch((e: unknown) => e)
    expect(error).toBe(failure)
    expect(error).not.toBeInstanceOf(ApiError)
  })
})
