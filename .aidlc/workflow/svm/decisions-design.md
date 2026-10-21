# Design Decisions (D3)

> This D3 record preserves the original design inputs and decisions as history. Current UI direction is authorized by the user and defined in the [SVM design system](../../../.kiro/steering/design-system.md); it supersedes the earlier Ferrari and dark SOC directions.

## Context Summary
- **ระบบ**: SVM core slice (32 user stories, 11 โดเมน), Greenfield, solo, comprehensive mode
- **Stack (ตัดสินแล้ว — ไม่ถามซ้ำ)**: Angular 22 + Tailwind 4 + daisyUI 5 / ASP.NET Core .NET 10 Web API (nullable C#) + EF Core 10 / **SQL Server**; REST `/api/v1`, OpenAPI, ProblemDetails, policy-based authz, OpenTelemetry (ตาม dipd-mcp baseline)
- **Design reference**: Ferrari Racing (ขาว-ดำ high-contrast, editorial, flat, pill button)
- **คำถามด้านล่าง** โฟกัสเฉพาะการตัดสินใจ design ที่ยัง "เปิด" สำหรับ SVM — ไม่ถามซ้ำสิ่งที่ baseline กำหนดแล้ว

---

## Decision Questions

### D3-1: โครงสร้าง Repository / Solution
**Question**: จัดโครงสร้างโปรเจกต์อย่างไร (web + api + db ในที่เดียว)?
- 1) **Single repo, 2 โฟลเดอร์** — `web/` (Angular) + `api/` (.NET solution หลาย project: Api/Domain/Infrastructure) **(Recommended)**
- 2) Single repo, .NET project เดียว (API) + web/ แยก (ง่ายกว่าแต่แยก layer น้อย)
- 3) Multi-repo (web แยก repo จาก api)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-2: การจัดเลเยอร์ภายใน API (.NET)
**Question**: รูปแบบการจัดโครงภายใน backend?
- 1) **Layered/Clean-ish**: Api (controllers) → Application (services/use-cases) → Domain (entities/rules) → Infrastructure (EF Core, mock adapters) **(Recommended)**
- 2) Vertical Slice (จัดตาม feature folder, MediatR)
- 3) Simple layered: Controllers → Services → Repositories (บางกว่า เหมาะ solo/workshop)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-3: รูปแบบ Risk Scoring Engine (configurable)
**Question**: ออกแบบ engine คำนวณ risk score (0–100) ที่ weight ปรับได้อย่างไร?
- 1) **Weighted-sum service + weight config ใน DB (master data)** — normalize แต่ละปัจจัยเป็น 0–100 แล้วถ่วงน้ำหนัก, อ่าน weight ตอน runtime, recalc ได้ **(Recommended)**
- 2) Rule engine แบบ pluggable (strategy per factor) — ยืดหยุ่นสุดแต่ซับซ้อนกว่า
- 3) Weighted-sum + weight ใน appsettings/config file (ปรับต้อง redeploy)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-4: การ implement Workflow/Status Transition (13 สถานะ)
**Question**: ควบคุม state transition + guard (permission/evidence/approval) อย่างไร?
- 1) **State machine table-driven** — นิยาม allowed transitions + guard rules ใน config/DB, service ตรวจก่อนเปลี่ยน **(Recommended, สอดคล้อง config-driven NFR-31)**
- 2) Hardcoded transition logic ใน service (เร็วแต่แก้ยาก)
- 3) ใช้ library state machine (เช่น Stateless) ผูกกับ domain
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-5: รูปแบบ Mock Integration Adapters
**Question**: ออกแบบ mock adapter (Scanner/CMDB/ITSM/Email/Teams/SSO) ให้สลับเป็นของจริงภายหลังได้อย่างไร?
- 1) **Interface + Mock implementation + seed data (CSV/JSON/DB)**, เลือกผ่าน DI/config (`UseMock=true`) **(Recommended ตามนโยบาย workshop)**
- 2) In-memory stub ใน service โดยตรง (เร็วแต่สลับยาก)
- 3) Mock HTTP server แยก (เหมือนจริงสุดแต่ overhead สูง)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-6: Authentication/Authorization implementation (local/mock)
**Question**: ทำ auth แบบ local/mock ที่สลับเป็น OIDC ได้ อย่างไร?
- 1) **JWT ที่ API ออกเอง (local login) + ASP.NET policy-based authz + role/scope claims**, ซ่อนหลัง interface `IAuthProvider` เพื่อสลับเป็น OIDC/Cognito ภายหลัง **(Recommended)**
- 2) Cookie-based session auth (ASP.NET Identity)
- 3) Mock auth แบบ fixed header/role selector (ง่ายสุด เหมาะ demo)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-7: API pagination / filtering convention
**Question**: รูปแบบ list API สำหรับรายการช่องโหว่จำนวนมาก (search/filter FR-58)?
- 1) **Offset pagination + query filter params + sort** (`?page=&pageSize=&riskLevel=&...`) **(Recommended สำหรับ workshop/ปริมาณปานกลาง)**
- 2) Cursor/keyset pagination (ดีกับข้อมูลมากแต่ซับซ้อนกว่า)
- 3) ส่งทั้งหมดแล้ว filter ฝั่ง client (เฉพาะชุดเล็ก)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-8: Audit Log immutability (NFR-21)
**Question**: ทำให้ audit log แก้ไข/ลบไม่ได้โดย app user/admin อย่างไร (ระดับ workshop)?
- 1) **Append-only table + ไม่มี update/delete endpoint + DB permission จำกัด + hash chain (prev-hash) ตรวจ integrity** **(Recommended)**
- 2) Append-only table + ไม่มี update/delete code path (ไม่มี hash chain)
- 3) เขียน audit ไปไฟล์/ภายนอกแยก (overhead สูงสำหรับ workshop)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-9: Testing scope (ตามนโยบาย hands-on — test ไว้ท้าย/optional)
**Question**: ขอบเขตการทดสอบใน core slice (Working-Software เป็นเกณฑ์ขั้นต่ำ)?
- 1) **Minimal + verify-by-running** — unit test เฉพาะ logic สำคัญ (risk scoring, dedup, transition guard, SLA calc) + ยืนยันด้วยการรัน dev server/คลิกใช้งานจริง **(Recommended ตามนโยบาย)**
- 2) Standard — unit test ครอบคลุม service layer + integration test API หลัก
- 3) Full — unit + integration + Playwright E2E critical flows
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-10: Correctness & Property-Based Testing (บังคับถาม)
**Question**: ต้องการ property-based testing สำหรับ logic ที่มี invariant ชัด (เช่น risk score ต้องอยู่ 0–100 เสมอ, dedup ต้อง idempotent) หรือไม่?
- 1) **ไม่ทำ property-based** — ใช้ example-based unit test + invariant assertion ธรรมดา **(Recommended สำหรับ workshop solo)**
- 2) ทำ property-based เฉพาะ risk scoring + dedup (invariant เด่น)
- 3) ทำ property-based ครอบคลุม core logic ทั้งหมด
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D3-11: Observability Strategy (บังคับถาม — Operations)
**Question**: ระดับ observability ที่ระบบต้องการ?
- 1) **Minimal** — structured logging + health endpoint **(Recommended สำหรับ workshop/internal)**
- 2) Standard — logging + metrics + health + readiness
- 3) Full — logging + metrics + distributed tracing + alerting + dashboards
- 4) None — ข้าม (prototype เท่านั้น)
- 5) Other (ระบุ): _______

**Answer**: 1

---

### D3-12: Error Tracking (บังคับถาม — Operations)
**Question**: จับและรายงาน runtime error อย่างไร?
- 1) **Log-based only** — error อยู่ใน structured log, query ผ่าน log platform **(Recommended สำหรับ workshop)**
- 2) Dedicated service (Sentry/Datadog/Rollbar)
- 3) Cloud-native (CloudWatch/GCP/Azure Monitor)
- 4) None
- 5) Other (ระบุ): _______

**Answer**: 1

---

### D3-13: Health & Lifecycle Management (บังคับถาม — Operations)
**Question**: lifecycle management ที่ service ต้องการ?
- 1) **Basic health endpoint only** (เหมาะ deployment ง่าย/workshop) **(Recommended)**
- 2) Health + readiness + graceful shutdown (เหมาะ container/orchestrated)
- 3) Health + readiness + graceful shutdown + startup probe + drain delay (zero-downtime)
- 4) Other (ระบุ): _______

**Answer**: 1

---

## Decisions Summary
<!-- Machine-readable compact summary. Downstream phases: read ONLY this section. -->
<!-- Auto-populated after user fills answers. One line per decision. -->
- D3-1 Repo Structure: Single repo, web/ (Angular) + api/ (.NET solution: Api/Application/Domain/Infrastructure)
- D3-2 API Layering: Layered/Clean-ish (Api → Application → Domain → Infrastructure)
- D3-3 Risk Scoring Engine: Weighted-sum service + weight config ใน DB (master data), normalize 0–100, runtime weight, recalc ได้
- D3-4 Workflow Transition: State machine table-driven (allowed transitions + guard rules ใน config/DB)
- D3-5 Mock Adapters: Interface + Mock implementation + seed data, เลือกผ่าน DI/config (UseMock)
- D3-6 Auth Implementation: Local JWT ที่ API ออกเอง + policy-based authz + role/scope claims, ซ่อนหลัง IAuthProvider (OIDC-ready)
- D3-7 API Pagination/Filter: Offset pagination + query filter params + sort
- D3-8 Audit Immutability: Append-only table + no update/delete endpoint + restricted DB permission + hash chain (prev-hash) integrity
- D3-9 Testing Scope: Minimal + verify-by-running (unit test logic สำคัญ: risk scoring, dedup, transition guard, SLA calc)
- D3-10 Property-Based Testing: ไม่ทำ property-based — example-based unit test + invariant assertion
- D3-11 Observability: Minimal — structured logging + health endpoint
- D3-12 Error Tracking: Log-based only (structured log)
- D3-13 Health/Lifecycle: Basic health endpoint only

---

**Instructions**: กรอกคำตอบในช่อง **Answer** แต่ละข้อ แล้วตอบว่า "done" — หรือพิมพ์ "use recommendations" เพื่อให้ผมเติมตัวเลือกที่แนะนำทั้งหมด

---

## Validation Notes (D3)
ตรวจกับ D3 validation rules (Technology Compatibility / Architecture / Security / Workflow-Cost / Operations) — ผลลัพธ์: **ไม่มี conflict**

| หมวด | ผล |
|---|---|
| Operations: Full observability for solo | ✅ ไม่เข้าเงื่อน — เลือก Minimal (D3-11) |
| Operations: No observability for production | ✅ ไม่เข้าเงื่อน — Minimal (ไม่ใช่ None) + บริบท workshop/internal |
| Operations: error tracking vs metrics | ✅ สอดคล้อง — Log-based + Minimal |
| Operations: full lifecycle without container | ✅ สอดคล้อง — Basic health |
| Technology compatibility | ✅ ทุกตัวเลือกเข้ากับ .NET/Angular/SQL Server (baseline) |
| Security | ✅ local JWT + policy authz + audit hash chain; PDPA/security ครอบใน NFR |
| Architecture/Performance | ✅ layered monolith + offset pagination เหมาะกับ scale workshop |

สรุป: ผ่าน validation → ไปขั้นสร้าง design documents
