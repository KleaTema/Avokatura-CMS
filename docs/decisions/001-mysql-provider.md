# 001 — MySQL provider for EF Core

**Decision:** use `MySql.EntityFrameworkCore` (Oracle).

**Reason:** the latest `Pomelo.EntityFrameworkCore.MySql` on NuGet is 9.0.0 (targets EF Core 9) and no version for EF Core 10 exists yet. The project is on .NET 10 / EF Core 10, and Oracle's provider publishes a 10.x release.

**Revisit:** if Pomelo releases an EF Core 10 version, re-evaluate. Schema is owned by DbUp scripts, so switching providers only affects the DbContext configuration.

The package is not referenced yet; it will be added (with its version in `Directory.Packages.props`) by the first task that introduces a DbContext.
