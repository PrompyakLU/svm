# Components

## Overview
สถาปัตยกรรม Layered/Clean-ish (D3-2): **Angular SPA (web)** → **REST `/api/v1`** → **Application services (use-cases)** → **Domain (entities/rules)** → **Infrastructure (EF Core repositories + mock adapters)** → **SQL Server**. แยกตามโดเมน (bounded responsibility) — แต่ละ domain module มี controller + application service + domain logic + repository ของตน และใช้ cross-cutting (Auth, Audit, Notification) ร่วมกัน

---

## Backend Components (.NET)

### C1. AuthModule
- **Purpose**: local JWT auth + policy-based authorization + role/scope claims (D3-6)
- **Technology**: ASP.NET Core auth, JWT bearer, `IAuthProvider` interface (OIDC-ready)
- **Responsibilities**: login/issue JWT, validate token, resolve role+permission+data scope, enforce policies
- **Exposes**: `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`; `IAuthProvider`, authorization policies (`RequirePermission(code)`), `ICurrentUser` (userId, roles, scopes)
- **Consumes**: AccessRepository (user/role/permission/scope), AuditModule
- **Internal structure**:
  ```
  Api/Controllers/AuthController.cs
  Application/Auth/{LoginHandler, TokenService}
  Domain/Auth/{User, Role, Permission, UserScope}
  Infrastructure/Auth/{LocalAuthProvider, JwtTokenGenerator, AccessRepository}
  ```
- **Key decisions**: 1) JWT ซ่อนหลัง `IAuthProvider` → สลับ OIDC ภายหลังไม่แตะ business logic 2) data scope เป็น claim → ใช้ใน query filter กลาง
- **Error handling**: 401 (invalid/expired), 403 (insufficient permission/scope) → ProblemDetails; log + audit (EX-01/04/05/06/07)

### C2. AssetModule
- **Purpose**: asset inventory, ownership, criticality, CMDB sync (mock) (FR-01..04)
- **Responsibilities**: CRUD asset (ตามสิทธิ์), ownership mapping, criticality + reason history, trigger CMDB mock sync
- **Exposes**: `GET/POST/PUT /api/v1/assets`, `GET /api/v1/assets/{id}`, `PUT /api/v1/assets/{id}/criticality`, `POST /api/v1/assets/sync`
- **Consumes**: AssetRepository, ICmdbAdapter (mock), AuditModule, AuthModule
- **Key decisions**: ownership แยกหลาย type ใน `master_assetOwnership`; sync บันทึกผลและไม่ลบ asset ที่หาย (BR-38)
- **Error handling**: EX-25/28/30/31 → mark + integration error, ไม่ throw ให้ล่ม

### C3. IntakeModule
- **Purpose**: นำเข้า finding (file import + manual) (FR-05/07/08)
- **Responsibilities**: parse/validate CSV/JSON ตาม template, manual registration, สร้าง import batch, ส่งต่อ DataQuality
- **Exposes**: `POST /api/v1/findings/import` (multipart), `GET /api/v1/findings/import-template`, `POST /api/v1/findings/manual`, `GET /api/v1/import-batches`
- **Consumes**: FindingRepository, ImportBatchRepository, DataQualityService, AuditModule
- **Key decisions**: import ทำแบบ async-capable (รองรับไฟล์ใหญ่ NFR-07) แต่ workshop ประมวลผล inline + batch tracking; idempotent ด้วย `transactionId` (BR-36)
- **Error handling**: EX-11..18 (format/size/empty/structure/mandatory/type/duplicate/partial) → รายงานผลรายรายการ

### C4. DataQualityService
- **Purpose**: validation + normalization + deduplication (FR-09..12)
- **Responsibilities**: ตรวจ mandatory/type/reference, map → common model, dedup กับ instance เดิม, consolidate
- **Exposes**: `IDataQualityService.Process(finding)` → (valid?, instanceId, isDuplicate); `GET /api/v1/dedup-queue`, `POST /api/v1/findings/{id}/resolve-duplicate`
- **Consumes**: FindingRepository, InstanceRepository, ReferenceDataService
- **Key decisions**: dedup key = asset+cve+location+port+component (filtered unique); ambiguous → คิวให้คนตัดสิน (EX-45)
- **Error handling**: invalid → mark `validationStatus`, ไม่สร้าง instance; EX-16/45/46

### C5. ReferenceDataService
- **Purpose**: CVE/CWE/CVSS/KEV lookup (seed) (FR-13/14/18)
- **Exposes**: `GET /api/v1/reference/cve/{id}`, `.../cwe/{id}`, `IReferenceDataService.Enrich(finding)`, `IsKev(cveId)`
- **Consumes**: CveRepository, CweRepository, CvssVersionRepository
- **Error handling**: EX-47 (CVE not found → mark), EX-48/49 (unsupported CVSS/invalid vector → mark)

### C6. RiskScoringEngine
- **Purpose**: คำนวณ risk score 0–100 (weighted configurable) + classification (FR-16/17, D3-3)
- **Responsibilities**: normalize แต่ละปัจจัย → ถ่วงน้ำหนักจาก `config_riskWeight` → score; classify ด้วย `config_riskThreshold`; recalc; manual adjust history
- **Exposes**: `IRiskScoringEngine.Calculate(instance)` → (score, level, breakdown); `PUT /api/v1/instances/{id}/risk` (manual), `POST /api/v1/instances/{id}/recalculate`
- **Consumes**: RiskWeightRepository, RiskThresholdRepository, ReferenceDataService, AssetModule, RiskScoreHistoryRepository, AuditModule
- **Key decisions**: 1) weight อ่าน runtime (config-driven NFR-31) 2) invariant: score ∈ [0,100] เสมอ (clamp + validate, EX-51) 3) breakdown เก็บ JSON เพื่อ explainability
- **Error handling**: EX-50 (data ไม่ครบ → mark error, ไม่ให้ score ผิดถูกใช้), EX-54 (invalid weight → reject config)

### C7. WorkflowModule
- **Purpose**: state machine 13 สถานะ + transition guard (FR-22/23, D3-4)
- **Responsibilities**: ตรวจ `config_statusTransition` (from→to, permission, evidence, approval) ก่อนเปลี่ยน; บันทึก `trans_statusHistory`; closure/reopen
- **Exposes**: `POST /api/v1/instances/{id}/transition`, `GET /api/v1/instances/{id}/history`, `IWorkflowService.CanTransition(...)`
- **Consumes**: InstanceRepository, StatusTransitionRepository, EvidenceModule, AuthModule, AuditModule
- **Key decisions**: table-driven → เพิ่ม/แก้ transition ผ่าน config; optimistic concurrency ด้วย `rowVersion` (EX-57)
- **Error handling**: EX-55 (invalid transition), EX-56 (missing evidence), EX-57 (concurrent) → 409/422 ProblemDetails

### C8. AssignmentRemediationModule
- **Purpose**: assignment (auto/manual/bulk) + remediation task/plan/progress + evidence + comment (FR-24..31)
- **Exposes**: `POST /api/v1/instances/{id}/assign`, `POST /api/v1/instances/bulk-assign`, `GET/PUT /api/v1/tasks/{id}`, `POST /api/v1/instances/{id}/evidence`, `POST /api/v1/instances/{id}/comments`
- **Consumes**: InstanceRepository, RemediationTaskRepository, EvidenceRepository, AssignmentRuleRepository, NotificationModule, IFileStorage + IMalwareScanner (mock), AuditModule
- **Key decisions**: auto assign ด้วย `config_assignmentRule`; evidence versioned, malware scan ก่อนรับ (NFR-18)
- **Error handling**: EX-59/60/61/62 (assignment), EX-21/22 (evidence malware/encrypted)

### C9. SlaModule
- **Purpose**: SLA calc/timer/recalc + escalation (FR-35..40)
- **Responsibilities**: คำนวณ due dates จาก `config_slaPolicy` + business calendar; timer + breach status; scheduled job สแกน near-due/overdue → escalate
- **Exposes**: `GET /api/v1/instances/{id}/sla`, `ISlaService.Calculate(instance)`, background `SlaMonitorJob`
- **Consumes**: SlaPolicyRepository, BusinessCalendarRepository, SlaTimerRepository, NotificationModule, AuditModule
- **Key decisions**: recalc เก็บค่าเดิม + เหตุผล (FR-37); escalation matrix table-driven
- **Error handling**: EX-65/66/67/68/70 → mark + แจ้งผู้ดูแล

### C10. VerificationModule
- **Purpose**: submit verification + rescan (mock) + result + closure/reopen (FR-43..47)
- **Exposes**: `POST /api/v1/instances/{id}/submit-verification`, `POST /api/v1/instances/{id}/rescan`, `POST /api/v1/instances/{id}/verification`, (closure/reopen ผ่าน WorkflowModule)
- **Consumes**: VerificationRepository, EvidenceModule, IScannerAdapter (mock), WorkflowModule, AuthModule, AuditModule
- **Key decisions**: Critical ต้อง independent verifier (verifiedBy ≠ remediation owner, BR-16); reopen เมื่อพบซ้ำ (BR-13)
- **Error handling**: EX-74/75/76/77/78/80

### C11. DashboardReportingModule
- **Purpose**: operations + executive dashboard + advanced search/filter (FR-56/57/58)
- **Exposes**: `GET /api/v1/dashboard/operations`, `GET /api/v1/dashboard/executive`, `GET /api/v1/instances?filters` (offset pagination D3-7)
- **Consumes**: InstanceRepository (read/aggregate), AuthModule (data scope)
- **Key decisions**: query ผ่าน data-scope filter กลางเสมอ; aggregate ด้วย EF Core group-by; สื่อสถานะด้วยสี+ไอคอน+label (NFR-34)
- **Error handling**: EX-93/94 (query timeout/scope violation)

### C12. AdministrationModule
- **Purpose**: master data + workflow config + SLA policy + risk weight config (FR-64/65/66, NFR-31)
- **Exposes**: `GET/POST/PUT /api/v1/admin/{master-data|status-transitions|sla-policies|risk-weights|assignment-rules}`
- **Consumes**: respective repositories, AuthModule, AuditModule
- **Key decisions**: ทุกการแก้ config ผ่านสิทธิ์ + audit (BR-40 change control)
- **Error handling**: EX-54 (invalid risk weight), validation ก่อนบันทึก

### C13. AuditModule (cross-cutting)
- **Purpose**: immutable append-only audit log + integrity (FR-68, NFR-20/21, D3-8)
- **Exposes**: `IAuditService.Record(action, objectType, objectId, before, after, result)`; `GET /api/v1/audit` (auditor, scoped)
- **Consumes**: AuditRepository (INSERT/SELECT only)
- **Key decisions**: hash chain (prevHash → rowHash SHA-256); ไม่มี update/delete path; เขียนผ่าน interceptor/middleware บน consequential actions
- **Error handling**: EX-98 (write fail → block action หรือ mark+alert), EX-100 (integrity fail)

### C14. NotificationModule (cross-cutting)
- **Purpose**: in-app + email/teams (mock) + history (FR-38..42)
- **Exposes**: `INotificationService.Notify(recipients, type, payload)`, `GET /api/v1/notifications` (in-app), background delivery
- **Consumes**: NotificationRepository, IEmailAdapter + ITeamsAdapter (mock)
- **Key decisions**: in-app จริง; email/teams = mock (log delivery); dedupe storm (EX-71)
- **Error handling**: EX-42/43/72/73 → mark delivery status + retry policy (NFR-24)

### C15. IntegrationModule (mock adapters) (cross-cutting infra)
- **Purpose**: interface + mock impl ของ external systems (D3-5, workshop policy)
- **Exposes interfaces**: `ICmdbAdapter`, `IScannerAdapter`, `IEmailAdapter`, `ITeamsAdapter`, `ISiemAdapter`, `IItsmAdapter`; mock impl อ่าน seed data (CSV/JSON/DB)
- **Key decisions**: เลือก mock/real ผ่าน DI + config (`Integration:UseMock=true`); timeout/retry/idempotency/error-queue (NFR-24, BR-36/37)

---

## Frontend Components (Angular)

### F1. CoreShell + AuthGuard
- **Purpose**: responsive app shell following the [SVM design system](../../../../.kiro/steering/design-system.md), routing, auth interceptor (attach JWT), role-based route guard, X-Forwarded-Proto aware
- **Navigation**: collapsible white 240px desktop sidebar; accessible mobile drawer; preserve Dashboard, Vulnerabilities (list/detail), Intake, Remediation, Verification, Admin, and Audit routes.
- **Consumes**: AuthService (login/me), API client

#### F1 authentication UI slice
- Public route `/login` renders outside CoreShell; protected routes use a shared guard and a safe in-app return destination.
- `AuthService` owns session state and expiry; an `AuthProvider` interface owns credential verification so mock and future backend identity do not leak into feature components.
- Mock mode uses the public workshop account and a per-tab browser session. It labels its limitations visibly; disabled real-auth mode fails closed rather than falling back to a demo identity.
- CoreShell shows the current user/role and an actual logout action on desktop and mobile. Expiry clears state and redirects to login with a readable reason.
- Follow the [authentication surface contract](../../../../docs/ui/authentication.md) for UI states, timeout semantics and verification. Backend C1, server-side RBAC/data scopes and auth audit remain separate incomplete work.

### F2. DashboardViews (Operations + Executive)
- **Contract**: follow the [dashboard surface contract](../../../../docs/ui/dashboard.md) for hierarchy, filters, data behavior, and interaction states.
- **Consumes**: DashboardApi; communicate status with text, icon, and color

### F3. VulnerabilityListView + Detail
- **Purpose**: worklist + advanced search/filter (offset pagination), finding detail (risk breakdown, history, evidence, SLA timer, workflow actions)
- **Consumes**: InstanceApi, WorkflowApi, EvidenceApi

### F4. IntakeViews
- **Purpose**: file import (upload + result), manual finding form, dedup-resolve queue
- **Consumes**: IntakeApi, DataQualityApi

### F5. AssignmentRemediationViews
- **Purpose**: assign/bulk-assign, remediation task + plan + progress + evidence upload + comment
- **Consumes**: AssignmentApi, TaskApi

### F6. VerificationViews
- **Purpose**: submit verification, rescan request, verification result
- **Consumes**: VerificationApi

### F7. AdminViews
- **Purpose**: master data, status transitions, SLA policies, risk weights, assignment rules, users/roles
- **Consumes**: AdminApi

### F8. AuditView
- **Purpose**: audit search + history (auditor, read-only)
- **Consumes**: AuditApi

---

## Interactions

### Component relationships (backend)
```
                         ┌──────────────┐
  Angular SPA ─REST─────▶│ AuthModule   │◀── ทุก controller (policy + scope)
                         └──────┬───────┘
                                │ ICurrentUser
  Intake ─▶ DataQuality ─▶ (Instance) ─▶ RiskScoringEngine ─▶ Workflow
     │           │                              │                │
     │       ReferenceData                      │            Assignment/Remediation ─▶ Sla ─▶ Verification
     │                                          │                │                     │        │
     └─────────────────────────── Audit (cross-cutting) ◀────────┴─────────────────────┴────────┘
                                   Notification (cross-cutting) ◀── Assignment/Sla/Verification/Workflow
                                   Integration mock adapters ◀── Asset(CMDB)/Intake(Scanner)/Notification(Email/Teams)
  Dashboard/Reporting ─reads─▶ Instance (scoped)      Administration ─config─▶ (weights/policies/transitions/rules)
```
(ไม่มี cycle: cross-cutting Auth/Audit/Notification ถูก consume ทางเดียว; Workflow→Verification→Workflow ผ่าน interface ไม่สร้าง DI cycle)

### Data flow (intake → closure)
```
Upload/Manual → Intake (batch) → DataQuality (validate→normalize→dedup)
  → Instance created/linked → ReferenceData enrich (CVE/CWE/KEV)
  → RiskScoringEngine (score 0–100 + level) → Sla (due dates + timer)
  → Workflow (New→Confirmed) → Assignment (owner) → RemediationTask (+evidence)
  → submit → Verification (rescan mock / manual, independent for Critical)
  → Workflow (Resolved→Closed) | Reopen เมื่อพบซ้ำ
  → ทุกขั้น consequential: Audit.Record + Notification.Notify (ตามเงื่อนไข)
```
