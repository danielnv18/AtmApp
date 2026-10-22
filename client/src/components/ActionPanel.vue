<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Loader2Icon } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { ApiError } from '@/api/atmClient'
import { useAtm } from '@/composables/useAtm'
import { formatMoney, parseAmount } from '@/lib/money'
import type { Operation } from '@/types'
import AccountSelect from './AccountSelect.vue'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const tab = defineModel<Operation>('tab', { required: true })
const accountId = defineModel<string | undefined>('accountId')

const { accounts, pending, deposit, withdraw, transfer, refresh } = useAtm()

const OPERATIONS: { op: Operation; label: string }[] = [
  { op: 'deposit', label: 'Deposit' },
  { op: 'withdraw', label: 'Withdraw' },
  { op: 'transfer', label: 'Transfer' },
]
const QUICK_WITHDRAWALS = [20, 50, 100]

const toAccountId = ref<string>()
const amount = ref('')
const confirming = ref(false)

const parsedAmount = computed(() => parseAmount(amount.value))
const from = computed(() => accounts.value.find((a) => a.id === accountId.value))
const to = computed(() => accounts.value.find((a) => a.id === toAccountId.value))
const toChoices = computed(() => accounts.value.filter((a) => a.id !== accountId.value))

watch(accountId, (id) => {
  if (id === toAccountId.value) toAccountId.value = undefined
})

const canSubmit = computed(
  () =>
    pending.value === null &&
    parsedAmount.value !== null &&
    from.value !== undefined &&
    (tab.value !== 'transfer' || to.value !== undefined),
)

function onSubmit() {
  if (!canSubmit.value) return
  if (tab.value === 'transfer') confirming.value = true
  else void execute()
}

async function execute() {
  const value = parsedAmount.value
  const source = from.value
  if (value === null || source === undefined) return
  const money = formatMoney(value)
  try {
    if (tab.value === 'deposit') {
      await deposit(source.id, value)
      toast.success(`Deposited ${money} to ${source.name}`)
    } else if (tab.value === 'withdraw') {
      await withdraw(source.id, value)
      toast.success(`Withdrew ${money} from ${source.name}`)
    } else {
      const target = to.value
      if (target === undefined) return
      await transfer(source.id, target.id, value)
      toast.success(`Transferred ${money} from ${source.name} to ${target.name}`)
    }
    amount.value = ''
  } catch (e) {
    reportFailure(e)
  } finally {
    confirming.value = false
  }
}

function reportFailure(e: unknown) {
  if (!(e instanceof ApiError)) {
    toast.error('Could not reach the server. Check your connection and try again.')
  } else if (e.status === 422) {
    toast.error('Insufficient funds', { description: e.detail ?? undefined })
  } else if (e.status === 409) {
    toast.error('The balance changed while you were working. Please try again.')
    void refresh()
  } else {
    toast.error(e.title, { description: e.detail ?? undefined })
  }
}
</script>

<template>
  <Card data-testid="action-panel">
    <CardHeader>
      <CardTitle>Transactions</CardTitle>
    </CardHeader>
    <CardContent>
      <Tabs v-model="tab">
        <TabsList class="w-full">
          <TabsTrigger v-for="o in OPERATIONS" :key="o.op" :value="o.op">{{ o.label }}</TabsTrigger>
        </TabsList>
        <TabsContent v-for="o in OPERATIONS" :key="o.op" :value="o.op" class="pt-4">
          <form class="grid gap-4" @submit.prevent="onSubmit">
            <div v-if="o.op === 'transfer'" class="grid gap-4 sm:grid-cols-2">
              <AccountSelect v-model="accountId" label="From" :accounts="accounts" />
              <AccountSelect v-model="toAccountId" label="To" :accounts="toChoices" />
            </div>
            <AccountSelect v-else v-model="accountId" label="Account" :accounts="accounts" />

            <div class="grid gap-1.5">
              <Label :for="`${o.op}-amount`">Amount (USD)</Label>
              <Input
                :id="`${o.op}-amount`"
                v-model="amount"
                inputmode="decimal"
                autocomplete="off"
                placeholder="0.00"
                :aria-invalid="amount !== '' && parsedAmount === null"
              />
              <div v-if="o.op === 'withdraw'" class="flex gap-2">
                <Button
                  v-for="n in QUICK_WITHDRAWALS"
                  :key="n"
                  type="button"
                  variant="outline"
                  size="sm"
                  @click="amount = String(n)"
                >
                  ${{ n }}
                </Button>
              </div>
            </div>

            <Button type="submit" :disabled="!canSubmit">
              <Loader2Icon v-if="pending === o.op" class="animate-spin" />
              {{ o.label }}
            </Button>
          </form>
        </TabsContent>
      </Tabs>
    </CardContent>

    <Dialog v-model:open="confirming">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Confirm transfer</DialogTitle>
          <DialogDescription v-if="from && to && parsedAmount !== null">
            Move {{ formatMoney(parsedAmount) }} from {{ from.name }} to {{ to.name }}?
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" :disabled="pending !== null" @click="confirming = false">Cancel</Button>
          <Button :disabled="pending !== null" @click="execute">
            <Loader2Icon v-if="pending === 'transfer'" class="animate-spin" />
            Confirm transfer
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Card>
</template>
