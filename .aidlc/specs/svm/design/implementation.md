# Implementation

## Code Organization
- **Architecture pattern**: Layered/Clean-ish (D3-2) — Api → Application → Domain → Infrastructure
- **Repository type**: Single repo, `web/` + `api/` (D3-1)

### Directory structure
```
svm/                               # repo root (git init -b main)
├─ .gitignore
├─ README.md
├─ web/                            # Angular 22 SPA
│  ├─ package.json / package-lock.json   (committed)
│  ├─ angular.json / tsconfig.json (strict)
│  ├─ tailwind.config.* / styles (runtime tokens; current direction in .kiro/steering/design-system.md)
│  └─ src/
│     ├─ main.ts
│     └─ app/
│        ├─ core/                  (auth service, http interceptor, guards, api clients)
│        ├─ shared/                (ui components, design-token css, pipes)
│        ├─ features/
│        │  ├─ dashboard/          (F2)
│        │  ├─ vulnerabilities/    (F3)
│        │  ├─ intake/             (F4)
│        │  ├─ remediation/        (F5)
│        │  ├─ verification/       (F6)
│        │  ├─ admin/              (F7)
│        │  └─ audit/              (F8)
│        └─ mock-data/             (*.json สำหรับ FE+mock stage ก่อนต่อ backend)
├─ api/                            # .NET 10 solution
│  ├─ global.json                  (pin SDK)
│  ├─ Directory.Packages.props     (central package versions) + packages.lock.json
│  ├─ SvmApi.sln
│  ├─ src/
│  │  ├─ SvmApi.Api/               (controllers, Program.cs, middleware, auth policies, health)
│  │  ├─ SvmApi.Application/       (use-case services: Intake, DataQuality, RiskScoring, Workflow, Sla, Verification, Assignment, Dashboard, Admin; DTOs; interfaces)
│  │  ├─ SvmApi.Domain/            (entities, enums, domain rules, invariants)
│  │  └─ SvmApi.Infrastructure/    (EF Core DbContext + configurations + repositories; Integration/Mock adapters + seed; Auth/local; Audit hash-chain)
│  └─ tests/
│     └─ SvmApi.Tests/             (xUnit: risk scoring, dedup, transition guard, sla calc)
└─ docs/
   └─ adr/                         (ADRs: baseline, SQL Server, local-auth)
```

### Module boundaries
- Domain ไม่พึ่ง Infrastructure/Api (dependency inward). Application นิยาม interface (เช่น `IAssetRepository`, `ICmdbAdapter`, `IAuthProvider`), Infrastructure เป็นผู้ implement.
- Controllers บาง → เรียก Application service; ไม่มี business logic ใน controller.

### Naming conventions (IRPC standard)
- C#: classes/methods PascalCase, variables camelCase, nullable enabled
- EF Core: `IEntityTypeConfiguration<T>` + `ToTable("master_asset")`, columns `HasColumnName` lowerCamelCase; ไม่ pluralize อัตโนมัติ
- API JSON: camelCase; routes lowercase plural `/api/v1/...`
- TypeScript: strict; components PascalCase; variables camelCase

## Technology Stack (resolved versions)
| Layer | Tool | Version |
|---|---|---|
| Backend runtime | .NET | 10.0 (LTS, 10.0.8+) |
| Backend lang | C# | 14 (nullable) |
| ORM | EF Core | 10.x (SqlServer provider) |
| Database | SQL Server | 2022+ (local via docker compose สำหรับ dev) |
| API docs | Swashbuckle/OpenAPI | ตาม .NET 10 baseline |
| Frontend | Angular | 22.0.x (standalone, strict) |
| Node runtime | Node.js | 24 LTS |
| Styling | Tailwind CSS / daisyUI / Angular CDK | 4.x / 5.x / 22.x |
| Test (api) | xUnit | current stable |
| Test (web) | Angular CLI/Vitest (+ Playwright optional) | current stable |

- Pin exact versions ต่อ repo; commit `package-lock.json`, `packages.lock.json`, `global.json`. ห้าม prerelease baseline.

## Development Setup
### Prerequisites
- .NET 10 SDK, Node.js 24 LTS, SQL Server (local หรือ `docker compose`), Docker (optional)

### Setup commands (fresh clone)
```bash
# db (local SQL Server via docker — dev)
docker compose up -d sqlserver        # หรือใช้ SQL Server ที่มีอยู่

# api
cd api
dotnet restore --locked-mode
dotnet ef database update -p src/SvmApi.Infrastructure -s src/SvmApi.Api   # migrate
dotnet run --project src/SvmApi.Api   # seed master/reference/users on startup (dev)

# web
cd ../web
npm ci
npm start                             # ng serve → http://localhost:4200 (proxy → api)
```

### Environment variables
| Name | Description | Example (placeholder) |
|---|---|---|
| `ConnectionStrings__Svm` | SQL Server connection | `Server=localhost,1433;Database=svm;User Id=svm_app;Password=__SET_ME__;Encrypt=True;TrustServerCertificate=True` |
| `Auth__Jwt__Issuer` | JWT issuer (local) | `svm-local` |
| `Auth__Jwt__SigningKey` | JWT signing key (secret) | `__SET_ME__` (จาก secret store; ห้าม commit) |
| `Auth__Mode` | `Local` หรือ `Oidc` | `Local` |
| `Integration__UseMock` | ใช้ mock adapter | `true` |
| `ASPNETCORE_ENVIRONMENT` | runtime env | `Development` |
| `Logging__LogLevel__Default` | log level | `Information` |

- Secrets ไม่ commit; ใช้ user-secrets (dev) หรือ secret store (NFR-16). ห้ามใส่ password จริงใน appsettings.

## Testing (ตาม D3-9/10 — Minimal + verify-by-running)
- **Unit (xUnit)**: logic สำคัญ — RiskScoringEngine (invariant 0–100, weight sum), DataQuality dedup (idempotent), Workflow transition guard, Sla calculation (business calendar). `dotnet test`
- **Property-based**: ไม่ทำ (example-based + invariant assertion)
- **Integration/E2E**: optional (phase ถัดไป); core slice ยืนยันด้วยการรัน dev server + คลิก flow จริง (Working-Software DoD)
- **Coverage target**: เชิงคุณภาพ — core logic paths มี test; ไม่ตั้ง % บังคับใน workshop
