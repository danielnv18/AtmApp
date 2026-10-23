import { randomUUID } from 'node:crypto'
import type { Page, Route } from '@playwright/test'
import type { Account, HistoryPage, Transaction, TransferReceipt } from '../../src/types'
import type { AccountName } from './atmPage'

export type Endpoint = 'accounts' | 'history' | 'deposit' | 'withdraw' | 'transfer'

export type Problem = { status: number; title: string; detail?: string }

/** Injected into the next request to an endpoint, ahead of the fake's normal handling. */
export type Fault = Problem | 'abort' | { until: Promise<void> }

type Body = Record<string, unknown>
type Request = { params: string[]; query: URLSearchParams; body: Body }
type RouteEntry = {
  endpoint: Endpoint
  method: 'GET' | 'POST'
  path: RegExp
  handle: (req: Request) => unknown
}

const MAX_CENTS = 1_000_000_000_00

class ProblemError extends Error {
  constructor(readonly problem: Problem) {
    super(problem.title)
  }
}

const PROBLEMS = {
  invalidAmount: {
    status: 400,
    title: 'Invalid amount',
    detail: 'Amount must be greater than 0, at most 1,000,000,000.00, and have at most 2 decimal places.',
  },
  sameAccount: { status: 400, title: 'Same account transfer', detail: 'Source and destination accounts must differ.' },
  notFound: { status: 404, title: 'Account not found', detail: 'No account exists with the given id.' },
  insufficientFunds: {
    status: 422,
    title: 'Insufficient funds',
    detail: 'The account balance is too low for this operation.',
  },
} satisfies Record<string, Problem>

type FakeAccount = { id: string; name: AccountName; cents: number; sequence: number }

function toCents(amount: unknown): number {
  if (typeof amount !== 'number') throw new ProblemError(PROBLEMS.invalidAmount)
  const cents = Math.round(amount * 100)
  if (Math.abs(cents - amount * 100) > 1e-6 || cents <= 0 || cents > MAX_CENTS) {
    throw new ProblemError(PROBLEMS.invalidAmount)
  }
  return cents
}

/**
 * In-memory stand-in for the ATM API with the same wire contract: balanceAfter on every
 * ledger row, a shared correlationId on both legs of a transfer, newest-first history,
 * and problem+json errors.
 */
export class FakeAtm {
  private readonly accounts = new Map<string, FakeAccount>()
  private readonly ledger: (Transaction & { sequence: number })[] = []
  private readonly faults: Record<Endpoint, Fault[]> = {
    accounts: [],
    history: [],
    deposit: [],
    withdraw: [],
    transfer: [],
  }
  private readonly requests: Record<Endpoint, number> = {
    accounts: 0,
    history: 0,
    deposit: 0,
    withdraw: 0,
    transfer: 0,
  }

  private readonly routes: RouteEntry[] = [
    {
      endpoint: 'accounts',
      method: 'GET',
      path: /^\/api\/atm\/accounts$/,
      handle: (): Account[] =>
        [...this.accounts.values()]
          .sort((a, b) => a.name.localeCompare(b.name))
          .map((a) => ({ id: a.id, name: a.name, balance: a.cents / 100 })),
    },
    {
      endpoint: 'history',
      method: 'GET',
      path: /^\/api\/atm\/accounts\/([^/]+)\/history$/,
      handle: ({ params: [id], query }): HistoryPage => {
        const account = this.find(id)
        const limit = Number(query.get('limit') ?? 50)
        const items = this.ledger
          .filter((t) => t.accountId === account.id)
          .sort((a, b) => b.sequence - a.sequence)
          .slice(0, limit)
          .map(({ sequence: _, ...t }) => t)
        return { items, nextCursor: null }
      },
    },
    {
      endpoint: 'deposit',
      method: 'POST',
      path: /^\/api\/atm\/deposit$/,
      handle: ({ body }) => this.credit(this.find(body.accountId), toCents(body.amount)),
    },
    {
      endpoint: 'withdraw',
      method: 'POST',
      path: /^\/api\/atm\/withdraw$/,
      handle: ({ body }) => this.debit(this.find(body.accountId), toCents(body.amount)),
    },
    {
      endpoint: 'transfer',
      method: 'POST',
      path: /^\/api\/atm\/transfer$/,
      handle: ({ body }): TransferReceipt => {
        const cents = toCents(body.amount)
        if (body.fromAccountId === body.toAccountId) throw new ProblemError(PROBLEMS.sameAccount)
        const from = this.find(body.fromAccountId)
        const to = this.find(body.toAccountId)
        const correlationId = randomUUID()
        return {
          correlationId,
          withdrawal: this.debit(from, cents, correlationId, `Transfer to ${to.name}`),
          deposit: this.credit(to, cents, correlationId, `Transfer from ${from.name}`),
        }
      },
    },
  ]

  constructor(seed: Record<AccountName, number> = { Checking: 1000, Savings: 500 }) {
    for (const [name, opening] of Object.entries(seed) as [AccountName, number][]) {
      const account: FakeAccount = { id: randomUUID(), name, cents: 0, sequence: 0 }
      this.accounts.set(account.id, account)
      this.credit(account, Math.round(opening * 100), null, 'Opening deposit')
    }
  }

  async install(page: Page) {
    await page.route((url) => url.pathname.startsWith('/api/'), (route) => this.dispatch(route))
  }

  fail(endpoint: Endpoint, ...faults: Fault[]) {
    this.faults[endpoint].push(...faults)
  }

  /** Holds the next request to `endpoint` until the returned function is called. */
  hold(endpoint: Endpoint): () => void {
    const { promise, resolve } = Promise.withResolvers<void>()
    this.fail(endpoint, { until: promise })
    return () => resolve()
  }

  requestCount(endpoint: Endpoint) {
    return this.requests[endpoint]
  }

  accountId(name: AccountName) {
    return this.byName(name).id
  }

  balance(name: AccountName) {
    return this.byName(name).cents / 100
  }

  /** A withdrawal made by some other client, invisible to the page until it re-fetches. */
  withdrawElsewhere(name: AccountName, amount: number) {
    this.debit(this.byName(name), toCents(amount))
  }

  private async dispatch(route: Route) {
    const request = route.request()
    const url = new URL(request.url())
    const entry = this.routes.find((r) => r.method === request.method() && r.path.test(url.pathname))
    if (entry === undefined) {
      return fulfillProblem(route, { status: 404, title: 'Not Found', detail: `${request.method()} ${url.pathname}` })
    }
    this.requests[entry.endpoint]++

    const fault = this.faults[entry.endpoint].shift()
    if (fault === 'abort') return route.abort('internetdisconnected')
    if (fault !== undefined && 'until' in fault) await fault.until
    else if (fault !== undefined) return fulfillProblem(route, fault)

    try {
      const params = url.pathname.match(entry.path)?.slice(1) ?? []
      const body = (request.postDataJSON() ?? {}) as Body
      const json = entry.handle({ params, query: url.searchParams, body })
      await route.fulfill({ status: 200, json })
    } catch (e) {
      if (!(e instanceof ProblemError)) throw e
      await fulfillProblem(route, e.problem)
    }
  }

  private find(id: unknown): FakeAccount {
    const account = typeof id === 'string' ? this.accounts.get(id) : undefined
    if (account === undefined) throw new ProblemError(PROBLEMS.notFound)
    return account
  }

  private byName(name: AccountName): FakeAccount {
    const account = [...this.accounts.values()].find((a) => a.name === name)
    if (account === undefined) throw new Error(`no fake account named ${name}`)
    return account
  }

  private credit(account: FakeAccount, cents: number, correlationId: string | null = null, description: string | null = null) {
    account.cents += cents
    return this.record(account, 'Deposit', cents, correlationId, description)
  }

  private debit(account: FakeAccount, cents: number, correlationId: string | null = null, description: string | null = null) {
    if (cents > account.cents) throw new ProblemError(PROBLEMS.insufficientFunds)
    account.cents -= cents
    return this.record(account, 'Withdrawal', cents, correlationId, description)
  }

  private record(
    account: FakeAccount,
    type: Transaction['type'],
    cents: number,
    correlationId: string | null,
    description: string | null,
  ): Transaction {
    account.sequence++
    const row: Transaction = {
      id: randomUUID(),
      accountId: account.id,
      type,
      amount: cents / 100,
      balanceAfter: account.cents / 100,
      correlationId,
      timestampUtc: new Date().toISOString(),
      description,
    }
    this.ledger.push({ ...row, sequence: account.sequence })
    return row
  }
}

function fulfillProblem(route: Route, { status, title, detail }: Problem) {
  return route.fulfill({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ type: 'about:blank', title, status, detail }),
  })
}
