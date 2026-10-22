<script setup lang="ts">
import { onMounted, ref } from 'vue'
import 'vue-sonner/style.css'
import { useAtm } from '@/composables/useAtm'
import type { Operation } from '@/types'
import AccountCards from '@/components/AccountCards.vue'
import ActionPanel from '@/components/ActionPanel.vue'
import LedgerTable from '@/components/LedgerTable.vue'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'

const { accounts, accountsError, refresh } = useAtm()

const tab = ref<Operation>('deposit')
const actionAccountId = ref<string>()

function start(operation: Operation, accountId: string) {
  tab.value = operation
  actionAccountId.value = accountId
}

async function load() {
  await refresh()
  actionAccountId.value ??= accounts.value[0]?.id
}

onMounted(load)
</script>

<template>
  <div class="bg-muted/40 min-h-svh">
    <main class="mx-auto grid max-w-5xl gap-6 px-4 py-8">
      <header>
        <h1 class="text-2xl font-semibold tracking-tight">ATM</h1>
      </header>

      <Alert v-if="accountsError" variant="destructive">
        <AlertTitle>Could not load accounts</AlertTitle>
        <AlertDescription>{{ accountsError }}</AlertDescription>
        <AlertAction>
          <Button variant="outline" size="sm" @click="load">Retry</Button>
        </AlertAction>
      </Alert>

      <template v-if="accounts.length > 0">
        <AccountCards :accounts="accounts" @start="start" />
        <ActionPanel v-model:tab="tab" v-model:account-id="actionAccountId" />
        <LedgerTable />
      </template>
    </main>
    <Toaster rich-colors position="top-right" />
  </div>
</template>
