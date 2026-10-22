<script setup lang="ts">
import type { Account, Transaction } from '@/types'
import AccountCards from '@/components/AccountCards.vue'
import ActionPanel from '@/components/ActionPanel.vue'
import LedgerTable from '@/components/LedgerTable.vue'

const accounts: Account[] = [
  { id: 'checking', name: 'Checking', balance: 1000 },
  { id: 'savings', name: 'Savings', balance: 500 },
]

const transactions: Transaction[] = [
  {
    id: '1',
    type: 'Deposit',
    amount: 1000,
    balanceAfter: 1000,
    correlationId: null,
    timestampUtc: '2026-10-22T16:00:00Z',
    description: 'Opening deposit',
  },
]
</script>

<template>
  <div class="bg-muted/40 min-h-svh">
    <main class="mx-auto grid max-w-5xl gap-6 px-4 py-8">
      <header>
        <h1 class="text-2xl font-semibold tracking-tight">ATM</h1>
      </header>

      <AccountCards :accounts="accounts" />
      <ActionPanel :accounts="accounts" />
      <LedgerTable :accounts="accounts" :transactions="transactions" />
    </main>
  </div>
</template>
