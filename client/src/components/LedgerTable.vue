<script setup lang="ts">
import { computed } from 'vue'
import type { Transaction } from '@/types'
import { useAtm } from '@/composables/useAtm'
import { formatMoney } from '@/lib/money'
import AccountSelect from './AccountSelect.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge, type BadgeVariants } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

const { accounts, ledgerAccountId, history, historyError, showHistory } = useAtm()

const TYPE_META: Record<Transaction['type'], { label: string; credit: boolean; badge: BadgeVariants['variant'] }> = {
  Deposit: { label: 'Deposit', credit: true, badge: 'secondary' },
  Withdrawal: { label: 'Withdrawal', credit: false, badge: 'outline' },
  TransferIn: { label: 'Transfer In', credit: true, badge: 'secondary' },
  TransferOut: { label: 'Transfer Out', credit: false, badge: 'outline' },
}

const rows = computed(() => history.value.map((t) => ({ ...t, meta: TYPE_META[t.type] })))

const selected = computed({
  get: () => ledgerAccountId.value ?? undefined,
  set: (id) => {
    if (id !== undefined) void showHistory(id)
  },
})

const timestamp = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' })
</script>

<template>
  <Card data-testid="ledger">
    <CardHeader class="flex flex-wrap items-end justify-between gap-4">
      <CardTitle>Ledger</CardTitle>
      <div class="w-full sm:w-64">
        <AccountSelect v-model="selected" label="Ledger account" :accounts="accounts" />
      </div>
    </CardHeader>
    <CardContent class="grid gap-4">
      <Alert v-if="historyError" variant="destructive">
        <AlertTitle>Could not load history</AlertTitle>
        <AlertDescription>{{ historyError }}</AlertDescription>
      </Alert>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Time</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Description</TableHead>
            <TableHead class="text-right">Amount</TableHead>
            <TableHead class="text-right">Balance After</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in rows" :key="t.id" data-testid="ledger-row">
            <TableCell>{{ timestamp.format(new Date(t.timestampUtc)) }}</TableCell>
            <TableCell>
              <Badge :variant="t.meta.badge">{{ t.meta.label }}</Badge>
            </TableCell>
            <TableCell>{{ t.description }}</TableCell>
            <TableCell
              class="text-right tabular-nums"
              :class="t.meta.credit ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'"
              data-testid="ledger-amount"
            >
              {{ t.meta.credit ? '+' : '−' }}{{ formatMoney(t.amount) }}
            </TableCell>
            <TableCell class="text-right tabular-nums" data-testid="ledger-balance">
              {{ formatMoney(t.balanceAfter) }}
            </TableCell>
          </TableRow>
          <TableEmpty v-if="rows.length === 0" :colspan="5">No transactions yet.</TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>
