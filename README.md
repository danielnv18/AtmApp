# AtmApp

.NET 10 minimal API (`api/`) + Vue 3 client (`client/`).

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 22.18 or later, or 24.12 or later

## Run the app

Clone the repository:

```sh
git clone <repository-url> AtmApp
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

Browser check. With the API and the client running, from `client/`:

```sh
npx playwright install chromium
node scripts/verify-e2e.mjs
```

The browser check withdraws $20 from Checking and saves a screenshot to `client/.verify/`.

## Reset the data

Stop the API and delete `api/atm.db`. The next start recreates it with the opening balances.

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
