const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

// Amounts are doubles on the wire, so arithmetic is done in whole cents to keep
// 0.1 + 0.2 === 0.3 and to avoid half-cent rounding surprises in formatting.
// toPrecision strips representation error (1.005 * 100 is 100.49999999999999) before rounding.
export const toCents = (amount: number) => Math.round(Number((amount * 100).toPrecision(15)))
export const fromCents = (cents: number) => cents / 100

export const addMoney = (a: number, b: number) => fromCents(toCents(a) + toCents(b))
export const subtractMoney = (a: number, b: number) => fromCents(toCents(a) - toCents(b))
export const sumMoney = (amounts: readonly number[]) => fromCents(amounts.reduce((sum, a) => sum + toCents(a), 0))

export const formatMoney = (amount: number) => usd.format(fromCents(toCents(amount)))

const AMOUNT = /^\d+(\.\d{1,2})?$/

export function parseAmount(input: string): number | null {
  const text = input.trim()
  if (!AMOUNT.test(text)) return null
  const value = fromCents(toCents(Number(text)))
  return value > 0 ? value : null
}

// Keeps what a user types or pastes to at most two decimal places ("0.03333" becomes "0.03").
export const limitDecimals = (text: string) => text.replace(/(\.\d{2})\d+/, '$1')
