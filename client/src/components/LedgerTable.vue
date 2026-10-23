<script setup lang="ts">
import { computed } from 'vue'
import type { Transaction } from '@/types'
import { useAtm } from '@/composables/useAtm'
import { formatMoney } from '@/lib/money'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

const { accounts, ledgerAccountId, history, historyError } = useAtm()

const TYPE_META: Record<Transaction['type'], { label: string; credit: boolean }> = {
  Deposit: { label: 'Deposit', credit: true },
  Withdrawal: { label: 'Withdrawal', credit: false },
  TransferIn: { label: 'Transfer in', credit: true },
  TransferOut: { label: 'Transfer out', credit: false },
}

type Row = Transaction & { meta: (typeof TYPE_META)[Transaction['type']] }

const accountName = computed(() => accounts.value.find((a) => a.id === ledgerAccountId.value)?.name)

const clock = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' })
const dayFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })

function dayLabel(date: Date) {
  const now = new Date()
  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (date.toDateString() === now.toDateString()) return 'Today'
  if (date.toDateString() === yesterday.toDateString()) return 'Yesterday'
  return dayFormat.format(date)
}

// The API returns newest first, so consecutive rows of one day form a group.
const groups = computed(() => {
  const result: { key: string; label: string; rows: Row[] }[] = []
  for (const t of history.value) {
    const date = new Date(t.timestampUtc)
    const key = date.toDateString()
    const row = { ...t, meta: TYPE_META[t.type] }
    const last = result.at(-1)
    if (last?.key === key) last.rows.push(row)
    else result.push({ key, label: dayLabel(date), rows: [row] })
  }
  return result
})
</script>

<template>
  <Card data-testid="ledger">
    <CardHeader>
      <CardTitle class="text-teal text-2xl">Activity</CardTitle>
      <CardDescription v-if="accountName">{{ accountName }}</CardDescription>
    </CardHeader>
    <CardContent class="grid gap-4">
      <Alert v-if="historyError" variant="destructive">
        <AlertTitle>Could not load history</AlertTitle>
        <AlertDescription>{{ historyError }}</AlertDescription>
      </Alert>
      <p v-else-if="groups.length === 0" class="text-muted-foreground py-6 text-center text-sm">
        No transactions yet. Make a deposit to get started.
      </p>
      <section v-for="g in groups" :key="g.key">
        <h3 class="font-heading text-orange text-sm font-semibold tracking-wide uppercase">{{ g.label }}</h3>
        <ul class="divide-y">
          <li v-for="t in g.rows" :key="t.id" class="flex items-center justify-between gap-3 py-2.5" data-testid="ledger-row">
            <div class="min-w-0">
              <p class="text-sm font-medium" data-testid="ledger-type">{{ t.meta.label }}</p>
              <p class="text-muted-foreground truncate text-xs">
                {{ clock.format(new Date(t.timestampUtc)) }}
                <template v-if="t.description">· <span data-testid="ledger-description">{{ t.description }}</span></template>
              </p>
            </div>
            <div class="text-right">
              <p
                class="text-sm font-medium tabular-nums"
                :class="t.meta.credit ? 'text-lime-dark' : 'text-destructive'"
                data-testid="ledger-amount"
              >
                {{ t.meta.credit ? '+' : '−' }}{{ formatMoney(t.amount) }}
              </p>
              <p class="text-muted-foreground text-xs tabular-nums" data-testid="ledger-balance">
                {{ formatMoney(t.balanceAfter) }}
              </p>
            </div>
          </li>
        </ul>
      </section>
    </CardContent>
  </Card>
</template>
