# AtmApp

.NET 10 minimal API (`api/`) and Vue 3 client (`client/`) for a two-account ATM: deposit, withdraw, transfer and view history. Data lives in SQLite.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 22.18 or later, or 24.12 or later

## Run the app

```sh
git clone https://github.com/danielnv18/AtmApp.git AtmApp
cd AtmApp
```

Start the API from the repository root. It listens on http://localhost:5000. On first start it creates `api/atm.db` and seeds Checking with $1,000.00 and Savings with $500.00.

```sh
dotnet run --project api
```

Start the client in a second terminal.

```sh
cd client
npm install
npm run dev
```

Open http://localhost:5173. The client proxies `/api` to the API, so keep both running. Swagger UI is at http://localhost:5000/swagger.

To reset the data, stop the API and delete `api/atm.db`.

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

The end-to-end tests start their own API (port 5199) and client (port 5174) on a temporary database, so they never touch `api/atm.db`. Stop any API started with `dotnet run` first, because on Windows it locks the build output. The `full-stack` project uses the real API. The `mocked` project fakes it to reproduce outages and conflicts.

## Design

- **Layers.** `Domain` holds `Money`, `Account` and `Transaction` and enforces the rules. `Application` has `AccountService`, which saves each operation in one database transaction. `Endpoints` maps HTTP to the service and errors to `application/problem+json`. `Infrastructure` is EF Core on SQLite.
- **Ledger.** Every balance change is an append-only row with a per-account sequence and the resulting `BalanceAfter`. Types are `Deposit`, `Withdrawal`, `TransferIn` and `TransferOut`. A transfer is a `TransferOut` and a `TransferIn` row sharing a `correlationId`, saved atomically.
- **Money.** Amounts are positive, at most 1,000,000,000.00, with at most 2 decimal places. SQLite stores them as integer cents, because it has no decimal type.
- **Rules in the database too.** CHECK constraints and triggers (append-only, gapless balance chain) back up the domain checks. A trigger failure is a `500` on purpose, because it means a bug.
- **History paging.** `GET /api/atm/accounts/{id}/history` takes `limit` and an opaque `cursor`.

## API errors

| Status | Cause                                                                          |
| ------ | ------------------------------------------------------------------------------ |
| 400    | Invalid amount, same-account transfer, malformed body, or bad `limit`/`cursor` |
| 404    | Unknown account                                                                |
| 409    | Concurrent update. The caller retries.                                         |
| 422    | Insufficient funds                                                             |
| 500    | Unexpected failure. No internal details.                                       |

## Known limitations

**Concurrency.**

- A stale write returns `409` (optimistic `LastSequence` token) and nothing retries it.
- There are no idempotency keys, so a replayed `POST` after a lost response moves the money twice.
- SQLite uses `journal_mode = DELETE` and a 5 second timeout. A longer wait fails with `SQLITE_BUSY`, which surfaces as a `500`. No test covers it.
- Save at most one new ledger row per account per `SaveChanges`. EF orders inserts by key, so the chain trigger could see two rows out of order.

**Product.**

- One currency. No authentication, account creation or overdraft.
- Nothing enforces exactly two accounts or unique account names, and an account balance has no cap.
- The client loads only the first page of history.

**Operations.** No production build (the client relies on the Vite proxy; the API has no CORS or static hosting), no CI, Docker image or health endpoint, and the OpenAPI document has no `Produces` metadata.
