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

Run tests with `dotnet test AppDeCaixa.slnx`. The development database can be reset with `docker compose -f backend/docker-compose.yml down -v`; this removes only the local Compose volume and all data stored in it. Never point this reset command at a production database.

See [backend documentation](backend/README.md) for architecture, assumptions, and security notes. No real user credentials or production secrets belong in this repository.
