# Integration

## Overview
ระบบภายนอกทั้งหมดเข้าถึงผ่าน **interface + mock adapter + seed data** (D3-5, workshop policy) — เลือก mock/real ผ่าน DI + config (`Integration:UseMock=true`). ออกแบบ contract ให้ real adapter สลับเข้าได้โดยไม่แตะ business logic (adapter pattern หลัง interface). เป็น single unit (modular monolith) → ไม่มี inter-unit messaging; cross-module ภายในเรียกผ่าน interface แบบ in-process sync.

## External Integrations (mock)

### ICmdbAdapter (CMDB / IT Asset Management)
- **Purpose**: นำเข้า/ซิงก์ asset (FR-02)
- **Type**: REST (real) / mock อ่าน seed `assets.seed.json`
- **Auth (real)**: API key / OAuth (config)
- **Key operations**: `GetAssets()`, `GetAssetById(code)`
- **Error handling**: timeout 3s, retry 3× (exponential), on fail → บันทึก integration error, คงข้อมูลเดิม (EX-30/31). Idempotent ด้วย assetCode.

### IScannerAdapter (Vulnerability Scanner — SAST/DAST/SCA/Infra/Web/Cloud/Container)
- **Purpose**: rescan request + (future) pull scan result (FR-44); intake หลักใช้ file import ใน MVP
- **Type**: REST (real) / mock จำลองผล rescan จาก seed + สุ่มสถานะ (resolved/still-detected)
- **Key operations**: `RequestRescan(assetId, instanceRef)`, `GetRescanResult(requestId)`
- **Error handling**: timeout, retry, EX-32/33/34/35/74/75 → mark + แจ้งผู้ขอ. Deduplicate ผลซ้ำ (EX-38).

### IEmailAdapter / ITeamsAdapter (Notification channels)
- **Purpose**: ส่ง notification/escalation (FR-38..41)
- **Type**: HTTPS API (real: SES API / Microsoft Graph) / mock = log delivery + บันทึก `trans_notification.deliveryStatus`
- **Error handling**: EX-42/43/73 → mark Failed + retry (NFR-24); dedupe storm (EX-71). **ไม่ใช้ SMTP** (workshop/platform policy).

### IItsmAdapter (ITSM — out of core scope, interface only)
- **Purpose**: (future) create/sync ticket (FR-32/33) — MVP เป็น interface + stub ไว้ขยาย
- **Error handling**: EX-39/40/41 (defined, not implemented in core)

### ISiemAdapter (SIEM — out of core scope, interface only)
- **Purpose**: (future) ส่ง security event (FR อ้างอิง) — interface + stub
- **Error handling**: EX-44 (defined)

### IAuthProvider (SSO/IdP)
- **Purpose**: ยืนยันตัวตน (NFR-09) — MVP = `LocalAuthProvider` (JWT ออกเอง); real = `OidcAuthProvider`
- **Error handling**: EX-01/02/03 → generic auth error

## Inter-Module Communication (ภายใน monolith)
- **Pattern**: sync in-process ผ่าน interface (DI). ไม่มี message bus ใน MVP.
- **Scheduled jobs**: `SlaMonitorJob` (สแกน near-due/overdue → escalate), `AuditIntegrityJob` (ตรวจ hash chain เป็นระยะ) — ใช้ hosted service (`BackgroundService`) ของ ASP.NET Core.
- **Domain events (in-process)**: `InstanceCreated`, `RiskRecalculated`, `StatusChanged`, `SlaBreached` → trigger Notification/Audit (ใช้ simple in-memory mediator หรือ event dispatcher; ไม่ต้องใช้ external broker)

## Integration Config (seed data location)
```
api/SvmApi.Infrastructure/Integration/Mock/seed/
  assets.seed.json          (CMDB)
  cve.seed.json / cwe.seed.json / kev.seed.json / cvss-version.seed.json
  findings-sample.csv       (ตัวอย่าง import)
  users-roles.seed.json     (RBAC)
  risk-weights.seed.json / risk-thresholds.seed.json
  sla-policies.seed.json / business-calendar.seed.json
  status-transitions.seed.json / assignment-rules.seed.json
```

## Integration Testing
- **Strategy**: mock adapter มี deterministic seed → test business flow ได้โดยไม่พึ่งระบบจริง
- **Mocking approach**: DI inject mock impl; สำหรับ API integration test ใช้ in-memory/SQLite หรือ SQL Server test DB
- **Contract testing**: interface ชัดเจน → เมื่อสลับ real adapter ให้ test ตาม interface contract (future)

## Rules compliance
- ทุก external system มี error handling (ไม่ silent) — timeout/retry/idempotency/error-queue (NFR-24, BR-37)
- real/mock สลับผ่าน config เท่านั้น — business logic ไม่รู้ว่าเป็น mock (BR-35/36)
