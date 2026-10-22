<script setup lang="ts">
import { onMounted } from 'vue'
import { useAtm } from '@/composables/useAtm'
import AccountCards from '@/components/AccountCards.vue'
import ActionPanel from '@/components/ActionPanel.vue'
import LedgerTable from '@/components/LedgerTable.vue'

const { accounts, error, ledgerAccountId, transactions, refresh, selectLedgerAccount, execute } = useAtm()

onMounted(refresh)
</script>

<template>
  <div class="bg-muted/40 min-h-svh">
    <main class="mx-auto grid max-w-5xl gap-6 px-4 py-8">
      <header>
        <h1 class="text-2xl font-semibold tracking-tight">ATM</h1>
      </header>

      <p v-if="error" class="text-destructive text-sm">{{ error }}</p>

      <template v-if="accounts.length > 0">
        <AccountCards :accounts="accounts" />
        <ActionPanel :accounts="accounts" :submit="execute" />
        <LedgerTable
          :accounts="accounts"
          :transactions="transactions"
          :account-id="ledgerAccountId"
          @update:account-id="selectLedgerAccount"
        />
      </template>
    </main>
  </div>
</template>
