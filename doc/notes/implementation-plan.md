# Event.ApiHost — Implementation Plan

Sequences the build of [specification.md](../specification.md). Depends on `KoreForge.SwaggerControllers.Abstractions` (NuGet, public), `KoreForge.Web.Authorization` (NuGet, public), `KoreForge.AppLifecycle` (NuGet, public), `KoreForge.Logging.Serilog`, `KoreForge.Metrics.AspNet`, `Event.FraudIntegration.Data` (NuGet — owns the DbContext, entities, repositories, and SQL scripts), and the private package set `Event.FraudIntegrationControllers.*`.

## Phase 0 — Repo bootstrap

1. Create GitHub repo `koreforger/Event.ApiHost` (public). Default branch `main`. Open `development`.
2. Add the standard skeleton: `README.md`, `LICENSE.md`, `.gitignore`, `Directory.Build.props`, `Directory.Packages.props`, `NuGet.config`, `Event.ApiHost.slnx`.
3. Copy `scr/koreforge-build.psm1` from a sibling eco-system repo verbatim. Add the standard wrappers (`build-clean.ps1`, `build-rebuild.ps1`, `build-test.ps1`, `build-test-codecoverage.ps1`, `build-integration.ps1`, `build-pack.ps1`, `build-publish.ps1`).
4. Add `.github/workflows/ci.yml` (build + test, ignore `Integration` projects).
5. Place a sample `appsettings.json` with placeholder `BootstrapConnectionString`.

## Phase 1 — Database availability

This host does **not** own the schema. The `Event.FraudIntegration.Data` repo (separate package) owns `database/scripts/*.sql` + scaffolded entities + `FraudIntegrationDbContext` + repositories. This phase only ensures a usable instance exists.

1. Spin up local SQL via `docker/docker-compose.yml` (workspace stack on host port 14334).
2. Apply `Event.FraudIntegration.Data/database/scripts/*.sql` against the local DB (run from that repo's `scr/apply-database-scripts.ps1`).
3. Author `database/seed/dev-bootstrap.sql` here with realistic dev rows for `Settings`, `ServiceClients`, `NginxUpstreams`, `ExternalApiAuth`, `MethodPermissionRules`.
4. Author `scr/verify-database.ps1` — a smoke check that opens the bootstrap connection string and asserts the required tables exist; fails fast if not. This is a **bootstrap diagnostic**, not a schema deployer (per the workspace's infra-first rule).
5. Add `PackageReference` to `Event.FraudIntegration.Data` and confirm `dotnet build` succeeds.
## Phase 2 — Bootstrap composition

1. `Program.cs`: build `WebApplicationBuilder`, load bootstrap config, register Serilog, register `FraudIntegrationDbContext` via `AddFraudIntegrationData(connectionString)` (extension shipped by the data package), register `AppSettingsRepository`, hand off to composition extensions.
2. `Composition/WebApplicationBuilderExtensions.cs`:
   - `ConfigureBootstrapLogging`
   - `ConfigureSettingsAndOptions`
3. `Settings/AppSettingsRepository.cs` reads `Settings` table on first call; exposed as `IOptionsMonitor<>` snapshots through small typed options classes.
4. Smoke test: `dotnet run` against the dev bootstrap database — host starts, `/healthz` returns 200, no auth wired yet.

## Phase 3 — Authentication

1. `Authentication/ServiceApiKeyAuthenticationHandler.cs` + `Options` + `Defaults`.
2. `Authentication/ServiceClientStore.cs` reads from `ServiceClients`, caches snapshot, refreshed on scheduled flow.
3. PBKDF2-SHA512 hash verify with constant-time compare (`CryptographicOperations.FixedTimeEquals`).
4. `Composition/AuthenticationConfiguration.cs` registers JWT bearer + the `ServiceApiKey` scheme, both as auth providers; reads JWT issuer/audience/signing key from settings.
5. Tests for both schemes (unit + integration `WebApplicationFactory`).

## Phase 4 — Authorization

1. `Composition/AuthorizationConfiguration.cs` calls `AddRoleAuthorizationCore()` + `AddDynamicMethodAuthorization(rules)`.
2. `Authorization/MethodPermissionRuleFactory.cs` maps `MethodPermissionRules` rows → `MethodPermissionRule[]`. Bad rows logged + skipped (don't crash startup).
3. Pipeline order in `Program.cs`: `UseAuthentication` → `UseDynamicMethodAuthorization` → `UseAuthorization` (the established `KoreForge.Web` sample order).
4. Seed rule integration test: with only the seed row, `Phishing.Administrator` JWT bearer can call any endpoint; any other role gets 403.

## Phase 5 — Lifecycle flows

1. Add `KoreForge.AppLifecycle` package.
2. Implement steps under `Lifecycle/Startup/`, `Lifecycle/Shutdown/`, `Lifecycle/Scheduled/` per Section 8 of the spec.
3. Register in `Composition/LifecycleConfiguration.cs` with the fluent builder.
4. `FifteenMinuteTrigger` returns `TimeSpan.FromMinutes(15)`.
5. Integration test: each dummy step's invocation counter increments at least once across a single test run (startup + scheduled forced via reflection or via shrunk trigger interval).

## Phase 6 — Controllers + Swagger + Scalar

1. Add `PackageReference` to `Event.FraudIntegrationControllers.AssemblyMarker` + each per-API package the host needs.
2. `Composition/ControllerRegistration.cs` calls `AddSwaggerControllers<Event.FraudIntegrationControllers.AssemblyMarker>()`.
3. Wire `Swashbuckle.AspNetCore` (`AddSwaggerGen` + `UseSwagger`) to produce `/swagger/v1/swagger.json`.
4. Wire `Scalar.AspNetCore` (`MapScalarApiReference`) at `/scalar/v1`.
5. Implement `Scalar/ScalarNginxEndpoint.cs` (a `MapGet("/scalar/nginx", ...)` handler) reading `NginxUpstreams` + the swagger doc.
6. Integration tests for all three endpoints.

## Phase 7 — HTTP.sys, port reservation, hardening

1. `Composition/HttpServerConfiguration.cs` (added in Phase 2) calls `builder.WebHost.UseHttpSys(opts => opts.UrlPrefixes.Add($"https://+:{port}/"))` in production, plain Kestrel HTTP in development. **No cert lookup, no thumbprint, no PFX path** — HTTP.sys terminates TLS using the OS-bound cert.
2. `scr/reserve-https-port.ps1`:
   - Param: `-Port`, `-AppId` (defaults to a stable workspace-owned GUID).
   - List certs via `Get-ChildItem Cert:\LocalMachine\My`.
   - Display via `Out-ConsoleGridView -OutputMode Single` (workspace-standard UI module). Columns: Subject, Issuer, NotAfter, Thumbprint.
   - Run `netsh http delete sslcert ...` + `netsh http delete urlacl ...` then `netsh http add urlacl ...` + `netsh http add sslcert ...` against the user-picked thumbprint.
   - Print `netsh http show sslcert ipport=0.0.0.0:<port>` and `netsh http show urlacl url=https://+:<port>/` for confirmation.
   - Refuses to run without Administrator. **Does not touch the database.**
3. `doc/notes/port-reservation-runbook.md` — manual ops steps plus the script invocation, plus the cert-rotation runbook (re-import cert → re-run script → restart host).
4. Add HSTS in production code path.

## Phase 8 — Logging + metrics

1. Add `KoreForge.Logging.Serilog` and configure sinks.
2. Add `KoreForge.Metrics.AspNet` and protect `/metrics` with `Service:metrics-scraper`.
3. Decorate the request pipeline with a correlation-id middleware that uses (or generates) `Outcome<T>.CorrelationId`.

## Phase 9 — Documentation

1. `doc/user-guide.md` — how an operator deploys, configures DB, runs the port script, and verifies the host.
2. `doc/developer-guide.md` — local dev with Docker SQL, scaffold-db, bootstrap config, running tests, the dummy-step pattern.
3. `doc/structure.md` — folder map, dependency graph, runtime topology (auth + middleware + lifecycle ordering).
4. `doc/notes/port-reservation-runbook.md`.
5. Update `c:\My\KoreForge2\docs\ecosystem\documentation-inventory.md`.

## Phase 10 — Hardening + acceptance

1. Run all integration tests on a clean Windows machine and a clean Linux container.
2. Validate every spec acceptance criterion (Section 15) one by one. Capture results in a checklist commit message.
3. Tag `Event.ApiHost/v0.1.0`. There is no `publish-nuget.yml` for an app — release artifacts are produced by `scr/build-publish.ps1` (a self-contained `dotnet publish` plus a zip into `artifacts/zips/`).

## Cross-phase dependencies

```text
Phase 0 ──> Phase 1 ──> Phase 2 ──> Phase 3 ──> Phase 4 ──> Phase 5 ──> Phase 6 ──> Phase 7 ──> Phase 8 ──> Phase 9 ──> Phase 10
                                          │            │
                                          ↑            └── Requires KoreForge.Web.Authorization NuGet to be available.
                                          └── Requires KoreForge.SwaggerControllers.Abstractions + Event.FraudIntegrationControllers.AssemblyMarker NuGets to be available.
```

`Event.ApiHost` cannot start Phase 6 until at least one `Event.FraudIntegrationControllers.*` package is published (private feed or `artifacts/packages`). Phase 4 cannot start until `KoreForge.Web.Authorization` is consumable as a NuGet.

## Risks

- **`KoreForge.Web` does not currently expose service-to-service auth scaffolding.** This host implements its own `ServiceApiKey` scheme — no change to `KoreForge.Web` is required.
- **No certificate or netsh helper exists in the workspace** (per investigation). `reserve-https-port.ps1` is built from scratch — its design is in `port-reservation-runbook.md` and is reviewed before implementation.
- **Schema drift between `database/scripts/*.sql` and EF-scaffolded entities**: mitigated by an integration test that runs the scripts then runs `dotnet ef dbcontext scaffold` into a temp dir and asserts no diff vs the checked-in `Generated/`.
- **Scheduled flow timing in tests**: rather than wait 15 minutes, tests invoke the step instances directly; CI never depends on real elapsed time.
