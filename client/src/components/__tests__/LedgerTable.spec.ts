import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
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
async function mountLedger(history: Transaction[] | Error, nextCursor: string | null = null) {
  vi.resetModules()
  const { atmClient } = await import('@/api/atmClient')
  const { useAtm } = await import('@/composables/useAtm')
  const { default: LedgerTable } = await import('@/components/LedgerTable.vue')

  const api = vi.mocked(atmClient)
  api.getAccounts.mockResolvedValue([account({ id: 'chk', name: 'Checking' })])
  if (history instanceof Error) api.getHistory.mockRejectedValue(history)
  else api.getHistory.mockResolvedValue({ items: history, nextCursor })
  await useAtm().refresh()

  wrapper = mount(LedgerTable)
  await flushPromises()
  return Object.assign(wrapper, { api })
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

  describe('pagination', () => {
    test('is hidden when everything fits on one page', async () => {
      const w = await mountLedger([ledgerRow()])

      expect(w.find('nav').exists()).toBe(false)
    })

    test('Older loads the next page, Newer returns to the previous one', async () => {
      const w = await mountLedger([ledgerRow({ amount: 1 })], 'c1')
      expect(w.get('[data-testid="page-indicator"]').text()).toBe('Page 1')
      expect(w.get('[data-testid="page-newer"]').attributes('disabled')).toBeDefined()

      w.api.getHistory.mockResolvedValueOnce({ items: [ledgerRow({ amount: 2 })], nextCursor: null })
      await w.get('[data-testid="page-older"]').trigger('click')
      await flushPromises()

      expect(w.api.getHistory).toHaveBeenLastCalledWith('chk', 10, 'c1')
      expect(w.get('[data-testid="page-indicator"]').text()).toBe('Page 2')
      expect(w.get('[data-testid="ledger-amount"]').text()).toBe('+$2.00')
      expect(w.get('[data-testid="page-older"]').attributes('disabled')).toBeDefined()

      w.api.getHistory.mockResolvedValueOnce({ items: [ledgerRow({ amount: 1 })], nextCursor: 'c1' })
      await w.get('[data-testid="page-newer"]').trigger('click')
      await flushPromises()

      expect(w.get('[data-testid="page-indicator"]').text()).toBe('Page 1')
      expect(w.get('[data-testid="ledger-amount"]').text()).toBe('+$1.00')
    })
  })

  describe('day grouping', () => {
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['Date'] })
      vi.setSystemTime(new Date('2026-06-15T12:00:00'))
    })
    afterEach(() => vi.useRealTimers())

    const headings = (w: VueWrapper) => w.findAll('h3').map((h) => h.text())

    test('labels today, yesterday and older days, newest first', async () => {
      const w = await mountLedger([
        ledgerRow({ timestampUtc: '2026-06-15T10:00:00' }),
        ledgerRow({ timestampUtc: '2026-06-15T09:00:00' }),
        ledgerRow({ timestampUtc: '2026-06-14T10:00:00' }),
        ledgerRow({ timestampUtc: '2026-06-01T10:00:00' }),
      ])

      expect(headings(w)).toEqual(['Today', 'Yesterday', 'Jun 1, 2026'])
      const sections = w.findAll('section')
      expect(sections.map((s) => s.findAll('[data-testid="ledger-row"]').length)).toEqual([2, 1, 1])
    })

    test('"Yesterday" still works across a month boundary', async () => {
      vi.setSystemTime(new Date('2026-07-01T12:00:00'))
      const w = await mountLedger([ledgerRow({ timestampUtc: '2026-06-30T10:00:00' })])

      expect(headings(w)).toEqual(['Yesterday'])
    })
  })

  test('floating point noise in amounts never leaks into the display', async () => {
    const w = await mountLedger([
      ledgerRow({ accountId: 'chk', type: 'Deposit', amount: 0.1 + 0.2, balanceAfter: 100.1 + 200.2 }),
    ])

    expect(rowCells(w)).toEqual([{ type: 'Deposit', amount: '+$0.30', balance: '$300.30' }])
  })
})
