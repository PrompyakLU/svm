# Structure

## Summary
- **Repo type**: Single repository, separate `web/` (Angular) and `api/` (ASP.NET Core, 4-project solution) applications; database managed via EF Core migrations.
- **Key source directories**: `web/src/app/` (core, shared, features, mock-data), `api/src/` (Api, Application, Domain, Infrastructure), `docs/adr/` (decisions).
- **Main entry points**: Angular `web/src/main.ts`, ASP.NET Core `api/src/SvmApi.Api/Program.cs`.

## Repository
- **Type**: Single repo (web + api side by side). Layout confirmed during design (D3-1).
- **Root**: `git init -b main` at project root at setup (per workshop policy), with `.gitignore`.

## Key Directories
| Directory | Purpose | Key Contents |
|-----------|---------|--------------|
| `web/src/app/core/` | Angular cross-cutting | auth service, HTTP interceptor, route guards, API clients |
| `web/src/app/shared/` | Shared UI | components, shared styling, pipes; follow the [current design system](../../.kiro/steering/design-system.md) |
| `web/src/app/features/` | Feature views F2–F8 | dashboard, vulnerabilities, intake, remediation, verification, admin, audit |
| `web/src/app/mock-data/` | FE+mock stage | `*.json` seed data before backend wired |
| `api/src/SvmApi.Api/` | Web API host | controllers, `Program.cs`, middleware, auth policies, health |
| `api/src/SvmApi.Application/` | Use-case services | Intake, DataQuality, RiskScoring, Workflow, Sla, Verification, Assignment, Dashboard, Admin; DTOs; interfaces |
| `api/src/SvmApi.Domain/` | Domain core | entities, enums, domain rules, invariants (no outward deps) |
| `api/src/SvmApi.Infrastructure/` | Data + adapters | EF Core DbContext/configurations/repositories; Integration mock adapters + seed; local auth; audit hash-chain |
| `api/tests/SvmApi.Tests/` | xUnit tests | risk scoring, dedup, transition guard, SLA calc |
| `docs/adr/` | Architecture Decision Records | ADRs: baseline, SQL Server, local-auth |

## Key Files
| File | Purpose | Notes |
|------|---------|-------|
| `.gitignore` | VCS ignore | created at setup |
| `web/package.json` + `package-lock.json` | web deps | committed lockfile, Node 24 LTS |
| `api/global.json` | pin .NET SDK | required by baseline |
| `api/Directory.Packages.props` + `packages.lock.json` | pin NuGet versions | locked restore |
| (OpenAPI spec, EF migrations, design-system `.css`) | — | created during design/implement |

## Entry Points
| Entry | Type | Description |
|-------|------|-------------|
| `web/src/main.ts` | Angular SPA bootstrap | serves SVM UI |
| `api/Program.cs` | ASP.NET Core host | REST `/api/v1`, OpenAPI, auth, telemetry |

## Module Dependencies
Layered/Clean-ish (D3-2), dependencies point inward (Domain has no outward deps):
```
web (Angular SPA)
   │  REST /api/v1 (same-origin)
   ▼
SvmApi.Api (controllers, auth policies, health)
   ▼
SvmApi.Application (use-case services + interfaces:
   IAssetRepository, ICmdbAdapter, IAuthProvider, ...)
   ▼
SvmApi.Domain (entities, invariants)  ◄── Infrastructure implements interfaces
   ▲
SvmApi.Infrastructure (EF Core repos, mock adapters+seed, local auth, audit hash-chain)
   ▼
EF Core → SQL Server
```

## Data Flow
N/A — greenfield. Target request lifecycle:
```
Angular component → Angular service (HTTP)
   → API controller (/api/v1) → authorization policy (RBAC + data scope)
   → application/domain service → EF Core repository → SQL Server
   → ProblemDetails on error · audit log on consequential actions
```

## Key Abstractions
| Abstraction | Role |
|-------------|------|
| `IAuthProvider` | auth seam — local JWT impl now, OIDC-swappable later |
| `IExternalXxx` / `ICmdbAdapter` adapters | external-system seam — mock impl + seed, toggled by `Integration__UseMock` |
| `RiskScoringEngine` | weighted-sum service, weights from `config_` table, normalize 0–100 |
| Workflow state machine | table-driven transitions + guards (config/DB) |
| Audit hash-chain | append-only, hash-chained audit entries (tamper-evident) |
| Repository interfaces | Application-defined, EF Core-implemented in Infrastructure |

## Test Organization
N/A — greenfield. Planned: web unit tests (Angular CLI/Vitest) + Playwright flows; API xUnit tests. Per workshop policy, test/verify is scheduled late/optional with Working-Software verification as the minimum gate.

## Build & Deploy
N/A — greenfield. Planned: web + api OCI images; deploy phase optional (last).
