# Shining Stars CRM — API

ASP.NET Core Web API for the Shining Stars Project CRM. The frontend is in
[`STP-Front-End`](https://github.com/The-Shining-Stars-Project/STP-Front-End).

| Layer     | Technology                     |
|-----------|--------------------------------|
| API       | ASP.NET Core Web API (.NET 10) |
| ORM       | Entity Framework Core 9        |
| Database  | Azure SQL (SQL Server)         |
| Files     | Azure Blob Storage             |
| Tests     | xUnit                          |

## Project structure

```
STP-BackEnd/
├── CRM.sln
├── CRM.API/             # Controllers, auth policies, auditing, MFA filter, Program.cs
├── CRM.Application/     # DTOs, service interfaces + implementations, validation
├── CRM.Domain/          # Entities and enums
├── CRM.Infrastructure/  # Auth (tokens, hashing, TOTP), blob storage, email, clock
├── CRM.Persistence/     # AppDbContext, EF configurations, repositories, queries, migrations, seeding
└── CRM.Tests/           # xUnit tests
```

Dependency flow: `API → Application → Domain`. `Persistence` and `Infrastructure` implement
Application interfaces; the inner layers know nothing about the outer ones.

## Running locally

> **Only ever run locally against a local SQL Server.** `dotnet run` uses
> `Properties/launchSettings.json`, which forces the Development environment. In Development
> the API applies pending migrations on boot and seeds demo data. Pointing a local run at the
> production database will change it.

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), plus a local SQL Server
(Docker or LocalDB). Optionally, [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
for PDF uploads.

1. Copy the settings template (the real `appsettings.json` is gitignored):
   ```bash
   cp CRM.API/appsettings.json.template CRM.API/appsettings.json
   ```
   The template documents every setting (JWT, MFA, CORS, forwarded headers, blob storage).
2. Put secrets in .NET User Secrets, never in files:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local SQL Server connection string>" --project CRM.API
   ```
   Development has placeholders for `Jwt:Key` and `Mfa:EncryptionKey`. Production refuses them.
3. Run:
   ```bash
   dotnet run --project CRM.API
   ```
   The API listens on http://localhost:5208. Health check: `GET /api/health`.

Blob storage is optional. With no `BlobStorage` config the API still starts, and file uploads
return 503 "not configured".

## Tests

```bash
dotnet test
```

## Migrations

```bash
dotnet ef migrations add <Name> --project CRM.Persistence --startup-project CRM.API
dotnet ef database update --project CRM.Persistence --startup-project CRM.API
```

Production does **not** migrate on boot (`Database:MigrateOnStartup` is false outside
Development). The API refuses to start while migrations are pending.

## Deployment

Pushing to `main` deploys to production via `.github/workflows/main_ssp-api.yml`
(build → test → publish `CRM.API` only → Azure App Service).

When a change includes a migration, the order is:

1. Apply the migration to the production database (`dotnet ef database update`).
2. Push this repo.
3. Push the frontend, if it depends on the change.

Expect a few minutes of 503s while the container restarts after each deploy.
