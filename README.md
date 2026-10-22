# AtmApp

.NET 10 minimal API (`api/`) + Vue 3 client (`client/`).

## Run

API (http://localhost:5233):

```sh
cd api
dotnet run --launch-profile http
```

Swagger UI (Development only): http://localhost:5233/swagger

OpenAPI document: http://localhost:5233/openapi/v1.json

Client (http://localhost:5173):

```sh
cd client
npm install
npm run dev
```

Run each in its own terminal.
