import { describe, expect, test } from 'vitest'
import { addMoney, formatMoney, fromCents, parseAmount, subtractMoney, sumMoney, toCents } from '@/lib/money'

describe('parseAmount', () => {
  test.each([
    ['20', 20],
    [' 20 ', 20],
    ['0.01', 0.01],
    ['10.5', 10.5],
    ['10.50', 10.5],
    ['0.07', 0.07],
    ['19.99', 19.99],
    ['1000000000.00', 1_000_000_000],
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

describe('formatMoney rounding', () => {
  test.each([
    [1.005, '$1.01'],
    [1.255, '$1.26'],
    [8.345, '$8.35'],
    [0.1 + 0.2, '$0.30'],
    [0.3 - 0.1, '$0.20'],
    [-12.5, '-$12.50'],
    [1e9, '$1,000,000,000.00'],
  ])('formats %d as %s', (amount, expected) => {
    expect(formatMoney(amount)).toBe(expected)
  })
})

describe('cent arithmetic', () => {
  test('toCents and fromCents round trip awkward doubles', () => {
    expect(toCents(0.07)).toBe(7)
    expect(toCents(1.15)).toBe(115)
    expect(toCents(0.1 + 0.2)).toBe(30)
    expect(fromCents(toCents(19.99))).toBe(19.99)
  })

  test('addMoney is exact where doubles are not', () => {
    expect(0.1 + 0.2).not.toBe(0.3)
    expect(addMoney(0.1, 0.2)).toBe(0.3)
    expect(addMoney(1.1, 2.2)).toBe(3.3)
  })

  test('subtractMoney is exact where doubles are not', () => {
    expect(0.3 - 0.1).not.toBe(0.2)
    expect(subtractMoney(0.3, 0.1)).toBe(0.2)
    expect(subtractMoney(5, 5.01)).toBe(-0.01)
  })

  test('sumMoney adds many amounts without drift', () => {
    expect(sumMoney(Array.from({ length: 10 }, () => 0.1))).toBe(1)
    expect(sumMoney([])).toBe(0)
    expect(sumMoney([100.1, 200.2])).toBe(300.3)
  })
})
