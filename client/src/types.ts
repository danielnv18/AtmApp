export type Account = { id: string; name: string; balance: number }

export type Transaction = {
  id: string
  accountId: string
  type: 'Deposit' | 'Withdrawal'
  amount: number
  balanceAfter: number
  correlationId: string | null
  timestampUtc: string
  description: string | null
}

export type Operation = 'deposit' | 'withdraw' | 'transfer'

export type OperationRequest = {
  op: Operation
  accountId: string
  toAccountId?: string
  amount: number
}
