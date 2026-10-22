<script setup lang="ts">
import type { Account } from '@/types'
import { formatMoney } from '@/lib/money'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

defineProps<{ accounts: readonly Account[] }>()
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2">
    <Card v-for="a in accounts" :key="a.id">
      <CardHeader>
        <CardDescription>{{ a.name }}</CardDescription>
        <CardTitle class="text-3xl font-semibold tabular-nums">{{ formatMoney(a.balance) }}</CardTitle>
      </CardHeader>
    </Card>
  </div>
</template>
