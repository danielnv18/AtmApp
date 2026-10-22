<script setup lang="ts">
import { computed, useId } from 'vue'
import type { Account } from '@/types'
import { formatMoney } from '@/lib/money'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

const props = defineProps<{ label: string; accounts: readonly Account[]; placeholder?: string }>()
const model = defineModel<string | undefined>()
const id = useId()

const describe = (a: Account) => `${a.name} (${formatMoney(a.balance)})`
// Reka's SelectValue keeps the item text from the moment it was chosen, so the
// trigger is rendered from live balances instead.
const triggerText = computed(() => {
  const selected = props.accounts.find((a) => a.id === model.value)
  return selected ? describe(selected) : (props.placeholder ?? 'Select account')
})
</script>

<template>
  <div class="grid gap-1.5">
    <Label :for="id">{{ label }}</Label>
    <Select v-model="model">
      <SelectTrigger :id="id" class="w-full">
        <SelectValue>{{ triggerText }}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="a in accounts" :key="a.id" :value="a.id">{{ describe(a) }}</SelectItem>
      </SelectContent>
    </Select>
  </div>
</template>
