<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RefreshCwIcon } from '@lucide/vue'
import { useAtm } from '@/composables/useAtm'
import { formatMoney } from '@/lib/money'
import type { Operation } from '@/types'
import ActionPanel from '@/components/ActionPanel.vue'
import LedgerTable from '@/components/LedgerTable.vue'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'

const { accounts, accountsError, ledgerAccountId, refresh, showHistory } = useAtm()

const tab = ref<Operation>('deposit')
const actionAccountId = ref<string>()
const refreshing = ref(false)
const updatedAt = ref<Date>()

const total = computed(() => accounts.value.reduce((sum, a) => sum + a.balance, 0))
const time = new Intl.DateTimeFormat(undefined, { timeStyle: 'short' })

async function load() {
  refreshing.value = true
  try {
    await refresh()
    actionAccountId.value ??= accounts.value[0]?.id
    if (accountsError.value === null) updatedAt.value = new Date()
  } finally {
    refreshing.value = false
  }
}

// The activity list follows whichever account is selected.
watch(actionAccountId, (id) => {
  if (id !== undefined && id !== ledgerAccountId.value) void showHistory(id)
})

onMounted(load)
</script>

<template>
  <div class="bg-background min-h-svh">
    <header class="bg-navy text-white">
      <div class="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-4">
        <h1 class="font-heading text-2xl font-semibold tracking-wide">ATM<span class="text-lime"> Trust</span></h1>
        <div v-if="accounts.length > 0" class="text-right">
          <p class="text-xs text-white/70">Total balance</p>
          <p class="font-heading text-xl font-medium tabular-nums" data-testid="total">{{ formatMoney(total) }}</p>
        </div>
      </div>
      <div class="bg-teal">
        <div class="mx-auto flex max-w-5xl items-center justify-between px-4 py-1.5 text-xs">
          <span>{{ updatedAt ? `Updated ${time.format(updatedAt)}` : 'Loading accounts…' }}</span>
          <Button
            variant="ghost"
            size="xs"
            class="text-white hover:bg-white/15 hover:text-white"
            :disabled="refreshing"
            @click="load"
          >
            <RefreshCwIcon :class="{ 'animate-spin': refreshing }" />
            Refresh
          </Button>
        </div>
      </div>
    </header>

    <main class="mx-auto grid max-w-5xl gap-6 px-4 py-6">
      <Alert v-if="accountsError" variant="destructive">
        <AlertTitle>Could not load accounts</AlertTitle>
        <AlertDescription>{{ accountsError }}</AlertDescription>
        <AlertAction>
          <Button variant="outline" size="sm" @click="load">Retry</Button>
        </AlertAction>
      </Alert>

      <div v-if="accounts.length === 0 && !accountsError" class="grid gap-6 lg:grid-cols-2" aria-busy="true">
        <Card class="h-80 animate-pulse" />
        <Card class="h-80 animate-pulse" />
      </div>

      <div v-if="accounts.length > 0" class="grid items-start gap-6 lg:grid-cols-2">
        <ActionPanel v-model:tab="tab" v-model:account-id="actionAccountId" />
        <LedgerTable />
      </div>
    </main>
  </div>
</template>
