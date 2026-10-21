# Tasks Decisions (D4)

## Context Summary
<!-- Auto-populated from design phase -->
- **Feature**: SVM (Security Vulnerability Management) — core end-to-end slice (Core + SLA & Verification)
- **Design scale**: 15 components (C1–C15), 8 Angular views (F1–F8), ~28 entities, ~45 endpoints `/api/v1`, 32 user stories (US-001..032, 0 gaps)
- **Stack**: Angular 22 + Tailwind 4/daisyUI 5 / ASP.NET Core .NET 10 (C# 14) / EF Core 10 / SQL Server
- **Architecture**: Layered/Clean-ish single repo (`web/` + `api/` 4 projects)
- **Integrations**: ทั้งหมดเป็น mock adapter (CMDB, Scanner, Email/Teams, Auth; ITSM/SIEM interface-only), toggle `UseMock`
- **Team**: Solo (1 developer)
- **Testing (D3)**: Minimal + verify-by-running (unit-test core logic เท่านั้น), ไม่มี property-based test
- **Policy constraint (hands-on-project — Task Execution Order บังคับ)**: ลำดับ wave = Setup → Core (FE + mock JSON) → Core (FE + Backend/DB จริง) → steps อื่น → test/verify (ท้าย/optional) → deploy (optional, ท้ายสุด) · หยุดรอ user ยืนยันเมื่อจบแต่ละกลุ่ม

---

## Decision Questions

### D4-1: Task breakdown strategy
**Question**: จะแบ่ง task ตามหลักไหนให้เหมาะกับ core slice ที่มี 15 components + 8 views และทำคนเดียว?
- 1) By layer (DB → API → FE แยกชั้น) — เห็นภาพ stack ชัด แต่ feature ใช้ได้ครบช้า
- 2) By vertical feature slice (ทำครบ FE→API→DB ต่อ 1 flow) — ได้ของใช้ได้เร็วทีละ flow **(Recommended)**
- 3) By component (1 component = 1 task) — ตรง design map แต่ task ย่อยเยอะ
- 4) Other (please specify): _______

**Answer**: 2

---

### D4-2: Implementation approach (test timing)
**Question**: จะเขียน test ตอนไหน (สอดคล้อง D3 = Minimal + verify-by-running และ policy ที่ให้ test ไว้ท้าย/optional)?
- 2) Test-after เฉพาะ core logic (risk scoring, workflow transition, dedup, SLA) หลัง flow ทำงานได้ + verify-by-running ทุกกลุ่ม **(Recommended)**
- 1) TDD (เขียน test ก่อน code ทุกส่วน) — ขัดกับ scope workshop/solo
- 3) ไม่เขียน unit test เลย ใช้ verify-by-running อย่างเดียว — ต่ำกว่าที่ D3 ตั้งไว้
- 4) Other (please specify): _______

**Answer**: 2

---

### D4-3: Component build priority (core flow ก่อน)
**Question**: หลัง Setup แล้ว ควรสร้างอะไรก่อนให้ได้ end-to-end flow เร็วที่สุด?
- 1) Foundation ก่อน (Auth C1 + Asset C2 + Reference C5) → Intake/DataQuality (C3/C4) → Risk (C6) → Workflow/Assignment (C7/C8) → SLA (C9) → Verification (C10) → Dashboard/Admin/Audit (C11/C12/C13) **(Recommended)**
- 2) Dashboard C11 ก่อน (เห็นภาพรวมเร็ว) แล้วค่อยเติม flow ย้อนหลัง
- 3) Admin/config C12 ก่อน (ตั้ง weight/SLA/transition) แล้วค่อยทำ flow
- 4) Other (please specify): _______

**Answer**: 1

---

### D4-4: Integration (external systems) strategy ระหว่าง dev
**Question**: จัดการ external system (CMDB/Scanner/Email/Teams/Auth) อย่างไรระหว่างพัฒนา core slice?
- 1) Mock adapter + seed data ตั้งแต่ task แรก, `UseMock=true` เป็น default, มี interface seam ให้สลับ real ภายหลัง **(Recommended)**
- 2) ยังไม่ทำ adapter เลย hardcode ค่าใน 服务 ไปก่อน แล้วค่อย refactor — เสี่ยงผิด policy
- 3) ทำ real adapter จริงเลยบางตัว — ขัด workshop policy (ห้าม call จริง)
- 4) Other (please specify): _______

**Answer**: 1

---

### D4-5: Testing scope ที่จะมี task รองรับ
**Question**: จะมี task สำหรับ test ประเภทใดบ้าง (ให้ตรง D3 Minimal)?
- 1) Unit test (xUnit) เฉพาะ core logic 4 จุด (RiskScoringEngine, Workflow transition guard, DataQuality dedup, SLA calc) + verify-by-running flow — ไม่มี E2E/load/property task **(Recommended)**
- 2) Unit + integration test (API endpoint) — เกิน scope workshop
- 3) Full pyramid (unit + integration + E2E Playwright) — เกิน scope มาก
- 4) Other (please specify): _______

**Answer**: 1

---

### D4-6: Task granularity (ขนาดต่อ task)
**Question**: 1 task ควรใหญ่แค่ไหน?
- 1) Single-concern (1 task = 1 ความสามารถที่ verify ได้, ขนาด ~0.5–2 วัน) **(Recommended)**
- 2) Atomic (ย่อยสุด เช่น 1 entity / 1 endpoint ต่อ task) — task เยอะเกินไปสำหรับ solo
- 3) Multi-concern (1 task = ทั้ง feature area) — ใหญ่เกิน ตรวจสอบยาก
- 4) Other (please specify): _______

**Answer**: 1

---

### D4-7: Execution model (sequential vs parallel) + wave ลำดับ
**Question**: รัน task แบบไหน (คำนึงถึง solo + policy Task Execution Order)?
- 1) Sequential ตาม wave ที่ policy บังคับ (Setup → FE+mock → FE+Backend → อื่นๆ → test/verify → deploy), หยุดรอ user ยืนยันจบแต่ละ wave **(Recommended)**
- 2) Parallel waves (หลาย track พร้อมกัน) — ไม่เหมาะกับ solo + ขัด policy หยุดรายกลุ่ม
- 3) Sequential by layer ล้วน (DB→API→FE) — ได้ของใช้ได้ช้า
- 4) Other (please specify): _______

**Answer**: 1

---

## Decisions Summary
<!-- Machine-readable compact summary. Downstream phases: read ONLY this section. -->
<!-- Auto-populated after user fills answers. One line per decision. -->
- D4-1 Breakdown strategy: Vertical feature slice (FE→API→DB ต่อ 1 flow)
- D4-2 Test timing: Test-after core logic (risk/workflow/dedup/SLA) + verify-by-running ทุกกลุ่ม
- D4-3 Build priority: Foundation ก่อน (Auth+Asset+Reference) → Intake/DQ → Risk → Workflow/Assignment → SLA → Verification → Dashboard/Admin/Audit
- D4-4 Integration strategy: Mock adapter + seed ตั้งแต่ task แรก, UseMock=true default, interface seam
- D4-5 Testing scope: Unit (xUnit) core logic 4 จุด + verify-by-running; ไม่มี E2E/load/property
- D4-6 Task granularity: Single-concern (1 task = 1 ความสามารถ verify ได้, ~0.5–2 วัน)
- D4-7 Execution model: Sequential ตาม wave policy (Setup→FE+mock→FE+Backend→อื่นๆ→test/verify→deploy), หยุดรอยืนยันจบแต่ละ wave

---

**Instructions**: Fill in your answers above and respond with "done"


---

## D4 Validation Notes
- **No Testing Strategy** 🔴 → N/A: D4-5 includes unit tests for core logic + verify-by-running (not "None").
- **Testing Framework Without Test Tasks** 🔴 → Mitigated: xUnit (D3) จะมี test task รองรับใน wave test/verify (RiskScoring, Workflow transition, dedup, SLA).
- **Task Count vs Capacity** 🔴 → Mitigated (not blocking): solo team + single-concern granularity อาจได้ ~25–35 tasks, แต่ execution เป็น sequential waves ที่มี stop-gate ต่อกลุ่มตาม hands-on-project policy (ไม่ใช่ความพยายามทำพร้อมกัน); scope ถูกจำกัดเป็น core slice แล้ว. ไม่ลด scope เพิ่ม.
- **DB Without Migration Tasks** 🟡 → Addressed: จะมี EF Core migration + seed task ใน setup/foundation wave.
- **CI/CD Without Pipeline Tasks** 🟡 → N/A: deploy เป็น optional ท้ายสุดตาม policy; ไม่บังคับ pipeline task ใน core slice.
- **Cloud Deploy Without Infra Tasks** 🔴 → N/A: deploy phase optional/deferred (workshop Day 1 = local).
- **Outcome**: No blocking conflicts. Proceed to task generation.
