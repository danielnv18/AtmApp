<script setup lang="ts">
import type { Account, Transaction } from '@/types'
import { formatMoney } from '@/lib/money'
import AccountSelect from './AccountSelect.vue'
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

defineProps<{ accounts: readonly Account[]; transactions: readonly Transaction[] }>()
const accountId = defineModel<string | undefined>('accountId')

// A transfer is a Withdrawal/Deposit pair sharing a correlationId.
function labelOf(t: Transaction) {
  if (!t.correlationId) return t.type
  return t.type === 'Deposit' ? 'Transfer In' : 'Transfer Out'
}

const timestamp = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'medium' })
</script>

<template>
  <Card>
    <CardHeader class="flex flex-wrap items-end justify-between gap-4">
      <CardTitle>Ledger</CardTitle>
      <div class="w-full sm:w-64">
        <AccountSelect v-model="accountId" label="Account" :accounts="accounts" />
      </div>
    </CardHeader>
    <CardContent>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Timestamp</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Description</TableHead>
            <TableHead class="text-right">Amount</TableHead>
            <TableHead class="text-right">Balance After</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in transactions" :key="t.id">
            <TableCell>{{ timestamp.format(new Date(t.timestampUtc)) }}</TableCell>
            <TableCell>{{ labelOf(t) }}</TableCell>
            <TableCell>{{ t.description }}</TableCell>
            <TableCell class="text-right tabular-nums">{{ formatMoney(t.amount) }}</TableCell>
            <TableCell class="text-right tabular-nums">{{ formatMoney(t.balanceAfter) }}</TableCell>
          </TableRow>
          <TableEmpty v-if="transactions.length === 0" :colspan="5">No transactions yet.</TableEmpty>
        </TableBody>
      </Table>
    </CardContent>
  </Card>
</template>
