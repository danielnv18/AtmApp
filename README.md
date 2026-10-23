# AtmApp

.NET 10 minimal API (`api/`) + Vue 3 client (`client/`).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 22.18 or later, or 24.12 or later

## Run the app

Clone the repository:

```sh
git clone https://github.com/danielnv18/AtmApp.git AtmApp
cd AtmApp
```

Start the API in one terminal, from the repository root. It listens on http://localhost:5000. On first start it creates the database `api/atm.db` and seeds Checking with $1,000.00 and Savings with $500.00.

```sh
dotnet run --project api
```

Start the client in a second terminal, also from the repository root:

```sh
cd client
npm install
npm run dev
```

Open http://localhost:5173. The client sends `/api` requests to the API, so keep both terminals running.

Press `Ctrl+C` in each terminal to stop.

## Run the tests

API tests, from the repository root:

```sh
dotnet test AtmApp.slnx
```

Client unit and component tests, from `client/`:

```sh
npm run test:unit -- --run
```

End-to-end tests, from `client/`:

```sh
npx playwright install chromium
npm run test:e2e
```

The end-to-end tests start their own API on port 5199 and client on port 5174. The API runs against a fresh temporary database, so the tests never change `api/atm.db`. Stop any API you started with `dotnet run` first. On Windows it locks the build output the tests need to rebuild. The `full-stack` project drives the real API. The `mocked` project fakes the API to reproduce failures such as outages and conflicts.

## Reset the data

Stop the API and delete `api/atm.db`. The next start recreates it with the opening balances. Do this after pulling schema changes, such as the `TransferIn` and `TransferOut` ledger types.

## API errors

Errors are `application/problem+json` with `status`, `title`, and `detail`.

| Status | Cause                                                                          |
| ------ | ------------------------------------------------------------------------------ |
| 400    | Invalid amount, same-account transfer, malformed body, or bad `limit`/`cursor` |
| 404    | Unknown account                                                                |
| 409    | Concurrent update. Retry.                                                      |
| 422    | Insufficient funds                                                             |
| 500    | Unexpected failure. No internal details.                                       |

The Swagger UI is at http://localhost:5000/swagger while the API runs.

## Known limitations

What the code does not do, so nobody assumes it does.

**Concurrency.** Concurrent writes to one account are detected, not resolved.

- A stale write fails with `409` through the `LastSequence` concurrency token, and nothing retries it. The caller must retry.
- There are no idempotency keys. If a response is lost and the client replays a `POST`, the money moves twice.
- SQLite runs with `journal_mode = DELETE` and a 5 second timeout, to keep a single database file. A write that waits longer fails with `SQLITE_BUSY`, which surfaces as a `500`, not a `409` or `503`. No test covers it.
- A database trigger failure (broken ledger chain, update or delete of a ledger row) also surfaces as a `500`. That is intended, because it means a bug.
- Saving two new ledger rows for the same account in one `SaveChanges` is unsafe. EF orders inserts by key, so the chain trigger can see them out of sequence. Each operation saves at most one row per account.

**Product.**

- One currency, and no account creation, authentication or overdraft.
- No cap on an account balance. Only a single amount is capped.
- Account names are not unique, and nothing enforces that exactly two accounts exist.
- The client loads only the first page of history, though the API supports `limit` and `cursor`.

**Operations.**

- No production build story. The client relies on the Vite dev proxy, and the API has no CORS or static hosting.
- No CI, Docker image or health check endpoint.
- The OpenAPI document has no `Produces` metadata.
