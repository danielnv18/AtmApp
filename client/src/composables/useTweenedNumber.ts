import { onScopeDispose, ref, watch, type Ref } from 'vue'

const prefersReducedMotion = () =>
  typeof window !== 'undefined' && (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false)

const easeOutCubic = (t: number) => 1 - (1 - t) ** 3

/**
 * Follows `source`, easing from the displayed value to each new target. The first value is
 * shown at once so a page load never counts up, and reduced-motion users get the jump.
 */
export function useTweenedNumber(source: () => number, duration = 450): Ref<number> {
  const shown = ref(source())
  let frame = 0

  watch(source, (target) => {
    cancelAnimationFrame(frame)
    const from = shown.value
    if (from === target || duration <= 0 || prefersReducedMotion()) {
      shown.value = target
      return
    }
    const start = performance.now()
    const step = (now: number) => {
      const t = Math.min((now - start) / duration, 1)
      shown.value = t === 1 ? target : from + (target - from) * easeOutCubic(t)
      if (t < 1) frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step)
  })

  onScopeDispose(() => cancelAnimationFrame(frame))
  return shown
}
