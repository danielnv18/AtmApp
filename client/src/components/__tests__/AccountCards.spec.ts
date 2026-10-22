import { describe, expect, test } from 'vitest'
import { mount } from '@vue/test-utils'
import AccountCards from '@/components/AccountCards.vue'
import { account } from '@/__tests__/fixtures'

const accounts = [
  account({ id: 'chk', name: 'Checking', balance: 1234.5 }),
  account({ id: 'sav', name: 'Savings', balance: 0 }),
]

describe('AccountCards', () => {
  test('renders one card per account with its name and formatted balance', () => {
    const w = mount(AccountCards, { props: { accounts } })

    const cards = w.findAll('[data-testid="account-card"]')
    expect(cards.map((c) => [c.attributes('data-account-name'), c.get('[data-testid="balance"]').text()])).toEqual([
      ['Checking', '$1,234.50'],
      ['Savings', '$0.00'],
    ])
  })

  test.each([
    ['Deposit', 'deposit'],
    ['Withdraw', 'withdraw'],
    ['Transfer', 'transfer'],
  ])('clicking %s emits start with the operation and account id', async (label, operation) => {
    const w = mount(AccountCards, { props: { accounts } })
    const savingsCard = w.findAll('[data-testid="account-card"]')[1]!

    await savingsCard.findAll('button').find((b) => b.text() === label)!.trigger('click')

    expect(w.emitted('start')).toEqual([[operation, 'sav']])
  })
})
