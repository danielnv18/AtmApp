import { afterEach, describe, expect, test, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { account, ledgerRow } from '@/__tests__/fixtures'
import type { Transaction } from '@/types'

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

let wrapper: VueWrapper | undefined

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

// useAtm keeps module-level state, so every test gets a fresh module graph.
async function mountLedger(history: Transaction[] | Error) {
  vi.resetModules()
  const { atmClient } = await import('@/api/atmClient')
  const { useAtm } = await import('@/composables/useAtm')
  const { default: LedgerTable } = await import('@/components/LedgerTable.vue')

  const api = vi.mocked(atmClient)
  api.getAccounts.mockResolvedValue([account({ id: 'chk', name: 'Checking' })])
  if (history instanceof Error) api.getHistory.mockRejectedValue(history)
  else api.getHistory.mockResolvedValue({ items: history, nextCursor: null })
  await useAtm().refresh()

  wrapper = mount(LedgerTable)
  await flushPromises()
  return wrapper
}

const rowCells = (w: VueWrapper) =>
  w.findAll('[data-testid="ledger-row"]').map((row) => {
    const t = (id: string) => row.get(`[data-testid="${id}"]`).text()
    return { type: t('ledger-type'), amount: t('ledger-amount'), balance: t('ledger-balance') }
  })

describe('LedgerTable', () => {
  test('labels each row from its type and signs credits and debits', async () => {
    const w = await mountLedger([
      ledgerRow({ accountId: 'chk', type: 'Deposit', amount: 50, balanceAfter: 150 }),
      ledgerRow({ accountId: 'chk', type: 'Withdrawal', amount: 20, balanceAfter: 130 }),
      ledgerRow({ accountId: 'chk', type: 'TransferIn', amount: 1000, balanceAfter: 1130, correlationId: 'c1' }),
      ledgerRow({ accountId: 'chk', type: 'TransferOut', amount: 30, balanceAfter: 1100, correlationId: 'c2' }),
    ])

    expect(rowCells(w)).toEqual([
      { type: 'Deposit', amount: '+$50.00', balance: '$150.00' },
      { type: 'Withdrawal', amount: '−$20.00', balance: '$130.00' },
      { type: 'Transfer in', amount: '+$1,000.00', balance: '$1,130.00' },
      { type: 'Transfer out', amount: '−$30.00', balance: '$1,100.00' },
    ])
  })

  test('shows the description', async () => {
    const w = await mountLedger([ledgerRow({ description: 'Rent' })])

    expect(w.get('[data-testid="ledger-row"]').text()).toContain('Rent')
  })

  test('an empty history shows "No transactions yet."', async () => {
    const w = await mountLedger([])

    expect(w.findAll('[data-testid="ledger-row"]')).toHaveLength(0)
    expect(w.text()).toContain('No transactions yet.')
  })

  test('a history error shows the alert with the message', async () => {
    const w = await mountLedger(new Error('History service unavailable'))

    const alert = w.get('[role="alert"]')
    expect(alert.text()).toContain('Could not load history')
    expect(alert.text()).toContain('History service unavailable')
  })

  test('no alert when history loads', async () => {
    const w = await mountLedger([ledgerRow()])

    expect(w.find('[role="alert"]').exists()).toBe(false)
  })
})
