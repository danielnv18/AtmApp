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

vi.mock('vue-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))

let wrapper: VueWrapper | undefined

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
})

const checking = () => account({ id: 'chk', name: 'Checking', balance: 100 })
const savings = () => account({ id: 'sav', name: 'Savings', balance: 500 })

// useAtm keeps module-level state, so every test gets a fresh module graph.
async function mountPanel(tab: Operation, accountId = 'chk') {
  vi.resetModules()
  const { atmClient, ApiError } = await import('@/api/atmClient')
  const { toast } = await import('vue-sonner')
  const { useAtm } = await import('@/composables/useAtm')
  const { default: ActionPanel } = await import('@/components/ActionPanel.vue')

  const api = vi.mocked(atmClient)
  api.getAccounts.mockResolvedValue([checking(), savings()])
  api.getHistory.mockResolvedValue({ items: [], nextCursor: null })
  await useAtm().refresh()
  api.getAccounts.mockClear()

  wrapper = mount(ActionPanel, { props: { tab, accountId }, attachTo: document.body })
  await flushPromises()

  const panel = wrapper
  const amountInput = () => panel.get<HTMLInputElement>(`#${tab}-amount`)
  const submit = () => panel.get<HTMLButtonElement>('button[type="submit"]')
  const bodyButton = (label: string) =>
    [...document.body.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)
  const dialog = () => document.body.querySelector('[role="dialog"]')

  async function enter(value: string) {
    await amountInput().setValue(value)
  }
  async function submitForm() {
    await panel.get('form').trigger('submit')
    await flushPromises()
  }
  async function chooseToAccount(id: string) {
    // Reka Select does not open in jsdom, so drive the "To" AccountSelect's v-model directly.
    const selects = panel.findAllComponents({ name: 'AccountSelect' })
    await selects[1]!.vm.$emit('update:modelValue', id)
  }

  return { api, ApiError, toast: vi.mocked(toast), panel, amountInput, submit, bodyButton, dialog, enter, submitForm, chooseToAccount }
}

describe('submit button', () => {
  test.each(['', 'abc', '0', '1.234'])('is disabled for amount %j', async (value) => {
    const { enter, submit } = await mountPanel('deposit')
    await enter(value)
    expect(submit().element.disabled).toBe(true)
  })

  test('is enabled for a valid amount', async () => {
    const { enter, submit } = await mountPanel('deposit')
    await enter('20')
    expect(submit().element.disabled).toBe(false)
  })

  test('is disabled while an operation is pending', async () => {
    const { api, enter, submit, submitForm } = await mountPanel('deposit')
    const call = deferred<Transaction>()
    api.deposit.mockReturnValue(call.promise)
    await enter('20')

    await submitForm()
    expect(submit().element.disabled).toBe(true)

    call.resolve(ledgerRow({ accountId: 'chk', balanceAfter: 120 }))
    await flushPromises()
    await enter('5')
    expect(submit().element.disabled).toBe(false)
  })
})

describe('deposit and withdraw', () => {
  test('a deposit toasts the amount and account name and clears the amount', async () => {
    const { api, toast, enter, submitForm, amountInput } = await mountPanel('deposit')
    api.deposit.mockResolvedValue(ledgerRow({ accountId: 'chk', amount: 1234.5, balanceAfter: 1334.5 }))
    await enter('1234.5')

    await submitForm()

    expect(api.deposit).toHaveBeenCalledWith('chk', 1234.5)
    expect(toast.success).toHaveBeenCalledWith('Deposited $1,234.50 to Checking')
    expect(amountInput().element.value).toBe('')
  })

  test('a withdrawal toasts the amount and account name', async () => {
    const { api, toast, enter, submitForm } = await mountPanel('withdraw', 'sav')
    api.withdraw.mockResolvedValue(ledgerRow({ accountId: 'sav', type: 'Withdrawal', amount: 50, balanceAfter: 450 }))
    await enter('50')

    await submitForm()

    expect(api.withdraw).toHaveBeenCalledWith('sav', 50)
    expect(toast.success).toHaveBeenCalledWith('Withdrew $50.00 from Savings')
  })

  test('the quick-withdraw $20 button fills the amount', async () => {
    const { panel, amountInput } = await mountPanel('withdraw')
    const quick = panel.findAll('button').find((b) => b.text() === '$20')!

    await quick.trigger('click')

    expect(amountInput().element.value).toBe('20')
  })
})

describe('transfer confirmation', () => {
  async function readyTransfer() {
    const ctx = await mountPanel('transfer', 'chk')
    await ctx.chooseToAccount('sav')
    await ctx.enter('75')
    await ctx.submitForm()
    return ctx
  }

  test('submitting opens the confirm dialog without calling the API', async () => {
    const { api, dialog } = await readyTransfer()

    expect(dialog()?.textContent).toContain('Move $75.00 from Checking to Savings?')
    expect(api.transfer).not.toHaveBeenCalled()
  })

  test('Confirm calls the API, toasts and closes the dialog', async () => {
    const { api, toast, bodyButton, dialog } = await readyTransfer()
    api.transfer.mockResolvedValue(
      transferReceipt({ accountId: 'chk', balanceAfter: 25 }, { accountId: 'sav', balanceAfter: 575 }, 75),
    )

    bodyButton('Confirm transfer')!.click()
    await flushPromises()

    expect(api.transfer).toHaveBeenCalledWith('chk', 'sav', 75)
    expect(toast.success).toHaveBeenCalledWith('Transferred $75.00 from Checking to Savings')
    expect(dialog()).toBeNull()
  })

  test('Cancel closes the dialog and calls nothing', async () => {
    const { api, bodyButton, dialog } = await readyTransfer()

    bodyButton('Cancel')!.click()
    await flushPromises()

    expect(dialog()).toBeNull()
    expect(api.transfer).not.toHaveBeenCalled()
  })

  test('a failed transfer closes the dialog and keeps the amount', async () => {
    const { api, ApiError, toast, bodyButton, dialog, amountInput } = await readyTransfer()
    api.transfer.mockRejectedValue(new ApiError(422, 'Insufficient funds', 'Balance is $100.00.'))

    bodyButton('Confirm transfer')!.click()
    await flushPromises()

    expect(toast.error).toHaveBeenCalled()
    expect(dialog()).toBeNull()
    expect(amountInput().element.value).toBe('75')
  })

  test('submit stays disabled until a destination account is chosen', async () => {
    const { enter, submit, chooseToAccount } = await mountPanel('transfer', 'chk')
    await enter('75')
    expect(submit().element.disabled).toBe(true)

    await chooseToAccount('sav')
    expect(submit().element.disabled).toBe(false)
  })
})

describe('error reporting', () => {
  async function failWithdraw(error: (e: { ApiError: typeof import('@/api/atmClient').ApiError }) => unknown) {
    const ctx = await mountPanel('withdraw')
    ctx.api.withdraw.mockRejectedValue(error(ctx))
    await ctx.enter('20')
    await ctx.submitForm()
    return ctx
  }

  test('422 reports insufficient funds with the server detail', async () => {
    const { toast, amountInput } = await failWithdraw(
      ({ ApiError }) => new ApiError(422, 'Unprocessable', 'Balance is $5.00.'),
    )

    expect(toast.error).toHaveBeenCalledWith('Insufficient funds', { description: 'Balance is $5.00.' })
    expect(amountInput().element.value).toBe('20')
  })

  test('409 reports a conflict and re-fetches accounts', async () => {
    const { api, toast } = await failWithdraw(({ ApiError }) => new ApiError(409, 'Conflict', null))

    expect(toast.error).toHaveBeenCalledWith('The balance changed while you were working. Please try again.')
    expect(api.getAccounts).toHaveBeenCalledTimes(1)
  })

  test('a non-ApiError reports a connection problem', async () => {
    const { toast, api } = await failWithdraw(() => new TypeError('Failed to fetch'))

    expect(toast.error).toHaveBeenCalledWith('Could not reach the server. Check your connection and try again.')
    expect(api.getAccounts).not.toHaveBeenCalled()
  })

  test('any other ApiError reports its title and detail', async () => {
    const { toast } = await failWithdraw(({ ApiError }) => new ApiError(404, 'Account not found', 'No account chk.'))

    expect(toast.error).toHaveBeenCalledWith('Account not found', { description: 'No account chk.' })
  })
})
