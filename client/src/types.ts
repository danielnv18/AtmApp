export type Account = { id: string; name: string; balance: number }

export type TransactionType = 'Deposit' | 'Withdrawal'

export type Transaction = {
  id: string
  accountId: string
  type: TransactionType
  amount: number
  balanceAfter: number
  correlationId: string | null
  timestampUtc: string
  description: string | null
}

export type HistoryPage = { items: Transaction[]; nextCursor: string | null }

export type TransferReceipt = { correlationId: string; withdrawal: Transaction; deposit: Transaction }

export type ProblemDetails = { type?: string; title?: string; status?: number; detail?: string }

export type Operation = 'deposit' | 'withdraw' | 'transfer'
