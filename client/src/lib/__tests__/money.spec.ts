import { describe, expect, test } from 'vitest'
import { formatMoney, parseAmount } from '@/lib/money'

describe('parseAmount', () => {
  test.each([
    ['20', 20],
    [' 20 ', 20],
    ['0.01', 0.01],
    ['10.5', 10.5],
    ['10.50', 10.5],
  ])('accepts %j as %d', (input, expected) => {
    expect(parseAmount(input)).toBe(expected)
  })

  test.each(['', '0', '0.00', '-5', '1.234', '1,000', '.5', '5.', 'abc', '1e3'])(
    'rejects %j',
    (input) => {
      expect(parseAmount(input)).toBeNull()
    },
  )

  test('client/server mismatch: accepts amounts above the server max of 1,000,000,000.00', () => {
    expect(parseAmount('2000000000')).toBe(2_000_000_000)
  })
})

describe('formatMoney', () => {
  test.each([
    [0, '$0.00'],
    [5, '$5.00'],
    [10.5, '$10.50'],
    [1234567.891, '$1,234,567.89'],
  ])('formats %d as %s', (amount, expected) => {
    expect(formatMoney(amount)).toBe(expected)
  })
})
