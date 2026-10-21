# Testing Strategy

> ตาม D3-9 (Minimal + verify-by-running) และ D3-10 (ไม่ทำ property-based) + นโยบาย hands-on (test ไว้ท้าย/optional, Working-Software เป็นเกณฑ์ขั้นต่ำ)

## Overview
- **Testing philosophy**: Hybrid — unit test logic สำคัญ (test-after) + verify-by-running flow จริง
- **Coverage target**: เชิงคุณภาพ — core logic paths มี unit test; ไม่ตั้ง % บังคับใน workshop
- **Test pyramid**: unit (เน้น) > integration (optional) > e2e (optional/later)

## Frameworks
- **Unit (api)**: xUnit + FluentAssertions (assertion), NSubstitute/Moq (mock interface)
- **Integration (api, optional)**: `WebApplicationFactory` + SQL Server test DB หรือ SQLite in-memory
- **Unit/Component (web, optional)**: Angular CLI/Vitest
- **E2E (optional/later)**: Playwright (critical flows)
- **PBT**: ไม่ใช้ (example-based + invariant assertion แทน)
- **Load/API tool**: ไม่ใช้ใน core slice

## Test Architecture
```
api/tests/SvmApi.Tests/
├─ Unit/
│  ├─ RiskScoring/        (RiskScoringEngineTests — invariant 0–100, weight sum, breakdown)
│  ├─ DataQuality/        (DeduplicationTests — idempotent, dedup key; ValidationTests)
│  ├─ Workflow/           (TransitionGuardTests — allowed/blocked, evidence/approval guard)
│  └─ Sla/                (SlaCalculationTests — due dates, business calendar, recalc)
├─ Integration/           (optional — API endpoint happy-path)
└─ _shared/               (builders/fixtures: InstanceBuilder, SeedData)
```
- **Naming**: `MethodName_Scenario_ExpectedResult`
- **Shared utilities**: entity builders, deterministic seed, fake `ICurrentUser`/adapters
- **Test data**: ใช้ seed deterministic; isolation ต่อ test (fresh context / rollback)

## Mock Strategy
- **External services**: inject mock adapter (`ICmdbAdapter`, `IScannerAdapter`, `IEmailAdapter`, ...) — มี seed deterministic อยู่แล้ว (D3-5)
- **Database**: unit = in-memory repository/fake; integration = SQLite in-memory หรือ SQL Server test DB
- **Time-dependent**: inject `IClock`/`TimeProvider` (SLA calc, expiry) → mock เวลาได้
- **File system**: `IFileStorage` mock (evidence)

## Environment
- **Test DB**: SQLite in-memory (เร็ว) หรือ SQL Server test (ใกล้ prod); migration ก่อน integration test
- **Env vars**: test-specific (`Integration__UseMock=true`, in-memory connection)
- **CI integration**: `dotnet test` (future CI); workshop รัน local

## Coverage Mapping (ขั้นต่ำ core slice)
| Component | Test type |
|---|---|
| RiskScoringEngine (C6) | unit (บังคับ — invariant/weight) |
| DataQualityService dedup (C4) | unit (บังคับ — idempotent) |
| WorkflowModule guard (C7) | unit (บังคับ — transition rules) |
| SlaModule calc (C9) | unit (บังคับ — due/recalc/calendar) |
| AuthModule (C1) | unit (policy/scope resolve) |
| API endpoints หลัก | integration (optional) |

| Endpoint (critical) | Test |
|---|---|
| `POST /instances/{id}/transition` | integration/unit (guard) |
| `POST /findings/import` | integration (partial/duplicate) |
| `POST /instances/{id}/verification` | unit (independent check) |

### Critical paths (verify-by-running — DoD)
- import → dedup → risk score → assign → SLA → verify → close (คลิกจริงบน dev server)
- login + RBAC + data scope (เห็นเฉพาะ scope)
- dashboard แสดงผลจริง + search/filter

## Run Commands
- Unit: `dotnet test` (api)
- Web (optional): `npm test`
- Coverage (optional): `dotnet test --collect:"XPlat Code Coverage"`
