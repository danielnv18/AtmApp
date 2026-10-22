const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export const formatMoney = (amount: number) => usd.format(amount)
