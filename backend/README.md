# Backend foundation

## Projects and dependency direction

`AppDeCaixa.Domain` holds business concepts and rules and has no framework/database references. `AppDeCaixa.Application` will coordinate use cases and DTOs. `AppDeCaixa.Infrastructure` owns EF Core/PostgreSQL persistence. `AppDeCaixa.Api` owns HTTP, configuration, OpenAPI, and middleware. Dependencies point inward; the API composes the services at startup.

## Foundation endpoints

- `GET /health` — process health.
- `/openapi/v1.json` and `/swagger` — development API reference.

## Implemented domain foundation

- `Money` stores whole reais as integer values, rejects fractional amounts, and uses checked integer arithmetic. The current PostgreSQL `integer` columns also cap each stored amount at the signed 32-bit range.
- `CashSession` enforces nonnegative opening/count amounts and permits only the `Open` to `Closed` transition. It records the opening/closing user IDs and business dates in the domain model.

These rules are unit-tested. Cash-session endpoints open, retrieve, and close sessions; service-order endpoints create orders and add service items. The existing-schema migration added `comanda.caixa_id` to link each order to its session. Audited cash movements use `caixa_movimento`; the legacy `entrada` table remains unused because it has no amount column.

## Authentication foundation

- `POST /api/auth/bootstrap-manager` creates the first active Manager account for an existing company only when `Bootstrap:AdminKey` is configured and no active Manager exists. Supply the key in the `X-Admin-Bootstrap-Key` header. The key is rate-limited, compared in constant time, and should be removed from the API environment after bootstrap.
- `POST /api/auth/bootstrap-company-manager` creates the initial company and first Manager together in one transaction. It uses the same one-time admin key and rate limit, so an empty database can be initialized without manually inserting an enterprise first. Example request:

  ```json
  {
    "companyName": "Example Salon",
    "cnpj": "12.345.678/0001-90",
    "contactName": "Salon Owner",
    "managerName": "Salon Owner",
    "managerEmail": "owner@example.com",
    "managerPassword": "replace-with-a-long-password"
  }
  ```

  CNPJ is stored as a 14-character string, normalized to uppercase with punctuation removed. The first 12 positions accept ASCII letters or digits; the final two must be digits. The API checks this structure and uniqueness but does not verify the check digits against [Receita Federal's technical specification](https://www.gov.br/receitafederal/pt-br/centrais-de-conteudo/publicacoes/documentos-tecnicos/cnpj).
- `POST /api/auth/token` verifies a password hash with ASP.NET Core's `PasswordHasher` and returns a 15-minute JWT. Invalid credentials, inactive users, and users without an assigned role receive the same unauthorized response.
- `GET /api/auth/me` requires a bearer token.
- `POST /api/users` requires the Manager role and creates a same-company Manager, Cashier, or Employee account. Employee accounts must link to an active collaborator in the manager's company; the API hashes the password and never returns it.
- In Swagger, click **Authorize** and paste the raw access token (without typing `Bearer `). Call `GET /api/auth/me` to confirm the token, then use `POST /api/users` to add a row to `usuario`. The company is taken from the Manager token. Cashier and Manager requests use `employeeId: null`; Employee requests need an active collaborator ID.
- Example cashier request body (use a unique email and a password with at least 12 characters):

  ```json
  {
    "name": "Front Desk",
    "email": "frontdesk@example.com",
    "password": "replace-with-a-long-password",
    "role": "Cashier",
    "employeeId": null
  }
  ```
- Roles are `Manager`, `Cashier`, and `Employee`. Existing accounts remain roleless after migration until a manager role is assigned. Existing password hashes are not assumed compatible; the bootstrap account is created with the new hasher.
- In Development, an ephemeral signing key is generated if `Authentication:SigningKey` is absent; tokens stop working after the API restarts. Outside Development, configure a secret key of at least 32 bytes through `Authentication__SigningKey`. Do not commit either secret. The frontend still needs to choose token storage; the API does not prescribe local storage or cookies.

Generate local keys in PowerShell without sharing them:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 48
$rng.GetBytes($keyBytes)
$env:Authentication__SigningKey = [Convert]::ToBase64String($keyBytes)
$rng.GetBytes($keyBytes)
$env:Bootstrap__AdminKey = [Convert]::ToBase64String($keyBytes)
$rng.Dispose()
```

## Create a new database

The application database is named `organizaeh`; PostgreSQL tables use the `public` schema inside that database. Create an empty database named `organizaeh` in PostgreSQL (for example, in pgAdmin or with `createdb organizaeh`), then set `ConnectionStrings__Default` to point to it. The first EF migration now detects an empty database and creates the legacy salon tables before applying the current MVP migrations. If the tables already exist, it leaves them in place and only applies migration changes, so the current `postgres` database is not recreated.

From the repository root in PowerShell:

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=organizaeh;Username=YOUR_USER;Password=YOUR_PASSWORD"
dotnet tool run dotnet-ef -- database update --project backend/src/AppDeCaixa.Infrastructure --startup-project backend/src/AppDeCaixa.Api
```

Replace `YOUR_USER` and `YOUR_PASSWORD` with the PostgreSQL credentials you use for this database. The migrations create tables, constraints, and indexes. After applying them, configure `Bootstrap__AdminKey` and call `/api/auth/bootstrap-company-manager` once to create the company and Manager together. The older `/api/auth/bootstrap-manager` endpoint remains available when a company row already exists. Fictional `DemoSampleData` records are opt-in: migrations never seed them unless `APPDECAIXA_SEED_DEMO=true` is set in that PowerShell session, and an active Manager must already exist. This keeps new production databases free of fake financial records by default.

## Existing-database migration

`ExistingSchemaAuthAndOrderLinks` adds nullable `usuario.role`, `usuario.colaborador_id`, and `comanda.caixa_id` columns plus their constraints. For an empty database only, it also creates the original 12-table salon baseline. Existing rows remain valid and these fields start null. Rolling back this migration removes its added columns but leaves the baseline tables intact. The local salon database has this migration applied, so the updated baseline will not run again there.

`CashSessionOneOpenPerCompany` adds a partial unique index so a company cannot have two cash sessions with `data_fechamento IS NULL`. The local database has both migrations applied. For another database, check for duplicate open sessions before applying it:

```sql
SELECT empresa_id, COUNT(*)
FROM public.caixa
WHERE data_fechamento IS NULL AND empresa_id IS NOT NULL
GROUP BY empresa_id
HAVING COUNT(*) > 1;
```

If the query returns rows, review and close the duplicate sessions deliberately; do not delete financial history. The migration also checks this condition and stops with an error if duplicates remain.

`AddServiceCatalogName` adds a required `nome` column to the existing `servicos` table. It preserves any existing rows by copying `descricao` into `nome`; the local salon database currently has no service rows, so this backfill will not change records. `ServiceOrderPriceAdjustments` adds the catalog-price snapshot and optional adjustment audit fields to `comanda_servico`. It backfills catalog prices from each existing item's stored price and adds an optional link to the user who adjusted the price. `ServiceOrderClientName` adds `comanda.cliente_nome` so each new order records the client's name. `OrderPaymentRecording` adds payment actor/note audit fields and seeds supported payment methods if missing. `CashSessionMovements` creates a dedicated `caixa_movimento` table for audited manual cash additions and withdrawals; the legacy `entrada` table remains unused because it has no amount field and its other columns have unclear meaning. `CommissionPayoutIntegrity` adds actor and timestamp audit fields to `pagamento_colaborador` and a unique index that prevents an order item from being included in multiple payouts. It stops if existing payout history already contains duplicate order items, which must be reviewed before retrying. Apply any unapplied migrations before using their endpoints.
`CompanyCnpjText` converts `empresa.cnpj` from an integer to text so new CNPJ values retain leading zeroes and support the 14-character alphanumeric format. Existing numeric values are cast to text as stored; review them after applying the migration because any leading zeroes were already lost when they were stored as integers. Its rollback can fail after nonnumeric or out-of-range CNPJ values have been added.
`DebtSettlementPayments` adds a cash-session link and settlement flag to payment records. It backfills existing payments' cash-session links from their order, so older reconciliations continue to work. Apply pending migrations before using the Fiado settlement endpoint.

`DemoSampleData` inserts clearly labeled fictional records into the first company with an active Manager: two collaborators, three services, one closed sample cash session, three sample orders (cash, Pix, and partially settled Fiado), a CashOut movement, and one commission payout. The closed session reconciles to R$290 expected cash from R$200 opening cash, R$80 cash sales, R$30 Fiado settlement, and R$20 CashOut. No user accounts or passwords are seeded. This migration is opt-in and requires `APPDECAIXA_SEED_DEMO=true`; it also logs a notice and skips the records if no company with an active Manager exists. The migration ID is still recorded when it is skipped, so set up the development company and Manager before applying it if you need the demo records.

After backing up the database, apply the pending migrations from the repository root with `ConnectionStrings__Default` set to the intended database:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef -- database update --project backend/src/AppDeCaixa.Infrastructure --startup-project backend/src/AppDeCaixa.Api
```

The first migration's `Down` operation removes the new columns and would discard role assignments and order/session links; it leaves the original tables in place. Do not roll it back after using those fields. The demo-data migration's `Down` operation removes its labeled fixture records, but stops if extra payments or commission payouts were added to those sample orders. Do not roll back a migration after applying it to production.

HTTP errors use Problem Details via ASP.NET Core exception handling. Unexpected exceptions return a generic 500 response; details stay in server logs. CORS allows the configured `Frontend:Origin` only, defaulting to `http://localhost:3000`.

## Database and migrations

PostgreSQL is configured from `ConnectionStrings:Default` (environment form: `ConnectionStrings__Default`). The default database name is `organizaeh`; `backend/docker-compose.yml` is for local development. The EF Core context is in Infrastructure. The first migration creates the original salon tables for a blank database and only adds nullable columns and constraints to a database where those tables already exist. Do not enable automatic migrations at application startup.

### Apply migrations on Railway

Set `ConnectionStrings__Default` on the API service to the target PostgreSQL database. In **Settings → Deploy → Pre-Deploy Command**, enter:

```text
dotnet /app/AppDeCaixa.Api.dll --migrate
```

Railway runs this command in the built API image with access to the private database. The command applies pending EF migrations and exits. The Dockerfile supplies the web server's start command. Confirm the destination before the first run, especially if you plan to restore a full backup. Leave `APPDECAIXA_SEED_DEMO` unset for production.

`backend/.env.example` lists configuration names and placeholders. PowerShell does not automatically load that file; set environment variables in the shell or use a local secret manager. The checked-in development fallback connection string is strictly local and must never be reused outside development.

### Existing database schema review

The existing `public` schema has been inventoried. Primary keys use PostgreSQL sequences, and foreign keys connect the main salon tables. The owner confirmed that stored `valor` amounts are whole Brazilian reais; API validation must reject fractional reais, and persistence must use integer-compatible values without floating-point arithmetic.

The first migration added `usuario.role`, `usuario.colaborador_id`, and `comanda.caixa_id`. The service-name migration adds `servicos.nome`. Remaining schema gaps before financial movement work are:

- `entrada` has no amount column, and its `client_id` and `historico_id` do not have declared foreign keys. Its intended meaning must be established before using it for financial movements.
- Existing `empresa.cnpj` values are converted to text by `CompanyCnpjText`. Review old values for missing leading zeroes after migrating; a value stored as an integer cannot reveal whether leading zeroes were originally present.

The baseline migrations were applied to the local database. Run `dotnet ef database update` to apply any later migrations not already recorded; EF skips migrations that are already applied. Review and back up another database before applying migrations.

## Decisions and open questions

- **Initial policy:** at most one cash session may be open per company. A PostgreSQL partial unique index enforces this; the policy can be changed later if the salon needs simultaneous drawers.
- **Initial validation:** service commission percentages are limited to 0–100. Change this if the salon's commission rules require percentages above 100.
- **Confirmed checkout rule:** the initial order checkout uses one payment method for the full order.
- **Confirmed Fiado settlement rule:** a debt can be reduced by partial or split payments using Pix, Dinheiro, debit card, and/or credit card. A settlement must not exceed the current balance and requires an open cash session.
- **Deferred payment behavior:** refunds are not implemented yet.
- **Assumption to confirm:** the salon uses `America/Bahia` for business dates. Override `Business:TimeZoneId` if it operates in a different timezone. Shifts crossing local midnight close on the local closing date.
- **Open decision:** frontend token storage. The planned API uses short-lived bearer tokens and must keep signing keys in secret configuration. Browser local storage increases exposure to script injection; secure same-site cookies require an anti-forgery strategy and matching deployment topology. Decide with the frontend before enabling authentication.
- **Open decision:** the administrator recovery path if the first Manager account is lost.

## Cash-session endpoints

- `POST /api/cash-sessions` opens a session. Body: `{ "openingCash": 200 }`.
- `GET /api/cash-sessions/current` retrieves the company's open session; returns 404 when none is open.
- `GET /api/cash-sessions/{id}` retrieves a session in the caller's company.
- `POST /api/cash-sessions/{id}/close` closes a session. Body: `{ "countedCash": 350 }`.
- `POST /api/cash-sessions/{id}/movements` records manual drawer cash. Body: `{ "direction": "CashOut", "amount": 30, "note": "Cleaning supplies" }`. `direction` is `CashIn` or `CashOut`.
- `GET /api/cash-sessions/{id}/movements` retrieves the session's audited cash movements.
- `GET /api/cash-sessions/{id}/reconciliation` reports expected drawer cash and totals by payment method. Call it after closing to see the difference between expected and counted cash.

Only Managers and Cashiers can use these routes. Company and actor IDs come from the access token, not the request body. Movement amounts must be positive whole reais and require a note; the actor and local timestamp are recorded. CashOut is rejected if it exceeds the expected drawer balance. Reconciliation calculates expected cash as opening cash plus Dinheiro receipts and CashIn movements minus CashOut movements. Pix and card receipts are reported separately; Fiado originations are reported as debt and excluded from money received. Fiado settlements are also shown as an informational subtotal; their actual receipts are already included in cash, Pix, or card totals. `Difference` is counted cash minus expected cash, and is null while the session is open. `Business:TimeZoneId` sets the business date zone and defaults to `America/Bahia`.

## Service-order endpoints

- `POST /api/orders` opens an order tied to the caller's current cash session. Body: `{ "clientName": "Maria Silva" }`. The name is stored on the order and returned in order details; it is not a unique customer record. Returns 409 when no session is open.
- `GET /api/orders/{id}` retrieves the order and its service items within the caller's company.
- `POST /api/orders/{id}/items` adds a service. Body: `{ "serviceId": 3, "collaboratorId": 7, "observation": "Optional note" }`. To charge a different whole-reais amount, include `priceOverride` and `adjustmentReason`, for example `{ "serviceId": 3, "collaboratorId": 7, "priceOverride": 60, "adjustmentReason": "Loyal client discount" }`.

Only Managers and Cashiers can use these routes. Every new order requires a client name, stored as `comanda.cliente_nome`; names can repeat. The service and collaborator must both be active and belong to the caller's company. Without `priceOverride`, the catalog price is used. Managers and Cashiers may override it; a different amount requires a reason. The item records its catalog-price snapshot, actual charged price, reason, user who made the adjustment, and adjustment time. `comanda.valor_total` is calculated by the API from the actual item prices. The commission percentage is still copied from the catalog. A successful payment closes the order; unpaid open orders continue to prevent their cash session from closing.

## Service-catalog endpoints

- `POST /api/services` creates a service for the Manager's company. Body: `{ "name": "Haircut", "description": "Wash and cut", "commissionPercentage": 40, "price": 80 }`.
- `GET /api/services` lists active services for the caller's company so their IDs can be used when opening orders.

Only Managers can create services; Managers and Cashiers can list them. `name` and `description` are stored separately as `servicos.nome` and `servicos.descricao`. Price is a nonnegative whole-reais integer; new services are active.

## Collaborator endpoints

- `POST /api/collaborators` creates an active collaborator for the Manager's company. Body: `{ "name": "Alex Worker", "cpf": null, "email": null }`.
- `GET /api/collaborators` lists active collaborators for the caller's company. Use the returned ID when adding an order item.

Only Managers can create collaborators; Managers and Cashiers can list them. CPF is optional and, when present, must be unique. The company is taken from the access token.

## Payment endpoints

- `GET /api/payment-methods` lists Pix, cash, debit card, credit card, and Fiado methods with their database IDs. Use these returned IDs in payment requests; the numeric IDs can vary between databases.
- `POST /api/orders/{id}/payments` records one full-order payment. Body: `{ "paymentMethodId": 2 }`; replace `2` with the selected method ID returned above. The amount is computed from the order's service items and is not accepted from the client.
- `GET /api/debts?clientName=Maria Silva&limit=50` lists outstanding Fiado orders. `clientName` is optional and matches the full name case-insensitively; multiple orders for the same name are returned separately. `limit` is optional and defaults to 50, with a maximum of 100.
- `POST /api/orders/{id}/debt-settlements` records a partial or split payment against an order originally closed as Fiado. Example body: `{ "payments": [{ "paymentMethodId": 1, "amount": 30 }, { "paymentMethodId": 2, "amount": 20 }], "note": "First installment" }`. Amounts are positive whole reais, and method IDs come from `GET /api/payment-methods`. Do not include Fiado as a settlement method.
- For Fiado, include a note and use the Fiado method ID returned above, for example: `{ "paymentMethodId": 5, "note": "Manager approved payment next Friday" }`.

Managers and Cashiers may record Pix, cash, debit, or credit payments. Fiado is a debt record, not money received: only a Manager may authorize it, and an explanatory note, the manager's user ID, and timestamp are recorded. The order is marked as debt and closed for service operations. Its `fiado` flag remains true after later settlement to preserve the fact that it originated as debt; `outstandingDebt` in the order response shows the remaining balance. A second checkout payment cannot be recorded on an already-paid order.

Managers and Cashiers may settle a debt. The total of the allocations cannot exceed the outstanding balance; each request can use one to four distinct methods. An open cash session is required. Every allocation is saved as its own payment row with the actor, local timestamp, note, and receiving cash session. Partial payments leave the remaining balance open; the order's existing `GET /api/orders/{id}` response reports the updated `outstandingDebt`. Split receipts are assigned to the current cash session, so reconciliation counts them in the drawer where the money was received rather than the session that originally authorized Fiado.

## Commission endpoints

- `GET /api/collaborators/{collaboratorId}/commissions?year=2026&month=11&half=1` previews unpaid commissions for the selected collaborator and period. `half` is `1` for days 1–15 and `2` for day 16 through the month's final day.
- `POST /api/collaborators/{collaboratorId}/commission-payouts` records a payout. Example body: `{ "year": 2026, "month": 11, "half": 1, "paymentMethod": "Pix", "note": "Optional note" }`. `paymentMethod` can be `Pix` or `Dinheiro`; the amount is calculated by the API.

Both routes require the Manager role. Closed orders are assigned by their local closing date. Each service item's commission uses the final charged item value and its saved commission percentage, with each line rounded to the nearest whole real (.5 rounds up). Fiado orders count when they close. A payout stores the calculated line items, marks them paid, and records the manager and local timestamp in one database transaction. The database also prevents any service item from appearing in two payouts.

Employees can use `GET /api/me/commissions?year=2026&month=11&half=1` to see only their own closed service items for the selected period, including each calculated commission and whether it has been paid. Paid items include their payout date and method when a payout record exists. The collaborator ID is taken from the signed-in Employee account; it is not accepted from the request. This is read-only self-service; discrepancy challenges are not implemented.

## Security status

This is a development foundation, not production-ready. Bearer authentication, role claims, login rate limits, a database health check, Manager-controlled user, collaborator, and service creation, cash-session lifecycle, service-order creation and item recording, full-order payments, partial/split Fiado settlement, cash movements, reconciliation, commission payouts, and read-only Employee commission self-service are implemented. The service-name, order-price audit, client-name, payment audit, cash-movement, commission-payout, and debt-settlement migrations must be applied before the related endpoints can use the new columns or tables. User creation writes `usuario`; collaborators write `colaborador`; services write `servicos`; cash sessions write `caixa`; movements write `caixa_movimento`; service orders write `comanda` and `comanda_servico`; checkout and debt-settlement payments write `comanda_pagamento`; commission payouts write `pagamento_colaborador` and `pagamento_colaborador_item`. Refunds, commission dispute handling, PostgreSQL integration tests, and deployment-specific HTTPS/CORS settings remain. New credentials use ASP.NET Core's password hasher; compatibility of old hashes is unverified. Never log credentials or tokens.

The retired prototype files were removed from the working tree. Their prior contents remain in Git history; rotate any credential from that prototype if it was used outside a disposable local database.

## Verify

```powershell
dotnet build AppDeCaixa.slnx
dotnet test AppDeCaixa.slnx
```
