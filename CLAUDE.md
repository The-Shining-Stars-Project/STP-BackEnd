# Shining Stars CRM — API

Backend for the Shining Stars Project CRM: an internal tool for a performing-arts non-profit
serving youth across programs (MJC, Manteca PT, Pathways). It covers stars (participants),
staff onboarding and compliance, attendance, weekly progress scores, planning, roster,
scripts, calendar, and reports. **Live in production and used daily.**

The frontend lives in a separate repo, `STP-Front-End` (usually cloned next to this one as
`STP-FrontEnd/`). It has its own `CLAUDE.md`.

Stack: ASP.NET Core (.NET 10), EF Core 9, Azure SQL, xUnit.

## Production safety: read before running anything

- **Do not `dotnet run` the API against a remote database.** `launchSettings.json` forces
  Development, which overrides any `ASPNETCORE_ENVIRONMENT` in the shell. Development also
  applies pending migrations on boot (`Database:MigrateOnStartup` defaults to true there) and
  runs the seeders. Program.cs skips seeding when the connection string contains
  `database.windows.net`, but the migration-on-boot still applies. Only run locally against a
  local SQL Server.
- `dotnet ef database update` applies migrations to whatever the user-secrets connection
  string points at, which may be **production**. Only run it when the user asks.
- Pushing to `main` **auto-deploys to production** (`.github/workflows/main_ssp-api.yml`:
  build → test → publish CRM.API only → deploy). Commit or push only when asked.
- **Deploy order when a migration is involved:** apply the migration → push the API → push the
  frontend. The API refuses to start with pending migrations (MigrateOnStartup is false in
  prod), and a frontend that calls new endpoints needs the new API first.
- A 503 or empty-body 500 for a few minutes after a deploy is the container restart. Wait
  before diagnosing.
- Secrets live in .NET user secrets and Azure app settings, never in files. `appsettings.json`
  is gitignored, and `appsettings.json.template` documents every setting. This repo is public.

## Vocabulary

- **Star** in the UI = `Participant` in code. Never rename the entity, and never say
  "student", "client", or "case" in user-facing text.
- **Sites = programs** in the client's language. The `Site` entity is a separate thing:
  classroom locations hanging off roster assignments and events.
- Games = "Curriculum Resources", Scripts = "Scripts & Lesson Plans", the progress tracker
  grid = "Weekly Data".
- Program **Track** (`PartTime` | `Pathways`) decides which progress framework applies. Never
  branch on program slug. Programs are created by hand in production, so slugs are
  unpredictable (`manteca-pt`, `pathways:-manteca`). Use `CrmProgram.Track`.

## How a feature flows

Clean Architecture: `API → Application → Domain`, with `Persistence` and `Infrastructure`
implementing Application interfaces.

1. **Entity** in `CRM.Domain/Entities` (inherits `BaseEntity`). Enums live in `CRM.Domain/Enums`
   and are stored as **ints**, so always append new values at the END. Adding a value needs
   no migration.
2. **EF config** in `CRM.Persistence/Configurations`, plus a `DbSet` in `AppDbContext`.
   Soft-deleted entities (`Participant`, `Volunteer`) use `IsDeleted` and a `HasQueryFilter`.
3. **DTOs** in `CRM.Application/DTOs/<Area>`, and a **service** plus interface in
   `CRM.Application/Services` and `Interfaces/Services`, registered in
   `CRM.Application/DependencyInjection.cs`. Data access goes through
   `IRepository<T>` / `IUnitOfWork`; read-heavy stats go in `CRM.Persistence/Queries`.
4. **Controller** in `CRM.API/Controllers`. Every endpoint needs:
   - Auth: bare `[Authorize]` (any signed-in user, but scoped), `[Authorize(Policy = "ManagementWrite")]`
     (Admin role, or staffRole Coordinator / TechnologySystemsCoordinator / Admin), or
     `[Authorize(Roles = "Admin")]`.
   - `[Audited("area.action", "Entity")]` on reads and writes (see `CRM.API/Auditing`).
   - Validation failures throw `ArgumentException` → 400 via `GlobalExceptionHandler`.
5. **Program scoping**: any list or read a Staff-role user can reach must go through
   `IProgramAccessService` (`ForUserAsync`, `RequireParticipantAsync`). An unscoped endpoint is
   a data-leak bug. That has happened before (program detail, cohort roll-up).
6. **Tests** in `CRM.Tests` (xUnit, hand-written fakes in `Fakes.cs`). Add one per rule you
   introduce. CI runs `dotnet test` before deploying.
7. **Migration**: `dotnet ef migrations add <Name> --project CRM.Persistence --startup-project CRM.API`.
   New columns that existing rows depend on need a correct default or a backfill. For example,
   `Sites.IsActive` once defaulted to false and "retired" every existing site.
8. When DTOs change, the frontend's hand-kept types in `STP-FrontEnd/lib/types/api.ts` must
   change too.

## Client rules that aren't obvious from the code

- Star documents and staff onboarding data are **Admin-only**, reads included.
- Delete is **soft delete** for stars and volunteers. Don't build hard delete (client decision).
- Attendance counts for **Active and Attention** stars only. `Rescheduled` and
  `NotScheduled` count as marked but are excluded from rates, and only managers may set them.
  Teachers can submit a session; only managers can reopen one.
- Teacher visibility = the programs their linked `StaffMember` is assigned to. A user with no
  linked staff record sees nothing; that's a setup issue, not a bug. Former staff (with
  `EndDate`) lose access.
- Dual enrollment is `Participant.SecondaryProgramId`. Roster rows are per star per program.
- Notifications are in-app only (no email).

## Before handing work back

Run `dotnet build` and `dotnet test`. Both must be green.

## Debugging lessons

- "Data missing for a teacher": check the staff link, program assignment, and roster quarter
  before touching code.
- "Broken right after deploy": check that the migration was applied.
