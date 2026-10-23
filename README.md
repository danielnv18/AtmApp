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
