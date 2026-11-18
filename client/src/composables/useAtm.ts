import { computed, readonly, ref } from 'vue'
import { atmClient } from '@/api/atmClient'
import type { Account, Operation, Transaction } from '@/types'

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e))

const accounts = ref<Account[]>([])
const accountsError = ref<string | null>(null)
const ledgerAccountId = ref<string | null>(null)
const history = ref<Transaction[]>([])
const historyError = ref<string | null>(null)
const pending = ref<Operation | null>(null)
const loadingHistory = ref(false)

// The API caps a page at 200 rows but never the number of pages, so any size here can
// be paged through to the oldest row.
export const PAGE_SIZES = [10, 25, 50, 100] as const
export type PageSize = (typeof PAGE_SIZES)[number]
const pageSize = ref<PageSize>(PAGE_SIZES[0])

// cursors[i] is the cursor that fetches page i. The API pages forward only, so the
// cursor each page returns is remembered, which is what lets the user step back.
const cursors = ref<(string | null)[]>([null])
const pageIndex = ref(0)
const nextCursor = ref<string | null>(null)
const page = computed(() => pageIndex.value + 1)
const hasNext = computed(() => nextCursor.value !== null)
const hasPrevious = computed(() => pageIndex.value > 0)

function resetPaging() {
  cursors.value = [null]
  pageIndex.value = 0
  nextCursor.value = null
}

// Only the latest history load may write, so a slow response or error for an
// account the user has since left is dropped.
let historyTicket = 0

type LoadResult = 'ok' | 'error' | 'stale'

async function loadHistory(): Promise<LoadResult> {
  const id = ledgerAccountId.value
  if (id === null) return 'ok'
  const ticket = ++historyTicket
  loadingHistory.value = true
  try {
    const result = await atmClient.getHistory(id, pageSize.value, cursors.value[pageIndex.value])
    if (ticket !== historyTicket) return 'stale'
    history.value = result.items
    nextCursor.value = result.nextCursor
    historyError.value = null
    return 'ok'
  } catch (e) {
    if (ticket !== historyTicket) return 'stale'
    historyError.value = messageOf(e)
    return 'error'
  } finally {
    if (ticket === historyTicket) loadingHistory.value = false
  }
}

async function nextPage() {
  if (nextCursor.value === null || loadingHistory.value) return
  const before = { cursors: cursors.value, index: pageIndex.value }
  cursors.value = [...cursors.value.slice(0, pageIndex.value + 1), nextCursor.value]
  pageIndex.value++
  // A failed load must not leave the pager pointing at a page that never rendered.
  if ((await loadHistory()) === 'error') {
    cursors.value = before.cursors
    pageIndex.value = before.index
  }
}

async function previousPage() {
  if (pageIndex.value === 0 || loadingHistory.value) return
  pageIndex.value--
  if ((await loadHistory()) === 'error') pageIndex.value++
}

async function firstPage() {
  if (pageIndex.value === 0 || loadingHistory.value) return
  const before = pageIndex.value
  pageIndex.value = 0
  if ((await loadHistory()) === 'error') pageIndex.value = before
}

async function setPageSize(size: PageSize) {
  if (size === pageSize.value) return
  pageSize.value = size
  // Cursors are row positions, but page boundaries move with the size, so start over.
  resetPaging()
  await loadHistory()
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
  resetPaging()
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
    // New rows land on the newest page, so go back to it.
    resetPaging()
    await loadHistory()
  } finally {
    pending.value = null
  }
}

const deposit = (accountId: string, amount: number, idempotencyKey: string) =>
  run('deposit', async () => [await atmClient.deposit(accountId, amount, idempotencyKey)])
const withdraw = (accountId: string, amount: number, idempotencyKey: string) =>
  run('withdraw', async () => [await atmClient.withdraw(accountId, amount, idempotencyKey)])
const transfer = (fromAccountId: string, toAccountId: string, amount: number, idempotencyKey: string) =>
  run('transfer', async () => {
    const receipt = await atmClient.transfer(fromAccountId, toAccountId, amount, idempotencyKey)
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
    loadingHistory: readonly(loadingHistory),
    page,
    pageSize: readonly(pageSize),
    hasNext,
    hasPrevious,
    nextPage,
    previousPage,
    firstPage,
    setPageSize,
    refresh,
    showHistory,
    deposit,
    withdraw,
    transfer,
  }
}
