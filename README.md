# AtmApp

.NET 10 minimal API (`api/`) + Vue 3 client (`client/`).

`AtmApp.slnx` at the repo root holds the API (`api/AtmApp.Api.csproj`) and its tests (`api.Tests/AtmApp.Api.Tests.csproj`).

## Setup

Restore the pinned `dotnet-ef` tool (`.config/dotnet-tools.json`, the same version as the EF Core packages):

```sh
dotnet tool restore
dotnet ef migrations list --project api
```

## Build and test

From the repo root:

```sh
dotnet build AtmApp.slnx
dotnet test AtmApp.slnx
```

Run one test class or test by name:

```sh
dotnet test AtmApp.slnx --filter "FullyQualifiedName~AtmApiTests"
dotnet test AtmApp.slnx --filter "FullyQualifiedName~Parallel_withdrawals"
```

The endpoint tests run the real app in memory through `WebApplicationFactory`. Each test gets its own temporary SQLite file, deleted afterwards, and a `FakeTimeProvider` clock. They read responses into records defined in the test project, so a change to the JSON contract fails a test.

## Run

API (http://localhost:5000):

```sh
cd api
dotnet run --launch-profile http
```

Swagger UI (Development only): http://localhost:5000/swagger

OpenAPI document: http://localhost:5000/openapi/v1.json

Client (http://localhost:5173), which proxies `/api` to the API:

```sh
cd client
npm install
npm run dev
```

Run each in its own terminal.

`api/AtmApp.Api.http` has one request per route. Paste account ids from `GET /api/atm/accounts` into its variables.

## Errors

Every error is `application/problem+json` with `status`, `title`, and `detail`, in every environment:

| Status | Cause |
| --- | --- |
| 400 | Invalid amount (0 or less, more than 2 decimal places, over 1,000,000,000.00), a same-account transfer, a malformed body or a missing field, or a history `limit` outside 1 to 200 or a bad `cursor`. |
| 404 | Unknown account. |
| 409 | Another request changed the account between read and write. Reload and retry. |
| 422 | Insufficient funds. |
| 500 | Unexpected failure. The body is generic and never includes exception details; the exception goes to the log. |

A missing body field is a 400, never a default such as an empty account id. The API serves plain HTTP only, with no HTTPS redirect, because the client reaches it through the Vite proxy.

## Reset the database

Stop the API, then delete this file in `api/`:

- `atm.db`

The next `dotnet run` recreates it and seeds the opening accounts.
