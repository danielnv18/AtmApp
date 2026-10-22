import { describe, expect, test, vi } from 'vitest'
import { account, deferred, ledgerRow, transferReceipt } from '@/__tests__/fixtures'
import type { HistoryPage } from '@/types'

vi.mock('@/api/atmClient', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/atmClient')>()
  return {
    ...actual,
    atmClient: {
      getAccounts: vi.fn(),
      getHistory: vi.fn(),
      deposit: vi.fn(),
      withdraw: vi.fn(),
      transfer: vi.fn(),
    },
  }
})

// useAtm keeps module-level state, so every test gets a fresh module graph.
async function setup() {
  vi.resetModules()
  const { atmClient, ApiError } = await import('@/api/atmClient')
  const { useAtm } = await import('@/composables/useAtm')
  return { api: vi.mocked(atmClient), ApiError, atm: useAtm() }
}

const page = (...items: HistoryPage['items']): HistoryPage => ({ items, nextCursor: null })

const checking = () => account({ id: 'chk', name: 'Checking', balance: 100 })
const savings = () => account({ id: 'sav', name: 'Savings', balance: 500 })

async function loaded() {
  const ctx = await setup()
  ctx.api.getAccounts.mockResolvedValue([checking(), savings()])
  ctx.api.getHistory.mockResolvedValue(page())
  await ctx.atm.refresh()
  return ctx
}

describe('refresh', () => {
  test('loads accounts, selects the first account for the ledger and loads its history', async () => {
    const { api, atm } = await setup()
    const row = ledgerRow({ accountId: 'chk' })
    api.getAccounts.mockResolvedValue([checking(), savings()])
    api.getHistory.mockResolvedValue(page(row))

    await atm.refresh()

    expect(atm.accounts.value.map((a) => a.id)).toEqual(['chk', 'sav'])
    expect(atm.accountsError.value).toBeNull()
    expect(atm.ledgerAccountId.value).toBe('chk')
    expect(api.getHistory).toHaveBeenCalledWith('chk')
    expect(atm.history.value).toEqual([row])
  })

  test('keeps the ledger on the account the user picked', async () => {
    const { api, atm } = await loaded()
    await atm.showHistory('sav')

    await atm.refresh()

    expect(atm.ledgerAccountId.value).toBe('sav')
    expect(api.getHistory).toHaveBeenLastCalledWith('sav')
  })

  test('a failure sets accountsError and keeps the accounts already loaded', async () => {
    const { api, atm } = await loaded()
    api.getAccounts.mockRejectedValue(new Error('Server unavailable'))

    await atm.refresh()

    expect(atm.accountsError.value).toBe('Server unavailable')
    expect(atm.accounts.value.map((a) => a.id)).toEqual(['chk', 'sav'])
  })

  test('a later success clears accountsError', async () => {
    const { api, atm } = await setup()
    api.getAccounts.mockRejectedValueOnce(new Error('down')).mockResolvedValue([checking()])
    api.getHistory.mockResolvedValue(page())

    await atm.refresh()
    await atm.refresh()

    expect(atm.accountsError.value).toBeNull()
    expect(atm.accounts.value).toHaveLength(1)
  })

  test('no accounts means no ledger account and no history request', async () => {
    const { api, atm } = await setup()
    api.getAccounts.mockResolvedValue([])

    await atm.refresh()

    expect(atm.ledgerAccountId.value).toBeNull()
    expect(api.getHistory).not.toHaveBeenCalled()
  })

  test('a non-Error rejection is still reported as text', async () => {
    const { api, atm } = await setup()
    api.getAccounts.mockRejectedValue('offline')

    await atm.refresh()

    expect(atm.accountsError.value).toBe('offline')
  })

  test('a history failure sets historyError', async () => {
    const { api, atm } = await setup()
    api.getAccounts.mockResolvedValue([checking()])
    api.getHistory.mockRejectedValue(new Error('history down'))

    await atm.refresh()

    expect(atm.historyError.value).toBe('history down')
  })
})

describe('operations', () => {
  test('deposit applies balanceAfter to only the touched account and reloads history', async () => {
    const { api, atm } = await loaded()
    const row = ledgerRow({ accountId: 'chk', amount: 25, balanceAfter: 125 })
    api.deposit.mockResolvedValue(row)
    api.getHistory.mockResolvedValue(page(row))

    await atm.deposit('chk', 25)

    expect(api.deposit).toHaveBeenCalledWith('chk', 25)
    expect(atm.accounts.value.map((a) => [a.id, a.balance])).toEqual([
      ['chk', 125],
      ['sav', 500],
    ])
    expect(atm.history.value).toEqual([row])
  })

  test('withdraw applies balanceAfter to only the touched account', async () => {
    const { api, atm } = await loaded()
    api.withdraw.mockResolvedValue(ledgerRow({ accountId: 'sav', type: 'Withdrawal', amount: 20, balanceAfter: 480 }))

    await atm.withdraw('sav', 20)

    expect(api.withdraw).toHaveBeenCalledWith('sav', 20)
    expect(atm.accounts.value.map((a) => [a.id, a.balance])).toEqual([
      ['chk', 100],
      ['sav', 480],
    ])
  })

  test('transfer updates both accounts', async () => {
    const { api, atm } = await loaded()
    api.transfer.mockResolvedValue(
      transferReceipt({ accountId: 'sav', balanceAfter: 400 }, { accountId: 'chk', balanceAfter: 200 }, 100),
    )

    await atm.transfer('sav', 'chk', 100)

    expect(api.transfer).toHaveBeenCalledWith('sav', 'chk', 100)
    expect(atm.accounts.value.map((a) => [a.id, a.balance])).toEqual([
      ['chk', 200],
      ['sav', 400],
    ])
  })

  test('pending is the operation while in flight and null once it settles', async () => {
    const { api, atm } = await loaded()
    const call = deferred<ReturnType<typeof ledgerRow>>()
    api.withdraw.mockReturnValue(call.promise)

    const op = atm.withdraw('chk', 20)
    expect(atm.pending.value).toBe('withdraw')

    call.resolve(ledgerRow({ accountId: 'chk', balanceAfter: 80 }))
    await op
    expect(atm.pending.value).toBeNull()
  })

  test('a failure resets pending to null, propagates the error and leaves balances alone', async () => {
    const { api, ApiError, atm } = await loaded()
    const failure = new ApiError(422, 'Insufficient funds', 'Balance is $100.00.')
    const call = deferred<never>()
    api.transfer.mockReturnValue(call.promise)

    const op = atm.transfer('chk', 'sav', 1000)
    expect(atm.pending.value).toBe('transfer')
    call.reject(failure)

    await expect(op).rejects.toBe(failure)
    expect(atm.pending.value).toBeNull()
    expect(atm.accounts.value.map((a) => a.balance)).toEqual([100, 500])
  })
})

describe('history race', () => {
  test('a slow response for an account the user switched away from is dropped', async () => {
    const { api, atm } = await loaded()
    const slow = deferred<HistoryPage>()
    const fresh = ledgerRow({ accountId: 'sav', description: 'savings row' })
    api.getHistory.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(page(fresh))

    const first = atm.showHistory('chk')
    await atm.showHistory('sav')
    slow.resolve(page(ledgerRow({ accountId: 'chk', description: 'stale checking row' })))
    await first

    expect(atm.ledgerAccountId.value).toBe('sav')
    expect(atm.history.value).toEqual([fresh])
  })

  test('a late error for an account the user switched away from is dropped', async () => {
    const { api, atm } = await loaded()
    const slow = deferred<HistoryPage>()
    api.getHistory.mockReturnValueOnce(slow.promise).mockResolvedValueOnce(page())

    const first = atm.showHistory('chk')
    await atm.showHistory('sav')
    slow.reject(new Error('stale failure'))
    await first

    expect(atm.historyError.value).toBeNull()
  })

  test('switching accounts clears the previous history and error immediately', async () => {
    const { api, atm } = await setup()
    api.getAccounts.mockResolvedValue([checking(), savings()])
    api.getHistory.mockRejectedValueOnce(new Error('chk down'))
    await atm.refresh()
    expect(atm.historyError.value).toBe('chk down')

    api.getHistory.mockReturnValue(deferred<HistoryPage>().promise)
    void atm.showHistory('sav')

    expect(atm.historyError.value).toBeNull()
    expect(atm.history.value).toEqual([])
  })
})
