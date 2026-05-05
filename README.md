# Event.ApiHost

Self-hosted ASP.NET Core (Kestrel) application that exposes the [Event.FraudIntegrationControllers](../Event.FraudIntegrationControllers) API surface, secured by JWT-bearer + service-to-service API key, with all variable configuration sourced from a SQL Server database.

Includes:

- Explicit, opt-in registration of every controller assembly.
- `KoreForge.Web` dynamic method authorization with the seed role `Phishing.Administrator`.
- `KoreForge.AppLifecycle` flows for startup, shutdown, and scheduled work — with one dummy step per category demonstrating the pattern.
- Scalar UI at `/scalar/v1` plus a custom `/scalar/nginx` page driven from DB-stored upstream config.
- A Windows port-reservation + cert-binding script with an interactive certificate picker.

See [doc/specification.md](doc/specification.md), [doc/structure.md](doc/structure.md), and [doc/notes/implementation-plan.md](doc/notes/implementation-plan.md).
