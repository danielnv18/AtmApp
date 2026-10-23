import { readonly, ref } from 'vue'
import { atmClient } from '@/api/atmClient'
import type { Account, Operation, Transaction } from '@/types'

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e))

const accounts = ref<Account[]>([])
const accountsError = ref<string | null>(null)
const ledgerAccountId = ref<string | null>(null)
const history = ref<Transaction[]>([])
const historyError = ref<string | null>(null)
const pending = ref<Operation | null>(null)

// Only the latest history load may write, so a slow response or error for an
// account the user has since left is dropped.
let historyTicket = 0

async function loadHistory() {
  const id = ledgerAccountId.value
  if (id === null) return
  const ticket = ++historyTicket
  try {
    const page = await atmClient.getHistory(id)
    if (ticket !== historyTicket) return
    history.value = page.items
    historyError.value = null
  } catch (e) {
    if (ticket !== historyTicket) return
    historyError.value = messageOf(e)
  }
}

async function refresh() {
  try {
    accounts.value = await atmClient.getAccounts()
    accountsError.value = null
  } catch (e) {
    accountsError.value = messageOf(e)
    return
  }
  ledgerAccountId.value ??= accounts.value[0]?.id ?? null
  await loadHistory()
}

async function showHistory(accountId: string) {
  ledgerAccountId.value = accountId
  history.value = []
  historyError.value = null
  await loadHistory()
}

function applyBalances(rows: Transaction[]) {
  const balanceAfter = new Map(rows.map((r) => [r.accountId, r.balanceAfter]))
  accounts.value = accounts.value.map((a) => {
    const balance = balanceAfter.get(a.id)
    return balance === undefined ? a : { ...a, balance }
  })
}

async function run(operation: Operation, call: () => Promise<Transaction[]>) {
  pending.value = operation
  try {
    applyBalances(await call())
    await loadHistory()
  } finally {
    pending.value = null
  }
}

const deposit = (accountId: string, amount: number) =>
  run('deposit', async () => [await atmClient.deposit(accountId, amount)])
const withdraw = (accountId: string, amount: number) =>
  run('withdraw', async () => [await atmClient.withdraw(accountId, amount)])
const transfer = (fromAccountId: string, toAccountId: string, amount: number) =>
  run('transfer', async () => {
    const receipt = await atmClient.transfer(fromAccountId, toAccountId, amount)
    return [receipt.transferOut, receipt.transferIn]
  })

export function useAtm() {
  return {
    accounts: readonly(accounts),
    accountsError: readonly(accountsError),
    ledgerAccountId: readonly(ledgerAccountId),
    history: readonly(history),
    historyError: readonly(historyError),
    pending: readonly(pending),
    refresh,
    showHistory,
    deposit,
    withdraw,
    transfer,
  }
}
