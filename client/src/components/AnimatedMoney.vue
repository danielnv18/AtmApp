<script setup lang="ts">
import { computed } from 'vue'
import { useTweenedNumber } from '@/composables/useTweenedNumber'
import { formatMoney } from '@/lib/money'

const props = defineProps<{ value: number }>()
const tweened = useTweenedNumber(() => props.value)
const text = computed(() => formatMoney(Math.round(tweened.value * 100) / 100))
</script>

<template>
  <span class="tabular-nums">{{ text }}</span>
</template>
