<script setup lang="ts">
import { computed, ref } from 'vue'
import type { Account, Operation, OperationRequest } from '@/types'
import AccountSelect from './AccountSelect.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const props = defineProps<{
  accounts: readonly Account[]
  submit: (request: OperationRequest) => Promise<void>
}>()

const OPERATIONS: { op: Operation; label: string }[] = [
  { op: 'deposit', label: 'Deposit' },
  { op: 'withdraw', label: 'Withdraw' },
  { op: 'transfer', label: 'Transfer' },
]

const tab = ref<Operation>('deposit')
const accountId = ref(props.accounts[0]?.id)
const toAccountId = ref<string>()
const amount = ref('')
const pending = ref(false)
const error = ref<string | null>(null)

const toAccounts = computed(() => props.accounts.filter((a) => a.id !== accountId.value))
const parsedAmount = computed(() => Number(amount.value))
const canSubmit = computed(
  () =>
    !pending.value &&
    parsedAmount.value > 0 &&
    accountId.value !== undefined &&
    (tab.value !== 'transfer' || toAccountId.value !== undefined),
)

async function onSubmit() {
  if (!canSubmit.value || accountId.value === undefined) return
  pending.value = true
  error.value = null
  try {
    await props.submit({
      op: tab.value,
      accountId: accountId.value,
      toAccountId: tab.value === 'transfer' ? toAccountId.value : undefined,
      amount: parsedAmount.value,
    })
    amount.value = ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <Card>
    <CardHeader>
      <CardTitle>Transactions</CardTitle>
    </CardHeader>
    <CardContent>
      <Tabs v-model="tab" @update:model-value="error = null">
        <TabsList class="w-full">
          <TabsTrigger v-for="o in OPERATIONS" :key="o.op" :value="o.op">{{ o.label }}</TabsTrigger>
        </TabsList>
        <TabsContent v-for="o in OPERATIONS" :key="o.op" :value="o.op" class="pt-4">
          <form class="grid gap-4" @submit.prevent="onSubmit">
            <div v-if="o.op === 'transfer'" class="grid gap-4 sm:grid-cols-2">
              <AccountSelect v-model="accountId" label="From" :accounts="accounts" />
              <AccountSelect v-model="toAccountId" label="To" :accounts="toAccounts" />
            </div>
            <AccountSelect v-else v-model="accountId" label="Account" :accounts="accounts" />

            <div class="grid gap-1.5">
              <Label :for="`${o.op}-amount`">Amount (USD)</Label>
              <Input :id="`${o.op}-amount`" v-model="amount" inputmode="decimal" autocomplete="off" placeholder="0.00" />
            </div>

            <p v-if="error" class="text-destructive text-sm">{{ error }}</p>

            <Button type="submit" :disabled="!canSubmit">{{ o.label }}</Button>
          </form>
        </TabsContent>
      </Tabs>
    </CardContent>
  </Card>
</template>
