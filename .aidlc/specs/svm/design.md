# Design Document: SVM (Security Vulnerability Management)

## Summary
<!-- 10-line max digest for downstream phases. -->
- **Architecture**: Layered/Clean-ish modular monolith — Angular SPA + ASP.NET Core API (same-origin REST `/api/v1`)
- **Stack**: Angular 22 + Tailwind 4/daisyUI 5 / ASP.NET Core .NET 10 (C# 14) / SQL Server (EF Core 10) / OCI containers
- **Components**: 15 (C1 Auth, C2 Asset, C3 Intake, C4 DataQuality, C5 ReferenceData, C6 RiskScoring, C7 Workflow, C8 Assignment/Remediation, C9 SLA, C10 Verification, C11 Dashboard, C12 Admin, C13 Audit, C14 Notification, C15 Integration-mock) + 8 Angular views
- **Entities**: ~28 — User/Role/Permission/UserScope, Asset/Ownership, Finding/Instance/ImportBatch, CVE/CWE/CVSS, RiskWeight/Threshold/ScoreHistory, StatusTransition/StatusHistory, AssignmentRule/RemediationTask/Evidence/Comment, SlaPolicy/Calendar/Timer, VerificationRecord, Notification, AuditLog
- **Endpoints**: ~45 under `/api/v1` (auth, assets, findings/import, instances risk/transition, assign/task/evidence, sla, verification, dashboards, admin, audit, health)
- **Integrations**: all mock adapters (CMDB, Scanner, Email/Teams, Auth; ITSM/SIEM interface-only) via DI toggle
- **Testing**: PBT No — NFR Yes (Minimal observability)
- **Key Decisions**: configurable weighted risk engine; table-driven workflow state machine; append-only hash-chained audit

## Architecture

### System Context Diagram
```
                 ┌─────────────────────────────────────────────┐
   User (16      │                 SVM System                  │
   roles) ──────▶│  ┌───────────────┐   REST    ┌────────────┐ │
   browser       │  │ Angular 22 SPA│──/api/v1──▶│ ASP.NET    │ │
                 │  │ (SVM web UI)  │  same-origin│ Core API   │ │
                 │  └───────────────┘            │ (.NET 10)  │ │
                 │                                └─────┬──────┘ │
                 │                            EF Core 10 │       │
                 │                                ┌──────▼─────┐ │
                 │                                │ SQL Server │ │
                 │                                └────────────┘ │
                 │   mock adapters (DI toggle UseMock=true)      │
                 │   CMDB │ Scanner │ Email/Teams │ Auth(OIDC)   │
                 │   ITSM/SIEM (interface-only)   + seed data    │
                 └─────────────────────────────────────────────┘
```

### Technology Stack
- **Frontend**: Angular 22 (standalone, strict TS), Tailwind CSS 4 + daisyUI 5 + Angular CDK; current UI follows the [SVM design system](../../../.kiro/steering/design-system.md)
- **Backend**: ASP.NET Core Web API on .NET 10 LTS (C# 14, nullable), REST `/api/v1`, OpenAPI, ProblemDetails, policy-based authz, OpenTelemetry (Minimal)
- **Database**: SQL Server (EF Core 10 SqlServer provider), nvarchar for Thai
- **Infrastructure**: separate web + api OCI images (same-origin); local dev via `ng serve` + `dotnet run` + SQL Server (docker)
- **Key Libraries**: EF Core 10, Swashbuckle, JWT bearer, xUnit/FluentAssertions

### Key Design Decisions
1. **Configurable weighted Risk Scoring Engine** (D3-3): weights ใน `config_riskWeight` อ่าน runtime; invariant score ∈ [0,100]; breakdown JSON เพื่อ explainability — ตอบ FR-16, NFR-31, BR-02
2. **Table-driven Workflow State Machine** (D3-4): `config_statusTransition` (from→to + permission/evidence/approval guard) — ตอบ FR-22/23, BR-27, config-driven NFR-31
3. **Append-only hash-chained Audit** (D3-8): no update/delete path + restricted DB perm + prevHash→rowHash — ตอบ NFR-20/21, FR-68
4. **Interface + mock adapter + seed** (D3-5): external systems สลับ real/mock ผ่าน DI/config — ตอบ workshop policy, BR-35/36/37
5. **Local JWT behind `IAuthProvider`** (D3-6): OIDC-ready โดยไม่แตะ business logic — ตอบ NFR-09, D1-4

## Current UI Direction

The approved light theme, shell, tokens, and seven product routes are defined in the [SVM design system](../../../.kiro/steering/design-system.md). See the [dashboard surface contract](../../../docs/ui/dashboard.md) for dashboard composition and data behavior. Earlier Ferrari and dark SOC decisions remain historical.

## Traceability

| Requirement | Component(s) | API Endpoint(s) | Data Entity | Design File | Status |
|---|---|---|---|---|---|
| US-001 Login+role | C1 Auth | POST /auth/login, GET /auth/me | User,Role,Permission,UserScope | components, api-spec, data-model | ✅ Covered |
| US-002 RBAC | C1 Auth | (policy ทุก endpoint) | Role,Permission | components, nfr | ✅ Covered |
| US-003 Data segregation | C1 Auth, C11 | (scope filter ทุก list) | UserScope | components, data-model | ✅ Covered |
| US-004 Asset inventory | C2 Asset | GET /assets, /assets/{id} | Asset | components, api-spec | ✅ Covered |
| US-005 Asset sync (CMDB) | C2, C15 | POST /assets/sync | Asset,ImportBatch | integration | ✅ Covered |
| US-006 Criticality+ownership | C2 Asset | PUT /assets/{id}/criticality | Asset,AssetOwnership | data-model | ✅ Covered |
| US-007 File import | C3 Intake, C4 | POST /findings/import | Finding,ImportBatch | components, api-spec | ✅ Covered |
| US-008 Manual registration | C3 Intake | POST /findings/manual | Finding | api-spec | ✅ Covered |
| US-009 Import template | C3 Intake | GET /findings/import-template | — | api-spec | ✅ Covered |
| US-010 Validation | C4 DataQuality | (intake pipeline) | Finding | components | ✅ Covered |
| US-011 Normalization | C4 DataQuality | (intake pipeline) | Finding,Instance | components | ✅ Covered |
| US-012 Deduplication | C4 DataQuality | GET /dedup-queue, POST resolve-duplicate | Instance,Finding | components, data-model | ✅ Covered |
| US-013 CVE/CWE/CVSS | C5 ReferenceData | GET /reference/cve,cwe | CVE,CWE,CvssVersion | data-model | ✅ Covered |
| US-014 KEV | C5 ReferenceData | (enrich) | CVE(isKev) | data-model | ✅ Covered |
| US-015 Risk score | C6 RiskScoring | POST /instances/{id}/recalculate | RiskWeight,ScoreHistory,Instance | components, data-model | ✅ Covered |
| US-016 Risk level | C6 RiskScoring | (classification) | RiskThreshold,Instance | data-model | ✅ Covered |
| US-017 Manual adjust+recalc | C6 RiskScoring | PUT /instances/{id}/risk | RiskScoreHistory | api-spec | ✅ Covered |
| US-018 Status mgmt (13) | C7 Workflow | (status field) | Instance,StatusHistory | data-model | ✅ Covered |
| US-019 Transition control | C7 Workflow | POST /instances/{id}/transition | StatusTransition,StatusHistory | components, api-spec | ✅ Covered |
| US-020 Assignment | C8 Assign | POST assign, bulk-assign | AssignmentRule,Instance | api-spec | ✅ Covered |
| US-021 Remediation task | C8 Assign | GET/PUT /tasks/{id} | RemediationTask | data-model | ✅ Covered |
| US-022 Evidence | C8 Assign | POST /instances/{id}/evidence | Evidence | data-model, operations | ✅ Covered |
| US-023 Comment+mention | C8 Assign | POST /instances/{id}/comments | Comment,Notification | api-spec | ✅ Covered |
| US-024 SLA calc | C9 SLA | (on risk level) | SlaPolicy,SlaTimer,Calendar | data-model | ✅ Covered |
| US-025 SLA timer+recalc | C9 SLA | GET /instances/{id}/sla | SlaTimer | api-spec | ✅ Covered |
| US-026 Notification+escalation | C9, C14 | GET /notifications, SlaMonitorJob | Notification | components, integration | ✅ Covered |
| US-027 Submit+rescan | C10 Verification | POST submit-verification, rescan | VerificationRecord,Evidence | api-spec, integration | ✅ Covered |
| US-028 Verification result | C10 Verification | POST /instances/{id}/verification | VerificationRecord | api-spec | ✅ Covered |
| US-029 Closure+reopen | C10, C7 | POST /transition (Closed/Reopened) | Instance,StatusHistory | components | ✅ Covered |
| US-030 Dashboards+search | C11 Dashboard | GET /dashboard/*, GET /instances?filter | Instance (aggregate) | components, api-spec | ✅ Covered |
| US-031 Admin config | C12 Admin | GET/POST/PUT /admin/* | RiskWeight,SlaPolicy,StatusTransition,AssignmentRule,MasterData | api-spec | ✅ Covered |
| US-032 Audit trail | C13 Audit | GET /audit | AuditLog | components, data-model, operations | ✅ Covered |

### Coverage Summary
- **Requirements covered**: 32 / 32 (US-001..032)
- **Gaps**: ไม่มี
- **Components without requirement**: C15 Integration-mock (infra/scaffold — รองรับ US-005/026/027), C14 Notification (รองรับ US-023/026) — justified เป็น cross-cutting/infra

## Open Questions & Risks
| # | Question/Risk | Impact | Status |
|---|---|---|---|
| 1 | SlaMonitorJob กับ multi-instance (double-run) | Medium | Open — MVP single instance; future lock/leader election |
| 2 | ปริมาณ import ใหญ่มาก (async worker จริง vs inline) | Medium | Mitigated — batch tracking + ออกแบบ async-capable; MVP inline |
| 3 | Risk weight/threshold เริ่มต้น (ค่า default) ยังไม่ยืนยันเชิงธุรกิจ | Medium | Open — ใช้ seed default + ปรับผ่าน admin (BR-40) |
| 4 | ค่าเป้าหมาย NFR (response time/SLA hours จริง) | Low | Open — ใช้ baseline workshop; config ได้ |

## Detailed Specifications
- [Components](design/components.md)
- [Data Model](design/data-model.md)
- [API Specification](design/api-spec.md)
- [Integration](design/integration.md)
- [Implementation](design/implementation.md)
- [Operations](design/operations.md)
- [Non-Functional Requirements](design/nfr.md)
- [Testing Strategy](design/testing-strategy.md)

## External References
| Source | Type | Used in |
|---|---|---|
| `.kiro/steering/design-system.md` | Current UI direction | components (F1–F8 UI), nfr (accessibility) |
| dipd-mcp development-guideline | Tech standard | implementation (stack/versions), api-spec (REST/ProblemDetails), nfr (authz) |
| requirements.md / personas.md | Requirements | traceability, all design files |
