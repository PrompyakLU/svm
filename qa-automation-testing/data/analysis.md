# Analysis — SVM (Security Vulnerability Management)

> Stage 2 (Analyze) ของ QA Automation Testing · workshop scope · ภาษา: ไทย
> วันที่วิเคราะห์: 2026-10-21

## 1. Project Overview

SVM (Security Vulnerability Management) เป็นแพลตฟอร์มกลางระดับองค์กรสำหรับรวบรวม ประเมินความเสี่ยง บริหารวงจรการแก้ไข และกำกับดูแลช่องโหว่ด้านความมั่นคงปลอดภัยสารสนเทศ ระบบรวมผลตรวจพบช่องโหว่จากหลายแหล่ง (scanner/manual/import) เข้ากับข้อมูลทรัพย์สินและบริบทธุรกิจ เพื่อคำนวณคะแนนความเสี่ยง (0–100 แบบ weighted configurable) กำหนด SLA และบริหารกระบวนการแก้ไขตั้งแต่ตรวจพบจนปิดรายการ

ผู้ใช้งานหลักครอบคลุม 16 user class — เด่นคือ Vulnerability Management Officer, Security Analyst, Asset Owner, Remediation Owner, Risk Owner/Approver, Auditor, Management และ System/Cybersecurity Administrator ระบบใช้ RBAC + data segregation ตามหน่วยงาน/ขอบเขตความรับผิดชอบ และมี audit trail แบบ append-only hash-chained บนทุก action สำคัญ

ขอบเขตปัจจุบันเป็น **core end-to-end slice** (32 user stories US-001..032) ครอบคลุม auth, asset, intake, data quality, reference data, risk scoring, workflow 13 สถานะ, assignment/remediation, SLA/escalation, verification/closure, dashboard, admin config, audit ส่วนต่อระบบภายนอกทั้งหมดเป็น mock adapter + seed data

## 2. Tech Stack & Architecture

- **Frontend**: Angular 22 (standalone, strict TS) + Tailwind 4 + daisyUI 5 — SPA, lazy-loaded feature routes, light theme (teal/navy), รองรับ Thai/Latin (Noto Sans Thai)
- **Backend**: ASP.NET Core .NET 10 (C# 14) — Layered/Clean-ish (Api / Application / Domain / Infrastructure), REST `/api/v1`, OpenAPI, ProblemDetails (RFC 7807)
- **Database**: SQL Server + EF Core 10
- **Auth**: Local JWT หลัง `IAuthProvider` (OIDC-ready) — access token (in-memory) + refresh cookie (HttpOnly, SameSite=Strict) + CSRF antiforgery + rate limit บน login; policy-based authz + central data-scope filter
- **Integration**: external systems ทั้งหมดเป็น mock adapter (CMDB, Scanner, Email/Teams) via DI toggle `Integration:UseMock=true`
- **Data flow**: Angular component → HTTP (proxy `/api` → `:5181`) → API controller (policy check) → application/domain service → EF Core → SQL Server

**การรันในเครื่อง (dev)**: `dotnet run --project api/src/SvmApi.Api` (http://localhost:5181) + `ng serve` (http://localhost:4200, proxy ไป API); SQL Server ผ่าน `docker-compose.dev.yml`

## 3. Features / Modules Inventory

### Frontend views (8)
| View | Route | หน้าที่ |
|------|-------|--------|
| Login | `/login` | เข้าสู่ระบบ (guestGuard) — Thai UI, validation, show/hide password, generic error, demo disclosure |
| Dashboard | `/dashboard` | Operations + Executive dashboard (KPI, risk distribution, overdue, KEV) |
| Vulnerabilities (list) | `/vulnerabilities` | worklist + advanced filter + offset pagination |
| Vulnerability (detail) | `/vulnerabilities/:id` | risk breakdown, history, evidence, SLA, workflow actions |
| Intake | `/intake` | file import (CSV/JSON) + manual finding + dedup queue |
| Remediation | `/remediation` | remediation task/plan/progress + evidence |
| Verification | `/verification` | submit verification, rescan, record result |
| Admin | `/admin` | master data / workflow / SLA / risk weights / assignment rules / users |
| Audit | `/audit` | audit log search (scoped, immutable) |

### API controllers (14) → ~45 endpoints `/api/v1`
Auth · Asset · FindingIntake · (DataQuality/dedup) · Reference · InstanceRisk · InstanceWorkflow · AssignmentRemediation · InstanceSla · Notifications · InstanceVerification · InstanceQuery · Dashboard · Admin · Audit

## 4. Sources Discovered + Reference Ranking

| Source | Type | Ranking |
|--------|------|---------|
| `.aidlc/specs/svm/requirements.md` (32 US + EARS) | Doc (approved spec) | **Primary** — EARS criteria → expected results + positive/negative variants |
| `.aidlc/specs/svm/design/api-spec.md` (~45 endpoints) | Doc (API contract) | **Primary (API layer)** — method/path/permission/status codes |
| `.aidlc/specs/svm/design.md` + design/* | Doc (design) | Supporting — edge cases / error paths |
| `docs/ui/authentication.md` (16 acceptance) | Doc (UI contract) | **Primary (login UI)** |
| `docs/ui/dashboard.md` | Doc (UI contract) | Supporting (dashboard UI) |
| `.aidlc/specs/svm/tasks.md` | Doc | Supporting — implemented scope (1–7 done, 8 deploy pending) |
| source code (`web/src/app`, `api/src`) | Code | Supporting — ยืนยัน route/permission/endpoint จริง |

**เหตุผล**: Kiro spec เป็น approved source ที่แข็งแรงที่สุด → ใช้เป็น primary; api-spec.md เป็นสัญญา API ที่ชัดเจน map 1:1 กับ controller จริง

## 5. QA Layers Enabled + Active Domains

| Layer | Enabled | Domain skill | เหตุผล |
|-------|:---:|---|---|
| **UI** | ✅ | `automation-testing-domain-ui` | Angular SPA, 8 views + login |
| **API** | ✅ | `automation-testing-domain-api` | REST `/api/v1` ~45 endpoints, OpenAPI, ProblemDetails |
| **Unit-Integration** | ✅ | `automation-testing-domain-unit` | xUnit tests มีอยู่แล้ว (Risk/Workflow/Intake/Sla/Auth/Asset/Admin/Audit/Dashboard/Reference/Remediation/Verification); web ใช้ Vitest |

- **Scope level**: `full` (rich sources, 3 layers) — ตาม steering workshop
- **Target**: app ในรีโป (local), ไม่มี live URL

## 6. UI Driver

- ไม่พบ marker ของ Cypress/WebdriverIO/Selenium/Puppeteer/Playwright ในโปรเจกต์
- → ใช้ **Playwright (bundled default driver)** · `report_meta.tool = playwright`
- **Browser**: Chromium · **Device**: desktop 1440×900 เท่านั้น (steering workshop — ไม่รัน tablet/mobile)

## 7. Coverage Outline (per module — positive + negative, ตาม user journey)

จัดเรียงตาม user journey หลัก end-to-end · workshop = เน้น happy path + negative สำคัญ (ไม่แตก case ละเอียดเกิน)

| # | Journey / Module | Layer | Positive | Negative | US / source |
|---|------------------|-------|----------|----------|-------------|
| J1 | Login + session | UI+API | login สำเร็จ → dashboard | credential ผิด → generic error; required field ว่าง; protected URL redirect | US-001, auth.md |
| J2 | RBAC / authz | API | token ถูก → 200 | ไม่มี token → 401; ไม่มีสิทธิ์ → 403 | US-002 |
| J3 | Asset inventory | UI+API | list/filter asset (scoped) | เปิด asset นอก scope → 404/403 | US-003/004 |
| J4 | Vulnerability intake (import) | UI+API | import CSV/JSON สำเร็จ → batch summary | ไฟล์ผิด format/ว่าง → reject; partial result | US-007/010 |
| J5 | Manual finding | UI+API | กรอกครบ → finding New | mandatory ขาด → reject + ชี้ field | US-008 |
| J6 | Deduplication | API | dedup ambiguous → queue | — | US-012 |
| J7 | Risk scoring | API+Unit | score ∈ [0,100], classify ตาม threshold | weight รวมผิด/ติดลบ → 422 INVALID_RISK_WEIGHT | US-015/016 |
| J8 | Workflow transition | API+Unit | transition ที่อนุญาต สำเร็จ | transition ไม่อนุญาต → 422; ขาด evidence → 422; concurrency → 409 | US-018/019 |
| J9 | Assignment + evidence | API | assign/bulk-assign สำเร็จ | assignee นอก scope → skip; evidence malware → 422 | US-020/022 |
| J10 | SLA | API | SLA timer + due date แสดง | — | US-024/025 |
| J11 | Verification + closure | API | record Verified → closable | Critical ไม่มี independent verifier → 422 | US-028/029 |
| J12 | Dashboard | UI+API | operations + executive แสดง KPI | — | US-030 |
| J13 | Admin config | UI+API | แก้ risk weight/SLA policy สำเร็จ | weight invalid → 422 | US-031 |
| J14 | Audit trail | API | audit log search (auditor) | ผู้ไม่มีสิทธิ์ → 403 | US-032 |

**Out of scope** (เจตนา — deferred phase ถัดไป): Risk Acceptance/False Positive/Exception เต็มรูปแบบ + Approval Matrix, report/export ละเอียด, scanner API จริง, SIEM/ITSM/Email/Teams จริง, archiving/retention, DR/backup, MFA

## 8. Risk Priorities

| ระดับ | Modules |
|------|---------|
| **Critical** | Login/Auth (J1), RBAC/authz (J2), Risk scoring (J7), Workflow transition (J8), Audit (J14) |
| **High** | Intake (J4/J5), Dedup (J6), Assignment/evidence (J9), Verification/closure (J11), Data segregation (J3) |
| **Medium** | SLA (J10), Dashboard (J12), Admin config (J13) |
| **Low** | — |

เหตุผล: auth + authz + core logic (risk/workflow) + audit = หัวใจความถูกต้องและความปลอดภัยของระบบ → Critical

## 9. Test Data & Environment

- **Data source**: seed data เดิมในระบบ (ไม่สร้างใหม่/ไม่ใช้ PII จริง) — ตาม `SeedData.Access.cs`
- **บัญชีทดสอบ**: `admin` / `SvmDemo!2026` (Administrator, config mgmt); `officer1` (vulnerability flow) — รหัสจาก `Seed:DemoPassword`
- **Environment**: local/dev — web `:4200`, API `:5181`, SQL Server ผ่าน docker; **ไม่แตะ production**
- **Cleanup**: tests ที่ mutate ข้อมูลใช้ seed/transaction ของ dev เท่านั้น

## 10. Assumptions, Constraints, Open Questions

- **Assumption**: ต้องรัน SQL Server (docker) + API + web ได้จริงในเครื่อง workshop ก่อนรัน UI/API e2e; ถ้า env ไม่พร้อม Unit layer (xUnit/Vitest) ยังรันได้อิสระ
- **Constraint**: desktop 1440×900 only; ห้าม call external/SOAP API จริง (mock + log); จบรอบใน ~1 ชม.
- **Open**: ค่า `Seed:DemoPassword` / `ConnectionStrings:Svm` / `Auth:Jwt:SigningKey` ต้อง config ผ่าน env/user-secrets ก่อนรัน (ไม่ commit) — ต้องยืนยันว่า env พร้อมตอน Validate One
