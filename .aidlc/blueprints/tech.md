# Tech

## Summary
- **Stack**: Angular 22 (TS strict) + Tailwind 4 + daisyUI 5 (web) / ASP.NET Core .NET 10 Web API (nullable C#) + EF Core 10 / **SQL Server**
- **Architecture / API**: Layered modular monolith, separate web + API OCI images (same-origin routes), REST/JSON `/api/v1`, OpenAPI, `ProblemDetails`
- **Infra**: SQL Server; OCI containers; structured logging + OpenTelemetry

> Baseline source of truth: dipd-mcp `development-guideline` (approved baseline v1.0). Database = SQL Server (project decision, recorded here). Design-phase (D3) decisions now resolved — see "Design Decisions (D3)" below.

## Design Decisions (D3)
| Topic | Decision |
|-------|----------|
| Repo structure | Single repo: `web/` (Angular) + `api/` (.NET solution: `Api` / `Application` / `Domain` / `Infrastructure` projects) |
| API layering | Layered / Clean-ish (controllers → application services → domain → EF Core infrastructure) |
| Risk scoring engine | Weighted-sum service, weights in `config_` DB table, normalize 0–100, runtime recalculation on change |
| Workflow transitions | State-machine, table-driven (allowed transitions + guards in config/DB) |
| Mock adapters | `IExternalXxx` interface + Mock implementation + seed data; DI/config toggle (`UseMock`) — real adapter swaps in without touching business logic |
| Authentication | Local JWT behind `IAuthProvider` (OIDC-ready); policy-based authz + role/scope claims. No real IdP in workshop |
| API pagination | Offset pagination + query filters + sort |
| Audit immutability | Append-only, no update/delete, restricted DB permission, hash-chained entries |
| Observability | Minimal — structured logging + health endpoint |
| Error tracking | Log-based only |
| Testing scope | Minimal + verify-by-running (unit-test core logic: risk scoring, workflow transitions, audit hash chain) |

## Stack
- **Languages**: TypeScript (strict mode), C# (nullable enabled)
- **Frameworks**: Angular 22.x (standalone APIs), ASP.NET Core Web API on .NET 10 LTS, EF Core 10.x
- **Build system**: Angular CLI (web); .NET SDK (API)
- **Package manager**: npm (committed `package-lock.json`, Node.js 24 LTS); NuGet with `Directory.Packages.props` + locked restore (`packages.lock.json`), SDK pinned in `global.json`
- **Testing**: Angular CLI/Vitest unit tests + Playwright for critical browser flows (web); xUnit (API)

## Architecture
- **Pattern**: Layered modular monolith — Angular SPA → REST API controllers → application/domain services → EF Core repositories → SQL Server. Modules by domain (Asset, Vulnerability, Risk, Workflow, SLA, Governance, Dashboard, Access/Audit, Admin, Integration).
- **API style**: REST/JSON over HTTPS, versioned `/api/v1`, Controllers, OpenAPI spec, `ProblemDetails` for error responses

## Infrastructure
- **Cloud provider**: Workshop/local first (laptop dev). Deployment target per approved platform standards (deferred — deploy phase optional)
- **Compute**: Separate web and API OCI container images, same-origin routing
- **Database**: SQL Server (EF Core SqlServer provider). Day-1/local dev: SQL Server via `docker compose`, or SQLite only as a throwaway local fallback for FE+mock stage. Thai text columns use `nvarchar`.
- **IaC tool**: Will be defined during deploy phase (optional)

## Patterns & Conventions
- **Architecture pattern**: Layered/Clean-ish — controllers → application services → domain → EF Core infrastructure → SQL Server (API projects: `Api`/`Application`/`Domain`/`Infrastructure`)
- **Data access**: EF Core 10, explicit entity configuration (`IEntityTypeConfiguration<T>`, `ToTable(...)`), no pluralized default table names
- **API response format**: resource JSON (camelCase fields); errors via RFC 7807 `ProblemDetails`
- **Error handling**: centralized exception handling → `ProblemDetails`; ไม่เปิดเผย stack trace/secret/internal (NFR-40)
- **Authentication**: OIDC/SSO ตาม IdP ที่อนุมัติ (production). **Workshop/local**: mock/local auth provider ตามนโยบาย (ไม่ต่อ IdP จริง); อ่านค่า OIDC จาก configuration
- **Authorization**: policy-based (RBAC per role) + organizational data-scope segregation (business unit / asset / responsibility scope); ตรวจทุก function และ API ก่อนอนุญาต (NFR-11/12/13)
- **Validation**: input validation ทุก endpoint (ป้องกัน injection/XSS/CSRF); DTO validation ฝั่ง API
- **Logging**: structured JSON logs + OpenTelemetry; แยก application/integration/security/error log; audit log แยกและ immutable (NFR-20/21/25)
- **Code style**: TypeScript strict + Angular lint; C# nullable + analyzers
- **Naming conventions**: ยึด IRPC naming standard — variables/objects camelCase, classes/methods PascalCase, API JSON fields camelCase, API routes lowercase plural (`/api/v1/vulnerabilities`); DB per IRPC standard (`master_`/`config_`/`trans_` + lowerCamelCase), columns lowerCamelCase
- **Branch strategy**: git init `main` at setup (per workshop policy); trunk-based for workshop scope

## Environment & Configuration
- **Config approach**: environment variables + appsettings (API) / Angular environment files (web); ห้าม hardcode secret ใน source/config (NFR-16, security policy)
- **Environments**: local/dev (workshop); staging/prod ตาม platform standard (ภายหลัง)
- **Secrets management**: secret store (เช่น Secrets Manager / Parameter Store) — ไม่เก็บใน code/image/plaintext config

## CI/CD Pipeline
- **Tool**: ตามแนวทาง platform ที่อนุมัติ (deferred — deploy phase optional สำหรับ workshop)
- **Stages**: build → test → container image → deploy (ภายหลัง)
- **Deploy target**: OCI container runtime (ภายหลัง)

## Dependency Management
- **Lockfile**: `package-lock.json` (web), `packages.lock.json` + `global.json` (API) — committed
- **Version strategy**: pin major versions ที่ baseline; exact versions ต่อ repo; ห้ามใช้ prerelease framework เป็น baseline
- **Monorepo tooling**: Single repo, no special tooling — `web/` + `api/` folders side by side (confirmed D3)
