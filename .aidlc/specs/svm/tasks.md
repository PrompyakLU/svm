# Tasks: SVM (Security Vulnerability Management)

## Summary
- **Total Tasks**: 35 across 10 phases
- **Execution Waves**: 7 waves (sequential — solo team, stop-gate per wave per hands-on-project policy)
- **Coverage**: 15 components (C1–C15), ~28 entities, ~45 endpoints `/api/v1`, 8 Angular views (F1–F8)
- **Testing**: 4 unit-test tasks (core logic) + verify-by-running every wave; no integration/E2E/load/PBT tasks (D3/D4 Minimal)
- **Strategy**: Vertical feature slice (D4-1), Test-after core logic (D4-2), Single-concern granularity (D4-6)
- **Derived from**: design.md + design/{components,data-model,api-spec,integration,implementation,operations,nfr,testing-strategy}.md

## Overview
- **Rationale**: ลำดับ wave ยึดตาม hands-on-project Task Execution Order (บังคับ): Setup → Core (FE+mock JSON) → Core (FE+Backend/DB จริง) → steps อื่น → test/verify → deploy(optional). ภายใน wave ทำ task แบบ sequential (solo). หยุดรอ user ยืนยันจบแต่ละ wave.
- **Checkbox legend**: `- [ ]` ยังไม่ทำ · `- [x]` เสร็จ (ติ๊กเมื่อ verify ผ่าน)
- **DoD ขั้นต่ำ**: build/compile ผ่าน + dev server รันได้ + flow ตาม scope คลิกใช้งานได้จริง + ไม่มี error ค้าง (Working-Software DoD)

---

## Task Phases

- [x] 1. Setup & โครงสร้างพื้นฐาน (Wave 1)
  - [x] 1.1 Git init + repo skeleton
    - **Deps**: — | **Ref**: implementation.md — Directory structure
    - `git init -b main` ที่ root, สร้าง `.gitignore` (node_modules, bin/obj, *.user, .env, appsettings.*.local.json, dist, coverage)
    - สร้างโครง `web/`, `api/`, `docs/adr/`, `README.md` (essential info)
  - [x] 1.2 Angular workspace (web/) + initial styling tokens (historical direction; superseded)
    - **Deps**: 1.1 | **Ref**: components.md — F1; implementation.md — web/; [current design system](../../../.kiro/steering/design-system.md)
    - Angular 22 (standalone, strict), Tailwind 4 + daisyUI 5 + Angular CDK, Node 24, commit `package-lock.json`
    - Initial token scaffolding followed the earlier Ferrari reference. The approved visual redesign follows the current SVM design system; runtime tokens remain in `web/src/styles.css`.
    - CoreShell layout + routing + placeholder nav; verify `ng serve` ขึ้นที่ http://localhost:4200
  - [x] 1.3 .NET solution (api/) 4 โปรเจกต์ + pin versions
    - **Deps**: 1.1 | **Ref**: implementation.md — api/; tech.md
    - `SvmApi.sln`: `SvmApi.Api`/`SvmApi.Application`/`SvmApi.Domain`/`SvmApi.Infrastructure` (.NET 10, C# 14 nullable)
    - `global.json` (pin SDK), `Directory.Packages.props` + `packages.lock.json`; EF Core 10 SqlServer, JWT bearer, Swashbuckle, xUnit
    - Program.cs: OpenAPI, ProblemDetails middleware, UseForwardedHeaders ก่อน UseAuthentication; verify `dotnet run` ขึ้น + Swagger
  - [x] 1.4 Cross-cutting foundation (config, logging, health, DI toggle)
    - **Deps**: 1.3 | **Ref**: operations.md; nfr.md; components.md — C15
    - Config validation ตอน startup (env schema), structured JSON logging + request-id middleware (Minimal observability, D3), `GET /api/v1/health` (liveness+readiness)
    - DI/config switch `Integration:UseMock=true`; ห้าม log secret/PII (NFR-40, data-privacy)
    - Secrets จาก env/user-secrets (ห้าม commit; `Auth__Jwt__SigningKey`, `ConnectionStrings__Svm`)

- [x] 2. Core FE + Mock JSON (Wave 2)
  - [x] 2.1 Angular feature shells + API client seam + mock data mode
    - **Deps**: 1.2 | **Ref**: components.md — F1..F8; data-model.md
    - สร้าง feature views F2–F8 (dashboard, vulnerabilities list+detail, intake, remediation, verification, admin, audit) เป็น route + หน้าโครงจริง
    - `*.json` mock-data ใน `web/src/app/mock-data/` + API client service ที่สลับ mock/real ได้ (environment flag)
    - states ครบทุกหน้า: empty/loading(skeleton)/error/success; responsive mobile/tablet/desktop; WCAG 2.2 AA (focus-visible, tap ≥44px, สี+ไอคอน+label)
  - [x] 2.2 FE flows wired to mock (worklist→detail→action, intake, dashboard)
    - **Deps**: 2.1 | **Ref**: components.md — F2/F3/F4; nfr.md (accessibility)
    - vulnerability worklist + advanced filter + offset pagination (mock), finding detail (risk breakdown/history/evidence/SLA/workflow actions), intake upload+manual form, dashboard KPI/charts
    - verify ใน browser: ทุก flow หลักคลิกใช้งานได้จริงบน mock, ไม่มี console error, polish ผ่าน Visual Quality Bar

  - [x] 2.3 Authentication UI flow + mock session (follow-up to Wave 2; does not complete backend AuthModule)
    - **Deps**: 2.1, 2.2 | **Ref**: US-001 UI acceptance criteria; F1 authentication UI slice; [authentication surface contract](../../../docs/ui/authentication.md)
    - Login page in approved white/teal design, shared Thai/Latin typography; labelled public demo account, accessible validation/error/loading/password visibility, one submission at a time
    - AuthProvider seam + AuthService, protected routes, internal-only return URL, user/role display and logout on desktop/mobile, per-tab restoration and 30-minute inactivity expiry
    - Verify invalid credentials, direct protected URL, successful return, reload, logout/back, expiry/background resume and fail-closed non-mock mode; update evidence before checking complete
    - Task 3.3 (JWT/RBAC/data scopes/audit) and Task 6.3 (real API wiring) stay pending
    - Verified: production build, combined frontend tests and desktop/mobile browser flows; see [authentication verification evidence](../../../docs/ui/authentication.md#verification-evidence). Client-side demo behavior does not close US-001/002/003 server-side requirements.

- [x] 3. Data layer + Access/Audit foundation (Wave 3)
  - [x] 3.1 EF Core DbContext + entity configurations + initial migration
    - **Deps**: 1.3, 1.4 | **Ref**: data-model.md (ทุก entity); implementation.md — naming
    - map ~28 entities ด้วย `IEntityTypeConfiguration<T>` + `ToTable("master_/config_/trans_...")`, columns `HasColumnName` lowerCamelCase, nvarchar สำหรับ Thai, rowversion บน instance
    - `dotnet ef migrations add Init` + `database update` (local SQL Server docker); verify schema สร้างสำเร็จ
  - [x] 3.2 Seed data (reference + master + config + users)
    - **Deps**: 3.1 | **Ref**: data-model.md — reference/config; integration.md (seed)
    - seed CVE/CWE/CVSS/KEV, riskWeight+threshold default, statusTransition (13 states), slaPolicy+calendar, assignmentRule, roles/permissions/users (local), asset ตัวอย่าง — ไม่มี PII จริง
  - [x] 3.3 AuthModule (local JWT + policy authz + data scope)
    - **Deps**: 3.1, 3.2 | **Ref**: components.md — C1; data-model.md — config_user/role/permission/userScope; nfr.md (authz)
    - `IAuthProvider`+`LocalAuthProvider`, `JwtTokenGenerator`, `ICurrentUser`, `RequirePermission(code)` policies, data-scope claim + central query filter
    - endpoints: `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`; 401/403 → ProblemDetails; US-001/002/003
  - [x] 3.4 AuditModule (append-only hash-chained)
    - **Deps**: 3.1 | **Ref**: components.md — C13; data-model.md — trans_auditLog; operations.md
    - `IAuditService.Record(...)` INSERT-only, SHA-256 prevHash→rowHash chain, no update/delete path, restricted perm
    - `GET /api/v1/audit` (auditor, scoped); EX-98/100; US-032

- [x] 4. Core backend flow: Asset → Intake → Risk (Wave 4)
  - [x] 4.1 AssetModule + CMDB mock adapter
    - **Deps**: 3.3, 3.4 | **Ref**: components.md — C2/C15; data-model.md — master_asset/assetOwnership; integration.md — ICmdbAdapter
    - CRUD asset (scoped), ownership, criticality+history, `POST /assets/sync` ผ่าน `ICmdbAdapter` mock+seed; US-004/005/006; BR-05/38
  - [x] 4.2 ReferenceDataService
    - **Deps**: 3.2 | **Ref**: components.md — C5; data-model.md — master_cve/cwe, config_cvssVersion
    - `IReferenceDataService.Enrich/IsKev`, `GET /reference/cve|cwe`; EX-47/48/49; US-013/014
  - [x] 4.3 Intake + DataQuality (import/manual → validate → normalize → dedup)
    - **Deps**: 4.1, 4.2 | **Ref**: components.md — C3/C4; data-model.md — trans_vulnerabilityFinding/Instance, config_importBatch; integration.md — IScannerAdapter(mock)
    - `POST /findings/import` (CSV/JSON multipart, idempotent transactionId), `GET /findings/import-template`, `POST /findings/manual`, `GET /import-batches`
    - `IDataQualityService.Process`: validate mandatory/type/ref, normalize→common model, dedup key(asset+cve+location+port+component), `GET /dedup-queue` + `POST /findings/{id}/resolve-duplicate`
    - US-007/008/009/010/011/012; EX-11..18/45/46
  - [x] 4.4 RiskScoringEngine (weighted-sum configurable)
    - **Deps**: 4.3 | **Ref**: components.md — C6; data-model.md — config_riskWeight/threshold, trans_riskScoreHistory; design.md KD#1
    - normalize factors → weight จาก `config_riskWeight` (runtime) → score, classify ด้วย threshold, breakdown JSON, invariant 0–100 (clamp+validate)
    - `POST /instances/{id}/recalculate`, `PUT /instances/{id}/risk` (manual+reason+history); US-015/016/017; EX-50/51/54

- [x] 5. Core backend flow: Workflow → Assign/Remediation → SLA → Verification (Wave 4 cont.)
  - [x] 5.1 WorkflowModule (table-driven state machine)
    - **Deps**: 4.4 | **Ref**: components.md — C7; data-model.md — config_statusTransition, trans_statusHistory; design.md KD#2
    - `IWorkflowService.CanTransition`, `POST /instances/{id}/transition` (guard: permission/evidence/approval), `GET /instances/{id}/history`, optimistic concurrency rowVersion
    - US-018/019; EX-55/56/57
  - [x] 5.2 AssignmentRemediationModule + evidence + comment + Notification
    - **Deps**: 5.1 | **Ref**: components.md — C8/C14; data-model.md — config_assignmentRule, trans_remediationTask/evidence/comment, trans_notification; integration.md — IFileStorage/IMalwareScanner/IEmail/ITeams(mock)
    - assign/bulk-assign (auto rule), remediation task/plan/progress, evidence (versioned, malware-scan mock ก่อนรับ), comment+mention
    - `INotificationService.Notify` in-app จริง + email/teams mock(log), `GET /notifications`; US-020..023/026; EX-59..62/21/22
  - [x] 5.3 SlaModule + SlaMonitorJob (escalation)
    - **Deps**: 5.1 | **Ref**: components.md — C9; data-model.md — config_slaPolicy/calendar/slaTimer
    - `ISlaService.Calculate` (due dates + business calendar), timer+breach, recalc เก็บค่าเดิม+เหตุผล, background `SlaMonitorJob` สแกน near-due/overdue → escalate
    - `GET /instances/{id}/sla`; US-024/025/026; EX-65..70
  - [x] 5.4 VerificationModule (submit → rescan mock → result → closure/reopen)
    - **Deps**: 5.1, 5.2 | **Ref**: components.md — C10; data-model.md — trans_verificationRecord; integration.md — IScannerAdapter(mock)
    - `POST /instances/{id}/submit-verification`, `POST .../rescan` (mock), `POST .../verification`, closure/reopen ผ่าน Workflow; independent verifier สำหรับ Critical (BR-16), reopen เมื่อพบซ้ำ (BR-13)
    - US-027/028/029; EX-74..80

- [x] 6. Dashboard, Admin, Audit view + FE↔Backend integration (Wave 5)
  - [x] 6.1 DashboardReportingModule (operations + executive + advanced search)
    - **Deps**: 5.3 | **Ref**: components.md — C11; data-model.md — trans_vulnerabilityInstance (aggregate)
    - `GET /dashboard/operations`, `GET /dashboard/executive`, `GET /instances?filters` (offset pagination + data-scope filter กลาง); US-030; EX-93/94
  - [x] 6.2 AdministrationModule (master data + config)
    - **Deps**: 4.4, 5.1, 5.3 | **Ref**: components.md — C12; data-model.md — config_* 
    - `GET/POST/PUT /admin/{master-data|status-transitions|sla-policies|risk-weights|assignment-rules}` + validation + audit (BR-40); US-031; EX-54
  - [x] 6.3 Wire Angular (F2–F8) to real API + auth interceptor
    - **Deps**: 6.1, 6.2, 3.3 | **Ref**: components.md — F1..F8; nfr.md
    - สลับ API client จาก mock → real (`/api/v1`), JWT interceptor + role-based route guard, X-Forwarded-Proto aware
    - verify end-to-end ใน browser: login → intake → risk → workflow → assign → SLA → verify → close; audit + dashboard แสดงจริง; ไม่มี error ค้าง

- [x] 7. Core-logic unit tests + verify (Wave 6 — test/verify)
  - [x] 7.1 Unit tests: RiskScoringEngine
    - **Deps**: 4.4 | **Ref**: testing-strategy.md; implementation.md — Testing
    - xUnit: invariant score ∈[0,100], weight-sum ถูกต้อง, classify ตาม threshold, invalid weight reject (EX-54)
  - [x] 7.2 Unit tests: Workflow transition guard
    - **Deps**: 5.1 | **Ref**: testing-strategy.md
    - allowed/blocked transition, missing-evidence block (EX-56), concurrency conflict (EX-57)
  - [x] 7.3 Unit tests: DataQuality dedup
    - **Deps**: 4.3 | **Ref**: testing-strategy.md
    - dedup key correctness, idempotent re-import, ambiguous → NeedsReview queue (EX-45)
  - [x] 7.4 Unit tests: SLA calculation
    - **Deps**: 5.3 | **Ref**: testing-strategy.md
    - due-date จาก policy + business calendar, breach detection, recalc เก็บค่าเดิม
  - [x] 7.5 Full verify-by-running (DoD gate)
    - **Deps**: 6.3, 7.1, 7.2, 7.3, 7.4 | **Ref**: hands-on-project — Working-Software DoD
    - `dotnet test` ผ่าน, `dotnet run` + `ng serve` ขึ้น, คลิกครบทุก flow scope, ไม่มี error console/terminal

- [ ] 8. Deploy (Wave 7 — optional, ท้ายสุด)
  - [~] 8.1 Containerize + run (optional) — เตรียมไฟล์แล้ว, ยังไม่ verify build (ไม่มี Docker ใน env นี้)
    - **Deps**: 7.5 | **Ref**: tech.md — Infrastructure; operations.md
    - Dockerfile web + api (linux/amd64), docker compose (web+api+sqlserver) สำหรับ local; deploy ตาม platform standard ภายหลัง (ไม่บังคับใน workshop Day 1)
    - **เตรียมไว้แล้ว**: `api/Dockerfile`, `web/Dockerfile`, `web/nginx.conf`, `docker-compose.yml`, `.env.example`, `.dockerignore` (web+api), `docs/deploy/README.md` (local run + AWS checklist)
    - **ยังค้าง**: `docker compose build` + run เพื่อ verify จริง (env นี้ไม่มี Docker); AWS ECS Fargate deploy เป็นงาน Day 2/3 (ดู docs/deploy/README.md)

---

## Task Summary
| ID | Title | Deps | Status |
|----|-------|------|--------|
| 1.1 | Git init + repo skeleton | — | ☐ |
| 1.2 | Angular workspace + initial styling tokens | 1.1 | ☐ |
| 1.3 | .NET solution 4 projects | 1.1 | ☐ |
| 1.4 | Cross-cutting foundation | 1.3 | ☐ |
| 2.1 | FE feature shells + mock mode | 1.2 | ☐ |
| 2.2 | FE flows wired to mock | 2.1 | ☐ |
| 2.3 | Authentication UI + mock idle session | 2.1,2.2 | ☑ |
| 3.1 | EF Core DbContext + migration | 1.3,1.4 | ☐ |
| 3.2 | Seed data | 3.1 | ☐ |
| 3.3 | AuthModule | 3.1,3.2 | ☐ |
| 3.4 | AuditModule hash-chain | 3.1 | ☐ |
| 4.1 | AssetModule + CMDB mock | 3.3,3.4 | ☐ |
| 4.2 | ReferenceDataService | 3.2 | ☐ |
| 4.3 | Intake + DataQuality | 4.1,4.2 | ☐ |
| 4.4 | RiskScoringEngine | 4.3 | ☐ |
| 5.1 | WorkflowModule | 4.4 | ☐ |
| 5.2 | Assignment/Remediation + Notification | 5.1 | ☐ |
| 5.3 | SlaModule + monitor job | 5.1 | ☐ |
| 5.4 | VerificationModule | 5.1,5.2 | ☐ |
| 6.1 | Dashboard/Reporting | 5.3 | ☐ |
| 6.2 | AdministrationModule | 4.4,5.1,5.3 | ☐ |
| 6.3 | Wire Angular → real API | 6.1,6.2,3.3 | ☐ |
| 7.1 | Unit: RiskScoringEngine | 4.4 | ☐ |
| 7.2 | Unit: Workflow guard | 5.1 | ☐ |
| 7.3 | Unit: DataQuality dedup | 4.3 | ☐ |
| 7.4 | Unit: SLA calc | 5.3 | ☐ |
| 7.5 | Full verify-by-running | 6.3,7.1-7.4 | ☐ |
| 8.1 | Containerize (optional) | 7.5 | ☐ |

## Requirements Coverage
| US | Tasks | Status |
|----|-------|--------|
| US-001 Login+role | 3.3, 6.3 | ☐ |
| US-002 RBAC | 3.3 | ☐ |
| US-003 Data segregation | 3.3, 6.1 | ☐ |
| US-004 Asset inventory | 4.1 | ☐ |
| US-005 Asset sync (CMDB) | 4.1 | ☐ |
| US-006 Criticality+ownership | 4.1 | ☐ |
| US-007 File import | 4.3 | ☐ |
| US-008 Manual registration | 4.3 | ☐ |
| US-009 Import template | 4.3 | ☐ |
| US-010 Validation | 4.3 | ☐ |
| US-011 Normalization | 4.3 | ☐ |
| US-012 Deduplication | 4.3, 7.3 | ☐ |
| US-013 CVE/CWE/CVSS | 4.2 | ☐ |
| US-014 KEV | 4.2 | ☐ |
| US-015 Risk score | 4.4, 7.1 | ☐ |
| US-016 Risk level | 4.4 | ☐ |
| US-017 Manual adjust+recalc | 4.4 | ☐ |
| US-018 Status mgmt | 5.1 | ☐ |
| US-019 Transition control | 5.1, 7.2 | ☐ |
| US-020 Assignment | 5.2 | ☐ |
| US-021 Remediation task | 5.2 | ☐ |
| US-022 Evidence | 5.2 | ☐ |
| US-023 Comment+mention | 5.2 | ☐ |
| US-024 SLA calc | 5.3, 7.4 | ☐ |
| US-025 SLA timer+recalc | 5.3 | ☐ |
| US-026 Notification+escalation | 5.2, 5.3 | ☐ |
| US-027 Submit+rescan | 5.4 | ☐ |
| US-028 Verification result | 5.4 | ☐ |
| US-029 Closure+reopen | 5.4 | ☐ |
| US-030 Dashboards+search | 6.1, 6.3 | ☐ |
| US-031 Admin config | 6.2 | ☐ |
| US-032 Audit trail | 3.4 | ☐ |

**Coverage**: 32/32 user stories mapped (0 gaps).

## Design Coverage
| Component | Tasks |
|-----------|-------|
| C1 Auth | 3.3 |
| C2 Asset | 4.1 |
| C3 Intake | 4.3 |
| C4 DataQuality | 4.3 |
| C5 ReferenceData | 4.2 |
| C6 RiskScoring | 4.4 |
| C7 Workflow | 5.1 |
| C8 Assignment/Remediation | 5.2 |
| C9 SLA | 5.3 |
| C10 Verification | 5.4 |
| C11 Dashboard | 6.1 |
| C12 Admin | 6.2 |
| C13 Audit | 3.4 |
| C14 Notification | 5.2 |
| C15 Integration-mock | 1.4, 4.1, 4.3, 5.2, 5.4 |
| F1 CoreShell+AuthGuard | 1.2, 6.3 |
| F2–F8 views | 2.1, 2.2, 6.3 |
| Entities (~28) | 3.1, 3.2 |

## Testing Coverage
- **Unit test tasks**: 7.1 (RiskScoring), 7.2 (Workflow guard), 7.3 (dedup), 7.4 (SLA) — core logic ตาม D3/D4
- **Integration test tasks**: ไม่มี (เกิน scope — D3/D4 Minimal)
- **E2E test tasks**: ไม่มี (ไม่ได้เลือก E2E framework ใน D3)
- **Load test tasks**: ไม่มี
- **PBT tasks**: ไม่มี (D3-10 = No)
- **Verify-by-running**: 2.2, 6.3, 7.5 + ทุก wave ก่อน stop-gate (Working-Software DoD)
- **Coverage summary**: core-logic components (RiskScoring, Workflow, DataQuality, SLA) มี unit test; component อื่น + endpoint ยืนยันด้วย verify-by-running ตามนโยบาย workshop

## Definition of Done
- [ ] Code compile/build ผ่าน (web + api)
- [ ] Dev server รันได้จริง (`dotnet run` + `ng serve`)
- [ ] Flow ตาม scope ของ task คลิกใช้งานได้จริงบน browser
- [ ] Core-logic unit tests ผ่าน (`dotnet test`) สำหรับ task ที่มี test
- [ ] ไม่มี error ค้างใน console/terminal
- [ ] Acceptance criteria ของ US ที่เกี่ยวข้องผ่าน
- [ ] ติ๊ก `[x]` ใน tasks.md + verify จบ wave ก่อนไปต่อ

## Execution Waves
| Wave | Phases | Resolved Deps | Stop-gate |
|------|--------|---------------|-----------|
| 1 | Phase 1 (Setup) | — | ✋ ยืนยันก่อนไป Wave 2 |
| 2 | Phase 2 (Core FE + mock) | Wave 1 | ✋ |
| 3 | Phase 3 (Data + Access/Audit) | Wave 1 | ✋ |
| 4 | Phase 4 + 5 (Core backend flow) | Wave 3 | ✋ |
| 5 | Phase 6 (Dashboard/Admin + FE↔BE wire) | Wave 2, 4 | ✋ |
| 6 | Phase 7 (unit tests + verify) | Wave 4, 5 | ✋ |
| 7 | Phase 8 (deploy) | Wave 6 | optional |

**File ownership**: solo + sequential execution → ไม่มี parallel branch, ไม่มี file-ownership overlap. `web/` ถูกแตะใน Wave 1/2/5; `api/` ใน Wave 1/3/4/5/6; migration/seed (`api/src/SvmApi.Infrastructure`) ใน Wave 3 ก่อน backend flow Wave 4.

## Notes
- **Technical debt / future**: SlaMonitorJob multi-instance lock (MVP single instance), async import worker (MVP inline), integration test + E2E (phase ถัดไป), real adapter swap (CMDB/Scanner/Email/Teams/SSO), report/export, archiving/retention, DR/backup.
- **Open items (จาก design)**: risk weight/threshold business default (ใช้ seed + admin ปรับ), ค่าเป้าหมาย NFR จริง (config ได้).
- **Policy**: ห้าม call SOAP/external API จริง (SOAP_DRY_RUN); mock + log เท่านั้น เว้น user approve. หยุดรอ user ยืนยันจบแต่ละ wave.
