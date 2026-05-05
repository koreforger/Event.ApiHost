# Event.ApiHost — Specification

Status: Draft v1.0
Owner: Event team
Branch: `development`
Visibility: public (workspace-public; the controllers it hosts may come from private packages)

## 1. Purpose

`Event.ApiHost` is a self-hosted ASP.NET Core 10 application that exposes the controllers shipped by `Event.FraudIntegrationControllers.*` packages as an HTTPS API. It is the only entry point to the fraud-integration surface and is responsible for:

- Process hosting (Kestrel, no IIS, no reverse-proxy assumption).
- Authentication (JWT bearer for users, static API key for services).
- Authorization (per-method, KoreForge.Web dynamic rules).
- Configuration (SQL Server only; no JSON/YAML/env settings beyond a single bootstrap DB connection string).
- Application lifecycle (startup/shutdown/scheduled flows via KoreForge.AppLifecycle).
- API documentation UI (Scalar) and a custom nginx-mapping view.
- Logging (KoreForge.Logging.Serilog) and metrics (KoreForge.Metrics.AspNet).

## 2. Non-goals

- Does not generate controllers; consumes them via NuGet from `Event.FraudIntegrationControllers.*`.
- Does not own the upstream Nedbank API contracts.
- Does not perform DB schema migrations. Schema is owned externally and reverse-engineered into EF Core via `dotnet ef dbcontext scaffold` (the `koreforge-data` pattern).
- Does not run behind IIS. Kestrel is the production listener.
- Does not auto-create or auto-rotate certificates. The port-reservation script picks an existing cert from the Windows certificate store.

## 3. Repository layout

```text
event/Event.ApiHost/
  README.md
  LICENSE.md
  Event.ApiHost.slnx
  Directory.Build.props
  Directory.Packages.props
  NuGet.config
  .gitignore
  .github/
    workflows/
      ci.yml
  doc/
    specification.md
    user-guide.md
    developer-guide.md
    structure.md
    notes/
      implementation-plan.md
      port-reservation-runbook.md
  database/
    scripts/                                   # bootstrap-only: smoke-check the DB exists and the
      000-check-required-tables.sql            # required tables (owned by Event.FraudIntegration.Data)
                                               # are present. The schema itself is owned by
                                               # Event.FraudIntegration.Data/database/scripts.
    seed/
      dev-bootstrap.sql                        # local dev only (Docker SQL): inserts seed rows into
                                               # Settings, ServiceClients, NginxUpstreams,
                                               # ExternalApiAuth, MethodPermissionRules
  scr/
    build-clean.ps1
    build-rebuild.ps1
    build-test.ps1
    build-test-codecoverage.ps1
    build-integration.ps1
    build-pack.ps1
    build-publish.ps1
    koreforge-build.psm1
    reserve-https-port.ps1                     # Windows: cert picker + netsh sslcert + urlacl
    verify-database.ps1                        # bootstrap smoke check; schema is owned by Event.FraudIntegration.Data
  src/
    Event.ApiHost/
      Event.ApiHost.csproj
      Program.cs
      AssemblyMarker.cs
      Composition/
        WebApplicationBuilderExtensions.cs
        AuthenticationConfiguration.cs
        AuthorizationConfiguration.cs
        SwaggerConfiguration.cs
        ScalarConfiguration.cs
        LifecycleConfiguration.cs
        ControllerRegistration.cs
      Authentication/
        ServiceApiKeyAuthenticationHandler.cs
        ServiceApiKeyAuthenticationOptions.cs
        ServiceApiKeyDefaults.cs
        ServiceClientStore.cs
      Authorization/
        Permissions.cs
        MethodPermissionRuleFactory.cs
      Lifecycle/
        Startup/
          ValidateConfigurationStep.cs
          WarmUpExternalClientsStep.cs
          DummyStartupStep.cs
        Shutdown/
          DrainInflightRequestsStep.cs
          DummyShutdownStep.cs
        Scheduled/
          RefreshExternalAuthStep.cs
          DummyScheduledStep.cs
          IntervalTriggers/
            FifteenMinuteTrigger.cs
      Scalar/
        ScalarNginxEndpoint.cs
      Settings/
        AppSettingsRepository.cs               # reads `Settings` rows from DB
        ISettingsRepository.cs
  tst/
    Event.ApiHost.Tests/
      Event.ApiHost.Tests.csproj
      ...
    Event.ApiHost.Integration.Tests/
      Event.ApiHost.Integration.Tests.csproj
      Infrastructure/
        SqlServerFixture.cs                    # Testcontainers.MsSql
        ApiHostFactoryFixture.cs               # WebApplicationFactory<Program>
      ...
```

Conventions:

- `slnx` is `Event.ApiHost.slnx` (full dotted name; consistent with the spec for `KoreForge.SwaggerControllers` and `Event.FraudIntegrationControllers`).
- One top-level type per file. `Program.cs` is the only exception by ASP.NET convention (top-level statements).
- The bootstrap configuration source is exactly **one** `appsettings.json` containing **only** `BootstrapConnectionString` and `Logging:Bootstrap:MinimumLevel`. Every other setting comes from SQL.

## 4. Bootstrap configuration

The only file-based settings are:

```json
{
  "BootstrapConnectionString": "Server=...;Database=ApiHost;...",
  "Logging": { "Bootstrap": { "MinimumLevel": "Information" } }
}
```

Resolution order:

1. `appsettings.json` (committed; placeholder values in the repo).
2. Environment-specific override file `appsettings.<EnvironmentName>.json` (committed `.example`, gitignored real).
3. Environment variables prefixed `EVENT_APIHOST_` (e.g. `EVENT_APIHOST_BootstrapConnectionString`).

User-secrets is not supported — local secrets go in environment variables or in the bootstrapped DB.

After bootstrap, `AppSettingsRepository` loads `Settings` rows on startup and exposes them via `IOptionsMonitor<T>` snapshots that refresh on a 15-minute scheduled flow.

## 5. Database schema (DB-only configuration)

All persistent app configuration lives in tables owned by the `Event.FraudIntegration.Data` package (separate repo, separate NuGet). This host only **consumes** the package's `FraudIntegrationDbContext` and its repositories. The schema below is reproduced for cross-reference; the canonical definition lives in `event/Event.FraudIntegration.Data/database/scripts/*.sql`.

| Table | Purpose | Key columns |
|---|---|---|
| `Settings` | Generic key/value config: HTTP listen port, JWT issuer/audience, JWT signing key, log level overrides, Scalar enabled/disabled. **No cert thumbprint** (cert is bound by OS via netsh). | `Key NVARCHAR(200) PK`, `Value NVARCHAR(MAX)`, `Category`, `UpdatedUtc` |
| `ServiceClients` | Service-to-service API key allow-list. | `ClientId NVARCHAR(100) PK`, `ApiKeyHash VARBINARY(64)`, `Salt VARBINARY(32)`, `Iterations INT`, `Enabled BIT`, `AllowedRoutesPattern NVARCHAR(500)` |
| `NginxUpstreams` | Upstream nginx server entries shown by `/scalar/nginx`. | `Name NVARCHAR(100) PK`, `BaseUrl NVARCHAR(500)`, `Environment NVARCHAR(50)`, `DisplayOrder INT` |
| `ExternalApiAuth` | Per-API external auth options for `AuthDelegatingHandler`. | `ApiName NVARCHAR(100)`, `Version NVARCHAR(10)`, `Mode NVARCHAR(30)`, `JwtToken NVARCHAR(MAX)`, `ClientId NVARCHAR(200)`, `ClientSecret NVARCHAR(MAX)`, `BaseAddress NVARCHAR(500)`. PK = `(ApiName,Version)`. Stored secrets encrypted via DPAPI or referenced by Azure Key Vault URI. |
| `MethodPermissionRules` | Persisted method permission rules — read at startup and on scheduled refresh, used to construct the in-memory `MethodPermissionRule[]` for `KoreForge.Web`. | `Id INT PK`, `TypeFullName NVARCHAR(500)`, `MethodName NVARCHAR(200)`, `RoleRuleKind NVARCHAR(20)` (`AnyOf`/`AllOf`/`NotAnyOf`), `Roles NVARCHAR(MAX)` (comma-sep), `Enabled BIT` |
| `RequestAudit` | Structured request/response audit log written by a per-API `PersistenceDecorator` in `Event.FraudIntegrationControllers` via the shared `IRequestAuditRepository` from `Event.FraudIntegration.Data`. | `Id BIGINT IDENTITY PK`, `CorrelationId UNIQUEIDENTIFIER`, `ApiName`, `Operation`, `RequestUtc`, `DurationMs`, `Status`, `ErrorOrigin`, `ErrorKind`, `Principal NVARCHAR(200)`, `ServiceClientId NVARCHAR(100)` |

`MethodPermissionRules` is loaded into memory at startup. It is refreshed on a 15-minute scheduled flow. The `Phishing.Administrator -> *` row is seeded by the corresponding script in `Event.FraudIntegration.Data/database/scripts/`.

EF Core usage follows the **`koreforge-data` pattern** but is owned by the **`Event.FraudIntegration.Data`** package (see [its specification](../../Event.FraudIntegration.Data/doc/specification.md)). Entities + DbContext are reverse-engineered there via `dotnet ef dbcontext scaffold`. This host only references the package and consumes the repositories. No EF migrations are used by anyone.

## 6. Authentication

Two schemes are registered side by side; per-endpoint authorization can demand either.

### 6.1 JWT bearer (users)

- Provider-agnostic; configured from `Settings`-table values: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`.
- `RoleClaimType = ClaimTypes.Role`. Multiple `role` claims are supported.
- Roles are extracted via `IPrincipalRoleResolver` (default impl reads `ClaimTypes.Role` claims, dedupes case-insensitively). The interface lives in `KoreForge.SwaggerControllers.Abstractions` and is consumed here.

### 6.2 Service-to-service API key (services)

- Custom authentication handler `ServiceApiKeyAuthenticationHandler` registered as scheme `ServiceApiKey`.
- Reads header `X-Service-Key`.
- Looks up the key in `ServiceClients` by hashing the presented key with the row's salt + iterations (PBKDF2-SHA512) and comparing to `ApiKeyHash` in constant time.
- On success, emits a `ClaimsPrincipal` with claims `ClientId`, `Role = "Service:<ClientId>"`. The `Service:` role prefix lets `MethodPermissionRules` grant service-only access without colliding with user roles.
- `AllowedRoutesPattern` is a glob (e.g. `api/party/*,api/person/*`) checked by an authorization handler before any controller logic runs. Mismatch → 403.

### 6.3 Scheme selection

- All controller actions accept either scheme: `[Authorize(AuthenticationSchemes = "Bearer,ServiceApiKey")]`. This attribute is **not** emitted by the generator (the generator emits `[Authorize]` only). It is added by the host via an `IAuthorizationHandler` + `AuthenticationSchemeProvider` shim — no controller modification needed.

## 7. Authorization

- Library: `KoreForge.Web.Authorization` (`AddDynamicMethodAuthorization` + `UseDynamicMethodAuthorization`).
- Rules constructed at startup from the `MethodPermissionRules` SQL table by `MethodPermissionRuleFactory`.
- Seed rule: `Phishing.Administrator -> *` (`AnyOf` over an empty `MethodName`/`TypeFullName` filter — interpreted as a global allow when the role is present).
- A 15-minute scheduled flow `RefreshMethodPermissionRulesStep` reloads rules and atomically swaps the in-memory store. Concurrent requests use the snapshot they began with.

## 8. Lifecycle (KoreForge.AppLifecycle)

The host wires three flows. Each has one real step plus one `Dummy*Step` (to prove the pattern is exercised in tests).

### Startup

```csharp
options.Startup.Flow("Bootstrap")
    .BeginWith<ValidateConfigurationStep>()
    .Then<WarmUpExternalClientsStep>()
    .Then<DummyStartupStep>()
    .EndFlow();
```

`FailFastOnStartupFailure = true` (default) — a failed step prevents the host from accepting traffic.

### Shutdown

```csharp
options.Shutdown.Flow("Drain")
    .BeginWith<DrainInflightRequestsStep>()
    .Then<DummyShutdownStep>()
    .EndFlow();
```

### Scheduled

```csharp
options.Scheduled.Flow("RefreshSettings")
    .OnSchedule<FifteenMinuteTrigger>()
    .NoOverlap()
    .BeginWith<RefreshExternalAuthStep>()
    .Then<RefreshMethodPermissionRulesStep>()
    .Then<DummyScheduledStep>()
    .EndFlow();
```

`FifteenMinuteTrigger` returns `TimeSpan.FromMinutes(15)` from `GetNextDelayAsync` — there is no built-in interval primitive in `KoreForge.AppLifecycle`, so this is the canonical local pattern.

## 9. Controller registration (opt-in only)

`Composition/ControllerRegistration.cs` exposes:

```csharp
public static IMvcBuilder AddEventApiHostControllers(this IMvcBuilder builder)
{
    return builder.AddSwaggerControllers<Event.FraudIntegrationControllers.AssemblyMarker>();
    // future packages add additional .AddSwaggerControllers<...>() calls here
}
```

`Program.cs` calls this exactly once during composition. Adding a new controller package requires editing this single file and adding a `PackageReference` — there is no auto-discovery.

## 10. Scalar UI and `/scalar/nginx`

- `Scalar.AspNetCore` registered at `/scalar/v1`, reading the live `/swagger/v1/swagger.json` produced by `Swashbuckle.AspNetCore`.
- A custom endpoint `GET /scalar/nginx` (handled by `ScalarNginxEndpoint`) emits a small, dependency-free HTML page that:
  - Loads `swagger.json` once per request.
  - Loads `NginxUpstreams` rows from DB.
  - For each upstream, renders a section listing every API route mapped to `<upstream.BaseUrl>/<routeTemplate>`.
  - Rendering is server-side (string interpolation, no JS framework). Page size is tiny.
- Both endpoints are gated by JWT auth and require role `Phishing.Administrator` or `Service:<docs-reader>`.

## 11. HTTP server, HTTPS, and ports

The server abstraction is **HTTP.sys** (`UseHttpSys()`) in production on Windows; plain Kestrel HTTP in development.

- **Production (Windows)**: `UseHttpSys()`. URL prefix `https://+:<port>/` where `<port>` is read from `Settings.HttpsPort` (default `8443`). The HTTPS cert binding lives entirely in the OS (`netsh http add sslcert`). The application code **does not load, validate, or know the cert thumbprint** — HTTP.sys terminates TLS using whatever cert is bound at the `ipport`. This satisfies the workspace's DB-only-config rule by removing the cert from the DB entirely (it is OS configuration, not application configuration).
- **Development**: plain Kestrel HTTP on `Settings.HttpPort` (default `5080`). Toggled by `IHostEnvironment.IsDevelopment()`.
- HSTS enabled in production with `MaxAge = 365 days`.

`scr/reserve-https-port.ps1` (Windows-only, run once per host or whenever the cert rotates):

1. Lists all certs in `LocalMachine\My` using `Out-ConsoleGridView -OutputMode Single` (workspace-standard UI module). Display columns: Subject, Issuer, NotAfter, Thumbprint.
2. Captures the user's selected cert.
3. Reserves the URL and binds the cert (idempotent: deletes any prior binding on the same `ipport` first):
   ```text
   netsh http delete sslcert ipport=0.0.0.0:<port>
   netsh http delete urlacl  url=https://+:<port>/
   netsh http add    urlacl  url=https://+:<port>/ user="NT AUTHORITY\NETWORK SERVICE"
   netsh http add    sslcert ipport=0.0.0.0:<port> certhash=<thumbprint> appid={<guid>}
   ```
4. Prints the resulting bindings (`netsh http show sslcert`, `netsh http show urlacl`) for confirmation.
5. Requires Administrator. Refuses to run otherwise with a clear error.
6. Documented in `doc/notes/port-reservation-runbook.md`.

The script does **not** touch the database. Cert rotation is: re-import the new cert into `LocalMachine\My`, re-run the script, restart the host (so HTTP.sys picks up the new binding cleanly).

## 12. Logging and metrics

- Logging: `KoreForge.Logging.Serilog`. Sinks: Console (always) + Rolling File (path from `Settings`).
- Metrics: `KoreForge.Metrics.AspNet`. `/metrics` endpoint protected by `Service:metrics-scraper` role.
- Every controller call emits a `LogHelper.LogRequest`/`LogResponse` pair carrying `CorrelationId` (the `Outcome<T>.CorrelationId` value).

## 13. Testing

### Unit — `Event.ApiHost.Tests`

- `ServiceApiKeyAuthenticationHandlerTests` (PBKDF2 verification, route glob check, missing-header path).
- `MethodPermissionRuleFactoryTests` (DB row → `MethodPermissionRule` mapping; bad rows are skipped with a warning, not a crash).
- `AppSettingsRepositoryTests`.
- `Lifecycle/*StepTests`.
- `ScalarNginxEndpointTests` (renders expected HTML against a fake settings + fake swagger doc).

### Integration — `Event.ApiHost.Integration.Tests`

- `SqlServerFixture` using `Testcontainers.MsSql` (workspace-standard pattern).
- `ApiHostFactoryFixture` using `WebApplicationFactory<Program>` overriding `BootstrapConnectionString` to point at the Testcontainers SQL instance, then applying `Event.FraudIntegration.Data/database/scripts/*.sql` against it before each test class.
- End-to-end tests:
  - JWT-bearer happy path returns 200 with valid role.
  - JWT-bearer wrong role returns 403.
  - Service-API-key happy path returns 200 with allowed route, 403 with disallowed route.
  - Missing both schemes returns 401.
  - `/scalar/v1` reachable and contains expected swagger doc reference.
  - `/scalar/nginx` lists rows seeded into `NginxUpstreams`.
  - Settings change in DB → IOptionsMonitor snapshot picks it up after the scheduled refresh fires (test invokes the step manually).
- Excluded from `dotnet test` by filename containing `Integration` (workspace convention).
- Coverage target (unit + integration): **75% line** on non-generated source.

## 14. Constraints and operational rules

- **Configuration in DB only** (non-negotiable). Sole exception: bootstrap connection string + bootstrap log level.
- **No production-code retries for infra readiness** (per workspace user-memory). DB connection failure on startup → fail fast. The deploy runbook ensures DB is up before host starts.
- **Infra-first thinking**: the SQL schema scripts and `scaffold-db.ps1` must run successfully before the host can be built locally.
- **No `ProjectReference`**: every dependency is a NuGet package, including `Event.FraudIntegrationControllers.*`.
- **No vendoring**.
- **One top-level type per file** (Program.cs excepted).
- **Filename word order** follows established workspace patterns (`build-test.ps1`, `publish-nuget.yml`, `scaffold-db.ps1`, `reserve-https-port.ps1` — verb first).

## 15. Acceptance criteria

1. With a SQL database whose schema is established by running `Event.FraudIntegration.Data/database/scripts/*.sql` and seeded by `database/seed/dev-bootstrap.sql`, `dotnet run` brings the host up and `GET /scalar/v1` returns 200.
2. Removing `AddEventApiHostControllers()` from `Program.cs` yields zero controller routes (verified by an integration test).
3. JWT with role `Phishing.Administrator` accesses every action; without that role it gets 403.
4. Valid `X-Service-Key` matching an `Enabled` `ServiceClients` row with a permissive `AllowedRoutesPattern` accesses any matching action; rotation by updating the row + restart works.
5. Updating a row in `Settings` (e.g. log level) is reflected after at most one scheduled refresh cycle without a host restart.
6. `scr/reserve-https-port.ps1` on a clean Windows host produces a working `https://localhost:<HttpsPort>/scalar/v1` page **without writing anything to the database**. Re-running it after importing a new cert into the store and restarting the host swaps the cert with no application changes.
7. All KoreForge.AppLifecycle flows execute their dummy step at least once during a single integration test run (verified by counters in the dummy steps).
8. Unit + integration test coverage ≥ 75%.
