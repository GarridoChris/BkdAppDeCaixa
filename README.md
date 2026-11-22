# App de Caixa

Salon cash management API built with ASP.NET Core and .NET 10. The active backend is in `backend/`.

## Backend foundation

- .NET 10 / ASP.NET Core Web API
- PostgreSQL with EF Core and Npgsql
- Layered projects: API, Application, Domain, Infrastructure
- OpenAPI JSON and Swagger UI in Development
- xUnit project for domain and architecture tests

### Prerequisites

- .NET 10 SDK
- Docker Desktop with Docker Compose (or a local PostgreSQL 17 server)

### Run locally

If you use the included development database, start PostgreSQL from the repository root:

```powershell
docker compose -f backend/docker-compose.yml up -d
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=organizaeh;Username=appdecaixa;Password=development-only"
$env:Frontend__Origin = "http://localhost:3000"
dotnet tool restore
dotnet tool run dotnet-ef -- database update --project backend/src/AppDeCaixa.Infrastructure --startup-project backend/src/AppDeCaixa.Api
dotnet run --project backend/src/AppDeCaixa.Api
```

If PostgreSQL is already running, skip Docker Compose, create an empty database named `organizaeh`, and set `ConnectionStrings__Default` to it. The migration command above creates the application tables. Compose applies `POSTGRES_DB` only when initializing a new volume; if you already used this Compose setup, create `organizaeh` in that existing PostgreSQL server without deleting its volume. For the existing salon database, review [backend documentation](backend/README.md) before applying migrations; the existing database is not recreated.

The `/health` endpoint checks PostgreSQL connectivity. OpenAPI JSON is `/openapi/v1.json` and Swagger UI is `/swagger`, both in Development. Startup does not alter the schema.

### Run the API and frontend in Docker

Open Docker Desktop in Linux container mode. From the repository root, make a local configuration file and generate a JWT signing key:

```powershell
Copy-Item docker.env.example .env.docker
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 48
$rng.GetBytes($keyBytes)
[Convert]::ToBase64String($keyBytes)
$rng.Dispose()
notepad .env.docker
```

Set your PostgreSQL username and password in `APPDECAIXA_DOCKER_DB_CONNECTION` and paste the generated key into `APPDECAIXA_DOCKER_SIGNING_KEY`. The file is ignored by Git. For PostgreSQL running on this Windows computer, use `host.docker.internal` in the connection string. From inside the API container, `localhost` refers to the API container itself. The Windows PostgreSQL server must accept connections from Docker Desktop.

If you have a local Next.js process using port 3000, stop it before starting the containers:

```powershell
docker compose --env-file .env.docker -f compose.app.yml up --build -d
docker compose --env-file .env.docker -f compose.app.yml ps
```

Open [the local frontend](http://localhost:3000) and sign in with an existing account. The Next.js server sends API requests to `http://api:8080` inside Docker. The API runs in Production mode, so use the browser UI for sign-in. Swagger is only available in Development.

To view startup errors or shut down the app containers:

```powershell
docker compose --env-file .env.docker -f compose.app.yml logs api frontend
docker compose --env-file .env.docker -f compose.app.yml down
```

This Compose file starts only the two app services. PostgreSQL stays outside this stack, and schema updates are applied separately. When the database moves to a hosting provider, set the API's `ConnectionStrings__Default` to the hosted connection string and use the provider's database TLS settings. For a public deployment, serve the frontend over HTTPS, set `APP_ORIGIN` to its exact public origin, and set `API_BASE_URL` to the API address reachable by the Next.js server. Set `Hosting__RedirectToHttps=false` only when the API receives HTTP on a private network.

Run tests with `dotnet test AppDeCaixa.slnx`. The development database can be reset with `docker compose -f backend/docker-compose.yml down -v`; this removes only the local Compose volume and all data stored in it. Never point this reset command at a production database.

See [backend documentation](backend/README.md) for architecture, assumptions, and security notes. No real user credentials or production secrets belong in this repository.
