<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { ArrowDownUpIcon, CircleCheckIcon, Loader2Icon } from '@lucide/vue'
import { ApiError } from '@/api/atmClient'
import { useAtm } from '@/composables/useAtm'
import { formatMoney, parseAmount } from '@/lib/money'
import type { Operation } from '@/types'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

const tab = defineModel<Operation>('tab', { required: true })
const accountId = defineModel<string | undefined>('accountId')

const { accounts, pending, deposit, withdraw, transfer, refresh } = useAtm()

type Step = 'edit' | 'review' | 'done'
type Receipt = { title: string; detail: string; accountName: string; balance: number }

const OPERATIONS: { op: Operation; label: string; noun: string }[] = [
  { op: 'deposit', label: 'Deposit', noun: 'Deposit' },
  { op: 'withdraw', label: 'Withdraw', noun: 'Withdrawal' },
  { op: 'transfer', label: 'Transfer', noun: 'Transfer' },
]
const QUICK_AMOUNTS = [20, 50, 100, 200]

const step = ref<Step>('edit')
const amount = ref('')
const failure = ref<{ title: string; detail?: string } | null>(null)
const receipt = ref<Receipt | null>(null)
const amountField = useTemplateRef<HTMLElement>('amountField')

const parsed = computed(() => parseAmount(amount.value))
const from = computed(() => accounts.value.find((a) => a.id === accountId.value))
// Only two accounts exist, so a transfer always goes to "the other one".
const to = computed(() => accounts.value.find((a) => a.id !== accountId.value))
const meta = computed(() => OPERATIONS.find((o) => o.op === tab.value)!)
const debits = computed(() => tab.value !== 'deposit')
const busy = computed(() => pending.value !== null)

const problem = computed(() => {
  const value = parsed.value
  if (amount.value.trim() === '') return null
  if (value === null) return 'Enter an amount greater than zero, such as 25.00.'
  const available = from.value?.balance ?? 0
  if (debits.value && value > available) return `That is ${formatMoney(value - available)} more than is available.`
  return null
})

const projected = computed(() => {
  const value = parsed.value
  if (value === null || problem.value !== null || from.value === undefined) return null
  return from.value.balance + (debits.value ? -value : value)
})

const canReview = computed(
  () =>
    !busy.value &&
    parsed.value !== null &&
    problem.value === null &&
    from.value !== undefined &&
    (tab.value !== 'transfer' || to.value !== undefined),
)

function focusAmount() {
  void nextTick(() => amountField.value?.querySelector('input')?.focus())
}

function reset() {
  step.value = 'edit'
  amount.value = ''
  failure.value = null
  receipt.value = null
  focusAmount()
}

function selectAccount(id: string) {
  accountId.value = id
  if (step.value !== 'edit') reset()
}

function swap() {
  if (to.value !== undefined) accountId.value = to.value.id
}

watch(tab, reset)

function review() {
  if (!canReview.value) return
  failure.value = null
  step.value = 'review'
}

async function execute() {
  const value = parsed.value
  const source = from.value
  if (value === null || source === undefined) return
  try {
    let detail: string
    if (tab.value === 'deposit') {
      await deposit(source.id, value)
      detail = `${formatMoney(value)} deposited to ${source.name}`
    } else if (tab.value === 'withdraw') {
      await withdraw(source.id, value)
      detail = `${formatMoney(value)} withdrawn from ${source.name}`
    } else {
      const target = to.value
      if (target === undefined) return
      await transfer(source.id, target.id, value)
      detail = `${formatMoney(value)} moved from ${source.name} to ${target.name}`
    }
    const balance = accounts.value.find((a) => a.id === source.id)?.balance ?? 0
    receipt.value = { title: `${meta.value.noun} complete`, detail, accountName: source.name, balance }
    step.value = 'done'
  } catch (e) {
    failure.value = describeFailure(e)
    step.value = 'edit'
  }
}

function describeFailure(e: unknown): { title: string; detail?: string } {
  if (!(e instanceof ApiError)) {
    return { title: 'Could not reach the server', detail: 'Check your connection and try again.' }
  }
  if (e.status === 422) return { title: 'Insufficient funds', detail: e.detail ?? undefined }
  if (e.status === 409) {
    void refresh()
    return { title: 'Balance changed', detail: 'The balance changed while you were working. Review it and try again.' }
  }
  return { title: e.title, detail: e.detail ?? undefined }
}
</script>

<template>
  <Card data-testid="action-panel">
    <CardHeader>
      <CardTitle class="text-teal text-2xl">What would you like to do?</CardTitle>
    </CardHeader>
    <CardContent class="grid gap-5">
      <div class="grid gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Account">
        <button
          v-for="a in accounts"
          :key="a.id"
          type="button"
          role="radio"
          :aria-checked="a.id === accountId"
          data-testid="account-card"
          :data-account-name="a.name"
          class="focus-visible:ring-ring/50 rounded-xl border p-3.5 text-left transition focus-visible:ring-3 focus-visible:outline-none"
          :class="a.id === accountId ? 'bg-teal border-teal text-white' : 'hover:bg-muted bg-card'"
          @click="selectAccount(a.id)"
        >
          <span
            class="inline-block rounded-sm px-2 py-0.5 text-xs font-medium"
            :class="a.id === accountId ? 'bg-orange text-white' : 'text-teal'"
          >
            {{ a.name }}
          </span>
          <span class="font-heading mt-1.5 block text-2xl font-medium tabular-nums" data-testid="balance">
            {{ formatMoney(a.balance) }}
          </span>
        </button>
      </div>

      <div class="grid grid-cols-3 gap-2" role="group" aria-label="Operation">
        <Button
          v-for="o in OPERATIONS"
          :key="o.op"
          type="button"
          class="rounded-full"
          :variant="o.op === tab ? 'default' : 'outline'"
          :aria-pressed="o.op === tab"
          :disabled="busy"
          @click="tab = o.op"
        >
          {{ o.label }}
        </Button>
      </div>

      <template v-if="step === 'edit'">
        <Alert v-if="failure" variant="destructive">
          <AlertTitle>{{ failure.title }}</AlertTitle>
          <AlertDescription v-if="failure.detail">{{ failure.detail }}</AlertDescription>
        </Alert>

        <form class="grid gap-4" @submit.prevent="review">
          <div v-if="tab === 'transfer' && from && to" class="flex items-center gap-3 text-sm">
            <div class="grid flex-1 gap-0.5">
              <span class="text-muted-foreground text-xs">From</span>
              <span class="font-medium" data-testid="transfer-from">{{ from.name }}</span>
            </div>
            <Button type="button" variant="outline" size="icon-sm" class="rounded-full" aria-label="Swap accounts" @click="swap">
              <ArrowDownUpIcon />
            </Button>
            <div class="grid flex-1 gap-0.5 text-right">
              <span class="text-muted-foreground text-xs">To</span>
              <span class="font-medium" data-testid="transfer-to">{{ to.name }}</span>
            </div>
          </div>

          <div class="grid gap-1.5">
            <Label for="amount">Amount (USD)</Label>
            <div ref="amountField">
              <Input
                id="amount"
                v-model="amount"
                inputmode="decimal"
                autocomplete="off"
                placeholder="0.00"
                class="font-heading h-12 text-2xl tabular-nums md:text-2xl"
                :aria-invalid="problem !== null"
                aria-describedby="amount-help"
              />
            </div>
            <p
              id="amount-help"
              class="text-sm"
              :class="problem ? 'text-destructive' : 'text-teal'"
              aria-live="polite"
              data-testid="amount-help"
            >
              <template v-if="problem">{{ problem }}</template>
              <template v-else-if="projected !== null">New balance: {{ formatMoney(projected) }}</template>
              <template v-else-if="from">Available: {{ formatMoney(from.balance) }}</template>
            </p>
          </div>

          <div class="flex flex-wrap gap-2">
            <Button
              v-for="n in QUICK_AMOUNTS"
              :key="n"
              type="button"
              variant="outline"
              size="sm"
              class="rounded-full"
              :class="amount === String(n) && 'border-lime bg-lime/15'"
              @click="amount = String(n)"
            >
              ${{ n }}
            </Button>
            <Button
              v-if="debits && from && from.balance > 0"
              type="button"
              variant="outline"
              size="sm"
              class="rounded-full"
              @click="amount = from.balance.toFixed(2)"
            >
              All
            </Button>
          </div>

          <Button
            type="submit"
            size="lg"
            class="bg-lime hover:bg-lime-dark rounded-full tracking-wide text-white uppercase"
            :disabled="!canReview"
          >
            Review {{ meta.label.toLowerCase() }}
          </Button>
        </form>
      </template>

      <div v-else-if="step === 'review' && from && parsed !== null && projected !== null" class="grid gap-4" data-testid="review">
        <h3 class="text-teal font-heading text-lg font-medium">Please confirm</h3>
        <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt class="text-muted-foreground">Action</dt>
          <dd class="text-right font-medium">{{ meta.label }}</dd>
          <template v-if="tab === 'transfer' && to">
            <dt class="text-muted-foreground">From</dt>
            <dd class="text-right">{{ from.name }}</dd>
            <dt class="text-muted-foreground">To</dt>
            <dd class="text-right">{{ to.name }}</dd>
          </template>
          <template v-else>
            <dt class="text-muted-foreground">Account</dt>
            <dd class="text-right">{{ from.name }}</dd>
          </template>
          <dt class="text-muted-foreground">Amount</dt>
          <dd class="font-heading text-right text-xl font-medium tabular-nums">{{ formatMoney(parsed) }}</dd>
          <dt class="text-muted-foreground">New balance</dt>
          <dd class="text-right tabular-nums">{{ formatMoney(projected) }}</dd>
        </dl>
        <div class="grid grid-cols-2 gap-2">
          <Button variant="outline" class="rounded-full" :disabled="busy" @click="step = 'edit'">Back</Button>
          <Button class="bg-lime hover:bg-lime-dark rounded-full text-white" :disabled="busy" @click="execute">
            <Loader2Icon v-if="busy" class="animate-spin" />
            Confirm
          </Button>
        </div>
      </div>

      <div v-else-if="step === 'done' && receipt" class="grid gap-4 text-center" role="status" data-testid="receipt">
        <CircleCheckIcon class="text-lime-dark mx-auto size-10" />
        <div>
          <h3 class="font-heading text-xl font-medium">{{ receipt.title }}</h3>
          <p class="text-muted-foreground text-sm">{{ receipt.detail }}</p>
        </div>
        <p class="text-sm">
          {{ receipt.accountName }} balance
          <span class="font-heading block text-2xl font-medium tabular-nums">{{ formatMoney(receipt.balance) }}</span>
        </p>
        <Button class="rounded-full" @click="reset">Make another transaction</Button>
      </div>
    </CardContent>
  </Card>
</template>
