const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export const formatMoney = (amount: number) => usd.format(amount)

const AMOUNT = /^\d+(\.\d{1,2})?$/

export function parseAmount(input: string): number | null {
  const text = input.trim()
  if (!AMOUNT.test(text)) return null
  const value = Number(text)
  return value > 0 ? value : null
}
