import { afterEach, describe, expect, test, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { account, ledgerRow } from '@/__tests__/fixtures'

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
async function mountApp(accounts: ReturnType<typeof account>[] | Error) {
  vi.resetModules()
  const { atmClient } = await import('@/api/atmClient')
  const { default: App } = await import('@/App.vue')
  const api = vi.mocked(atmClient)
  if (accounts instanceof Error) api.getAccounts.mockRejectedValue(accounts)
  else api.getAccounts.mockResolvedValue(accounts)
  api.getHistory.mockResolvedValue({ items: [], nextCursor: null })

  wrapper = mount(App)
  await flushPromises()
  return { api, app: wrapper }
}

const chk = () => account({ id: 'chk', name: 'Checking', balance: 0.1 })
const sav = () => account({ id: 'sav', name: 'Savings', balance: 0.2 })

describe('App', () => {
  test('total balance is the exact sum of the accounts', async () => {
    const { app } = await mountApp([chk(), sav()])

    expect(0.1 + 0.2).not.toBe(0.3)
    expect(app.get('[data-testid="total"]').text()).toBe('$0.30')
  })

  test('loads accounts on mount, selects the first and shows its activity', async () => {
    const { api, app } = await mountApp([chk(), sav()])

    expect(api.getAccounts).toHaveBeenCalledTimes(1)
    expect(api.getHistory).toHaveBeenCalledWith('chk', 10, null)
    expect(app.findAll('[data-testid="account-card"]')).toHaveLength(2)
    expect(app.get('[data-testid="ledger"]').text()).toContain('Checking')
    expect(app.text()).toMatch(/Updated /)
  })

  test('shows loading placeholders before accounts arrive', async () => {
    vi.resetModules()
    const { atmClient } = await import('@/api/atmClient')
    const { default: App } = await import('@/App.vue')
    vi.mocked(atmClient).getAccounts.mockReturnValue(new Promise(() => {}))
    wrapper = mount(App)
    await flushPromises()

    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Loading accounts…')
    expect(wrapper.find('[data-testid="total"]').exists()).toBe(false)
  })

  test('a load failure shows the alert and Retry recovers', async () => {
    const { api, app } = await mountApp(new Error('Server down'))
    const alert = app.get('[role="alert"]')
    expect(alert.text()).toContain('Could not load accounts')
    expect(alert.text()).toContain('Server down')
    expect(app.text()).not.toMatch(/Updated /)

    api.getAccounts.mockResolvedValue([chk(), sav()])
    await alert.findAll('button').find((b) => b.text() === 'Retry')!.trigger('click')
    await flushPromises()

    expect(app.find('[role="alert"]').exists()).toBe(false)
    expect(app.findAll('[data-testid="account-card"]')).toHaveLength(2)
  })

  test('Refresh reloads and is disabled while loading', async () => {
    const { api, app } = await mountApp([chk(), sav()])
    const refresh = () => app.findAll('button').find((b) => b.text() === 'Refresh')!
    let release!: (v: ReturnType<typeof account>[]) => void
    api.getAccounts.mockReturnValue(new Promise((r) => (release = r)))

    await refresh().trigger('click')
    expect(refresh().element.disabled).toBe(true)

    release([account({ id: 'chk', name: 'Checking', balance: 5 }), sav()])
    await flushPromises()

    expect(refresh().element.disabled).toBe(false)
    expect(api.getAccounts).toHaveBeenCalledTimes(2)
  })

  test('selecting the other account loads its activity', async () => {
    const { api, app } = await mountApp([chk(), sav()])
    api.getHistory.mockResolvedValue({
      items: [ledgerRow({ accountId: 'sav', description: 'Gift' })],
      nextCursor: null,
    })

    await app.get('[data-account-name="Savings"]').trigger('click')
    await flushPromises()

    expect(api.getHistory).toHaveBeenLastCalledWith('sav', 10, null)
    expect(app.get('[data-testid="ledger"]').text()).toContain('Savings')
    expect(app.get('[data-testid="ledger"]').text()).toContain('Gift')
  })
})
