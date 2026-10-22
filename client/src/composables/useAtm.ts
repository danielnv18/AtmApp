import { ref } from 'vue'
import { atmClient } from '@/api/atmClient'
import type { Account, OperationRequest, Transaction } from '@/types'

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e))

export function useAtm() {
  const accounts = ref<Account[]>([])
  const error = ref<string | null>(null)
  const ledgerAccountId = ref<string>()
  const transactions = ref<Transaction[]>([])

  async function loadHistory() {
    const id = ledgerAccountId.value
    if (id === undefined) return
    const page = await atmClient.getHistory(id)
    // Ignore a slow response for an account that is no longer selected.
    if (ledgerAccountId.value === id) transactions.value = page.items
  }

  async function refresh() {
    try {
      accounts.value = await atmClient.getAccounts()
      ledgerAccountId.value ??= accounts.value[0]?.id
      await loadHistory()
      error.value = null
    } catch (e) {
      error.value = messageOf(e)
    }
  }

  async function selectLedgerAccount(id: string | undefined) {
    ledgerAccountId.value = id
    transactions.value = []
    try {
      await loadHistory()
    } catch (e) {
      error.value = messageOf(e)
    }
  }

  // Errors propagate so the form that submitted the operation can show them.
  async function execute({ op, accountId, toAccountId, amount }: OperationRequest) {
    if (op === 'deposit') await atmClient.deposit(accountId, amount)
    else if (op === 'withdraw') await atmClient.withdraw(accountId, amount)
    else if (toAccountId !== undefined) await atmClient.transfer(accountId, toAccountId, amount)
    await refresh()
  }

  return { accounts, error, ledgerAccountId, transactions, refresh, selectLedgerAccount, execute }
}
