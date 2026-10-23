import { test as base } from '@playwright/test'
import { AtmPage } from './atmPage'
import { FakeAtm } from './fakeAtm'

export { expect } from '@playwright/test'
export { money, type AccountName } from './atmPage'

export const test = base.extend<{ atm: AtmPage }>({
  atm: async ({ page }, use) => {
    await use(new AtmPage(page))
  },
})

/** Every /api request from the page is answered by a fresh in-memory FakeAtm. */
export const mockedTest = test.extend<{ fake: FakeAtm }>({
  // Auto, so a test that never names `fake` still cannot reach a real API.
  fake: [
    async ({ page }, use) => {
      const fake = new FakeAtm()
      await fake.install(page)
      await use(fake)
    },
    { auto: true },
  ],
})
