# AtmApp

.NET 10 minimal API (`api/`) + Vue 3 client (`client/`).

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
