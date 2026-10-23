import { afterEach, describe, expect, test, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { account, deferred, ledgerRow, transferReceipt } from '@/__tests__/fixtures'
import type { Operation, Transaction } from '@/types'

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

const checking = () => account({ id: 'chk', name: 'Checking', balance: 100 })
const savings = () => account({ id: 'sav', name: 'Savings', balance: 500 })

// useAtm keeps module-level state, so every test gets a fresh module graph.
async function mountPanel(tab: Operation, accountId = 'chk') {
  vi.resetModules()
  const { atmClient, ApiError } = await import('@/api/atmClient')
  const { useAtm } = await import('@/composables/useAtm')
  const { default: ActionPanel } = await import('@/components/ActionPanel.vue')

  const api = vi.mocked(atmClient)
  api.getAccounts.mockResolvedValue([checking(), savings()])
  api.getHistory.mockResolvedValue({ items: [], nextCursor: null })
  await useAtm().refresh()
  api.getAccounts.mockClear()

  wrapper = mount(ActionPanel, { props: { tab, accountId } })
  await flushPromises()

  const panel = wrapper
  const amountInput = () => panel.get<HTMLInputElement>('#amount')
  const button = (label: string) => panel.findAll('button').find((b) => b.text() === label)
  const reviewButton = () => panel.get<HTMLButtonElement>('button[type="submit"]')
  const help = () => panel.get('[data-testid="amount-help"]').text()

  async function enter(value: string) {
    await amountInput().setValue(value)
  }
  async function review() {
    await panel.get('form').trigger('submit')
  }
  async function confirm() {
    await button('Confirm')!.trigger('click')
    await flushPromises()
  }

  return { api, ApiError, panel, amountInput, button, reviewButton, help, enter, review, confirm }
}

describe('review button', () => {
  test.each(['', 'abc', '0', '1.234'])('is disabled for amount %j', async (value) => {
    const { enter, reviewButton } = await mountPanel('deposit')
    await enter(value)
    expect(reviewButton().element.disabled).toBe(true)
  })

  test('is enabled for a valid amount', async () => {
    const { enter, reviewButton } = await mountPanel('deposit')
    await enter('20')
    expect(reviewButton().element.disabled).toBe(false)
  })

  test('explains a malformed amount inline', async () => {
    const { enter, help, amountInput } = await mountPanel('deposit')
    await enter('abc')
    expect(help()).toContain('Enter an amount greater than zero')
    expect(amountInput().attributes('aria-invalid')).toBe('true')
  })
})

describe('inline validation', () => {
  test('a withdrawal above the balance is blocked with the shortfall', async () => {
    const { enter, help, reviewButton } = await mountPanel('withdraw')
    await enter('150')
    expect(help()).toContain('$50.00 more than is available')
    expect(reviewButton().element.disabled).toBe(true)
  })

  test('a deposit above the balance is allowed', async () => {
    const { enter, reviewButton } = await mountPanel('deposit')
    await enter('150')
    expect(reviewButton().element.disabled).toBe(false)
  })

  test('shows the projected balance for a valid amount', async () => {
    const { enter, help } = await mountPanel('withdraw')
    await enter('30')
    expect(help()).toBe('New balance: $70.00')
  })

  test('shows the available balance before an amount is entered', async () => {
    const { help } = await mountPanel('withdraw')
    expect(help()).toBe('Available: $100.00')
  })
})

describe('quick amounts', () => {
  test('a chip fills the amount', async () => {
    const { button, amountInput } = await mountPanel('deposit')
    await button('$50')!.trigger('click')
    expect(amountInput().element.value).toBe('50')
  })

  test('All fills the full balance for a withdrawal', async () => {
    const { button, amountInput } = await mountPanel('withdraw')
    await button('All')!.trigger('click')
    expect(amountInput().element.value).toBe('100.00')
  })

  test('a deposit has no All chip', async () => {
    const { button } = await mountPanel('deposit')
    expect(button('All')).toBeUndefined()
  })
})

describe('deposit and withdraw', () => {
  test('reviewing does not call the API', async () => {
    const { api, enter, review, panel } = await mountPanel('deposit')
    await enter('20')

    await review()

    expect(panel.get('[data-testid="review"]').text()).toContain('$20.00')
    expect(api.deposit).not.toHaveBeenCalled()
  })

  test('Back returns to the form and keeps the amount', async () => {
    const { enter, review, button, amountInput } = await mountPanel('deposit')
    await enter('20')
    await review()

    await button('Back')!.trigger('click')

    expect(amountInput().element.value).toBe('20')
  })

  test('a confirmed deposit shows a receipt with the new balance', async () => {
    const { api, enter, review, confirm, panel } = await mountPanel('deposit')
    api.deposit.mockResolvedValue(ledgerRow({ accountId: 'chk', amount: 1234.5, balanceAfter: 1334.5 }))
    await enter('1234.5')
    await review()

    await confirm()

    expect(api.deposit).toHaveBeenCalledWith('chk', 1234.5)
    const receipt = panel.get('[data-testid="receipt"]').text()
    expect(receipt).toContain('Deposit complete')
    expect(receipt).toContain('$1,234.50 deposited to Checking')
    expect(receipt).toContain('$1,334.50')
  })

  test('a confirmed withdrawal names the amount and account', async () => {
    const { api, enter, review, confirm, panel } = await mountPanel('withdraw', 'sav')
    api.withdraw.mockResolvedValue(ledgerRow({ accountId: 'sav', type: 'Withdrawal', amount: 50, balanceAfter: 450 }))
    await enter('50')
    await review()

    await confirm()

    expect(api.withdraw).toHaveBeenCalledWith('sav', 50)
    expect(panel.get('[data-testid="receipt"]').text()).toContain('$50.00 withdrawn from Savings')
  })

  test('Make another transaction returns to an empty form', async () => {
    const { api, enter, review, confirm, button, amountInput } = await mountPanel('deposit')
    api.deposit.mockResolvedValue(ledgerRow({ accountId: 'chk', balanceAfter: 120 }))
    await enter('20')
    await review()
    await confirm()

    await button('Make another transaction')!.trigger('click')

    expect(amountInput().element.value).toBe('')
  })

  test('Confirm is disabled while the call is pending', async () => {
    const { api, enter, review, button } = await mountPanel('deposit')
    const call = deferred<Transaction>()
    api.deposit.mockReturnValue(call.promise)
    await enter('20')
    await review()

    await button('Confirm')!.trigger('click')
    expect((button('Confirm')!.element as HTMLButtonElement).disabled).toBe(true)

    call.resolve(ledgerRow({ accountId: 'chk', balanceAfter: 120 }))
    await flushPromises()
  })
})

describe('transfer', () => {
  test('the destination is the other account', async () => {
    const { panel } = await mountPanel('transfer', 'chk')
    expect(panel.get('[data-testid="transfer-from"]').text()).toBe('Checking')
    expect(panel.get('[data-testid="transfer-to"]').text()).toBe('Savings')
  })

  test('swap selects the other account as the source', async () => {
    const { panel } = await mountPanel('transfer', 'chk')

    await panel.get('button[aria-label="Swap accounts"]').trigger('click')

    expect(panel.emitted('update:accountId')?.[0]).toEqual(['sav'])
  })

  test('review names both accounts and nothing is sent until Confirm', async () => {
    const { api, enter, review, panel } = await mountPanel('transfer', 'chk')
    await enter('75')

    await review()

    const text = panel.get('[data-testid="review"]').text()
    expect(text).toContain('Checking')
    expect(text).toContain('Savings')
    expect(text).toContain('$75.00')
    expect(api.transfer).not.toHaveBeenCalled()
  })

  test('Confirm calls the API and shows the receipt', async () => {
    const { api, enter, review, confirm, panel } = await mountPanel('transfer', 'chk')
    api.transfer.mockResolvedValue(
      transferReceipt({ accountId: 'chk', balanceAfter: 25 }, { accountId: 'sav', balanceAfter: 575 }, 75),
    )
    await enter('75')
    await review()

    await confirm()

    expect(api.transfer).toHaveBeenCalledWith('chk', 'sav', 75)
    expect(panel.get('[data-testid="receipt"]').text()).toContain('$75.00 moved from Checking to Savings')
  })
})

describe('error reporting', () => {
  async function failWithdraw(error: (e: { ApiError: typeof import('@/api/atmClient').ApiError }) => unknown) {
    const ctx = await mountPanel('withdraw')
    ctx.api.withdraw.mockRejectedValue(error(ctx))
    await ctx.enter('20')
    await ctx.review()
    await ctx.confirm()
    return ctx
  }

  test('422 reports insufficient funds with the server detail and keeps the amount', async () => {
    const { panel, amountInput } = await failWithdraw(
      ({ ApiError }) => new ApiError(422, 'Unprocessable', 'Balance is $5.00.'),
    )

    const alert = panel.get('[role="alert"]').text()
    expect(alert).toContain('Insufficient funds')
    expect(alert).toContain('Balance is $5.00.')
    expect(amountInput().element.value).toBe('20')
  })

  test('409 reports a conflict and re-fetches accounts', async () => {
    const { api, panel } = await failWithdraw(({ ApiError }) => new ApiError(409, 'Conflict', null))

    expect(panel.get('[role="alert"]').text()).toContain('The balance changed while you were working')
    expect(api.getAccounts).toHaveBeenCalledTimes(1)
  })

  test('a non-ApiError reports a connection problem', async () => {
    const { panel, api } = await failWithdraw(() => new TypeError('Failed to fetch'))

    expect(panel.get('[role="alert"]').text()).toContain('Could not reach the server')
    expect(api.getAccounts).not.toHaveBeenCalled()
  })

  test('any other ApiError reports its title and detail', async () => {
    const { panel } = await failWithdraw(({ ApiError }) => new ApiError(404, 'Account not found', 'No account chk.'))

    const alert = panel.get('[role="alert"]').text()
    expect(alert).toContain('Account not found')
    expect(alert).toContain('No account chk.')
  })
})
