<script setup lang="ts">
import type { Account, Operation } from '@/types'
import { formatMoney } from '@/lib/money'
import { Button } from '@/components/ui/button'
import { Card, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'

defineProps<{ accounts: readonly Account[] }>()
const emit = defineEmits<{ start: [operation: Operation, accountId: string] }>()

const ACTIONS: { operation: Operation; label: string }[] = [
  { operation: 'deposit', label: 'Deposit' },
  { operation: 'withdraw', label: 'Withdraw' },
  { operation: 'transfer', label: 'Transfer' },
]
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2">
    <Card v-for="a in accounts" :key="a.id" data-testid="account-card" :data-account-name="a.name">
      <CardHeader>
        <CardDescription>{{ a.name }}</CardDescription>
        <CardTitle class="text-3xl font-semibold tabular-nums" data-testid="balance">
          {{ formatMoney(a.balance) }}
        </CardTitle>
      </CardHeader>
      <CardFooter class="flex flex-wrap gap-2">
        <Button
          v-for="action in ACTIONS"
          :key="action.operation"
          variant="outline"
          size="sm"
          @click="emit('start', action.operation, a.id)"
        >
          {{ action.label }}
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>
