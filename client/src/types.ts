export type Account = { id: string; name: string; balance: number }

export type Transaction = {
  id: string
  type: 'Deposit' | 'Withdrawal'
  amount: number
  balanceAfter: number
  correlationId: string | null
  timestampUtc: string
  description: string | null
}
