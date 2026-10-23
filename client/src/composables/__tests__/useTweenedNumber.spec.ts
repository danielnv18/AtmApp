import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { nextTick, ref } from 'vue'
import { useTweenedNumber } from '@/composables/useTweenedNumber'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'performance', 'requestAnimationFrame', 'cancelAnimationFrame'] })
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useTweenedNumber', () => {
  test('shows the first value at once', () => {
    expect(useTweenedNumber(() => 100).value).toBe(100)
  })

  test('eases to a new target and lands exactly on it', async () => {
    const source = ref(100)
    const shown = useTweenedNumber(() => source.value, 400)

    source.value = 200
    await nextTick()
    vi.advanceTimersByTime(200)
    expect(shown.value).toBeGreaterThan(100)
    expect(shown.value).toBeLessThan(200)

    vi.advanceTimersByTime(300)
    expect(shown.value).toBe(200)
  })

  test('jumps straight to the target under reduced motion', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const source = ref(100)
    const shown = useTweenedNumber(() => source.value)

    source.value = 250
    await nextTick()

    expect(shown.value).toBe(250)
  })
})
