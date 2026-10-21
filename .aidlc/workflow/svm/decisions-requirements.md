# Requirements Decisions (D1)

## Context Summary
- **ระบบ**: Security Vulnerability Management (SVM) — Greenfield, Enterprise Web App, scope `new`, comprehensive mode
- **Stack (ตัดสินแล้ว)**: Angular 22 + Tailwind 4 + daisyUI 5 / ASP.NET Core .NET 10 + EF Core 10 / SQL Server; REST `/api/v1`
- **เอกสารต้นทาง**: FR-01..70, NFR-01..40, EX-01..114, BR-01..45, user classes 16 กลุ่ม
- **ผู้ใช้เลือกไว้แล้ว**: ทำ core end-to-end slice ก่อน (Asset → Intake/Import → Dedup → Risk Scoring → Workflow/Assignment → SLA → Dashboard + RBAC/Audit); ระบบภายนอกใช้ mock adapter ทั้งหมด
- **คำถามด้านล่าง** โฟกัสเพื่อ "ตีกรอบ scope ของ requirements phase" และยืนยันขอบเขต MVP ก่อนเขียน user story + EARS

---

## Decision Questions

### D1-1: ขอบเขต Core Slice (MVP) สำหรับ workshop
**Question**: core slice ที่จะเขียน requirements + implement ให้ครบจริง ควรครอบคลุมโดเมนใดบ้าง (ส่วนที่เหลือจะเป็น TBD/phase ถัดไป)?
- 1) **Core minimal** — Asset + Vulnerability Intake (File Import + Manual) + Dedup + Risk Scoring + Workflow/Status + Assignment + Dashboard + RBAC + Audit
- 2) **Core + SLA & Verification** — ข้อ 1) + SLA calculation/timer/escalation + Verification/Closure/Reopen **(Recommended)**
- 3) **Core + SLA + Governance** — ข้อ 2) + Risk Acceptance / False Positive / SLA Extension / Exception + Approval Matrix
- 4) Other (ระบุ): _______

**Answer**: 2

---

### D1-2: วิธีรับข้อมูลช่องโหว่ (Vulnerability Intake) ใน MVP
**Question**: ช่องทางนำเข้า finding ที่ต้องทำงานได้จริงใน core slice?
- 1) **File Import (CSV/JSON) + Manual Registration** เท่านั้น — Scanner API เป็น mock adapter ไว้ขยายภายหลัง **(Recommended)**
- 2) File Import + Manual + Scanner API (mock) ครบทั้ง 3 ช่องทางตั้งแต่ MVP
- 3) Manual Registration อย่างเดียวก่อน
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-3: โมเดลการคำนวณ Risk Score (0–100)
**Question**: ตอนเริ่ม จะใช้สูตร risk scoring แบบใด (FR-16: CVSS, Asset Criticality, Business Impact, Internet Exposure, Exploit Availability, KEV, Threat Intelligence, Vulnerability Age, Existing Control)?
- 1) **สูตร weighted แบบ configurable** — เก็บ weight ใน config/master data ปรับได้ (สอดคล้อง NFR-31) **(Recommended)**
- 2) สูตร fixed hardcoded ตาม default ก่อน แล้วค่อยทำ configurable ภายหลัง
- 3) ใช้ CVSS base score เป็นหลัก + ปรับด้วย asset criticality/KEV เท่านั้น (ปัจจัยย่อย)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-4: การยืนยันตัวตน (Authentication) ใน workshop
**Question**: NFR-09 ระบุ SSO ผ่าน IdP แต่ workshop ต่อ IdP จริงไม่ได้ จะใช้วิธีใดใน core slice?
- 1) **Local/mock auth provider** — username/password + role seed, ออกแบบหลัง interface เพื่อสลับเป็น OIDC จริงภายหลัง **(Recommended)**
- 2) OIDC จริงกับ IdP ที่เตรียมให้ (ถ้ามี)
- 3) ไม่มี auth ใน MVP (เปิดทุกหน้า) — ไม่แนะนำ ขัด NFR/บังคับ RBAC
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-5: ขอบเขต Persona / User Class ใน requirements
**Question**: เอกสารมี 16 user class — จะทำ persona ละเอียดเท่าใดใน core slice?
- 1) **ทำ persona หลักที่เกี่ยวกับ core flow** (เช่น Vulnerability Mgmt Officer, Security Analyst, Asset/System Owner, Remediation Owner, Risk Owner/Approver, Auditor, Management, System Admin) + สรุปที่เหลือ **(Recommended)**
- 2) ทำครบทั้ง 16 persona ละเอียด
- 3) ทำเฉพาะ role ที่กระทบ RBAC ขั้นต่ำ (3–4 role)
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-6: การสร้าง persona (บังคับถาม)
**Question**: ต้องการให้สร้างเอกสาร personas.md แยกหรือไม่?
- 1) **ใช่ สร้าง personas.md** — 16 user class ต่างกันชัดและขับเคลื่อน RBAC/journey **(Recommended)**
- 2) ไม่ รวม persona ไว้ใน requirements.md พอ
- 3) Other (ระบุ): _______

**Answer**: 1

---

### D1-7: ขอบเขต Dashboard & Report ใน MVP
**Question**: FR-56..60 มีหลาย dashboard/report — core slice ควรมีอะไร?
- 1) **Operations Dashboard + Executive Dashboard (สรุป) + Advanced Search/Filter** — report ละเอียด/export เป็น phase ถัดไป **(Recommended)**
- 2) Dashboard ครบ + Operational/Executive Report + Data Export ตั้งแต่ MVP
- 3) Search/Filter + รายการ (list) อย่างเดียวก่อน ยังไม่ทำ dashboard กราฟ
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-8: Non-Functional Requirements (NFR) analysis
**Question**: เอกสารมี NFR 40 ข้อ (รวม security, audit integrity, performance, DR) — จะจัดการอย่างไรใน requirements?
- 1) **เปิด NFR analysis** — สรุป NFR ที่ผูกกับ core slice เป็น acceptance criteria/constraint (เน้น security, RBAC, audit, encryption, input validation) **(Recommended)**
- 2) ระบุ NFR เป็น constraint ภาพรวม ไม่ลงรายละเอียดต่อ story
- 3) ข้าม NFR ใน requirements ไปจัดการที่ design
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-9: ขอบเขตที่ "ไม่ทำ" ใน core slice (Out of Scope)
**Question**: ยืนยันสิ่งที่ **อยู่นอก** core slice (ทำเป็น TBD/phase ถัดไป) — เลือกได้ว่าเห็นด้วยหรือปรับ
- 1) **เห็นด้วยตามนี้**: Scanner API จริง, SIEM/ITSM/Email/Teams integration จริง, Compensating Control เต็มรูปแบบ, Report/Export ละเอียด, Data Archiving/Retention automation, DR/Backup, Reference data sync อัตโนมัติ (ใช้ seed) **(Recommended)**
- 2) ปรับรายการ out-of-scope (ระบุใน Other)
- 3) ไม่กำหนด out-of-scope ตอนนี้
- 4) Other (ระบุ): _______

**Answer**: 1

---

### D1-10: ขนาดทีมพัฒนา (บังคับถาม — ใช้ใน validation D2/D3/D5)
**Question**: มีนักพัฒนากี่คนในโปรเจกต์นี้?
- 1) **Solo (1 คน)** — workshop รายบุคคล **(Recommended สำหรับบริบท workshop)**
- 2) Small team (2–3)
- 3) Medium team (4–8)
- 4) Large team (9+)

**Answer**: 1

---

## Decisions Summary
<!-- Machine-readable compact summary. Downstream phases: read ONLY this section. -->
<!-- Auto-populated after user fills answers. One line per decision. -->
- D1-1 Core Slice Scope: Core + SLA & Verification (Asset, Intake, Dedup, Risk Scoring, Workflow/Status, Assignment, Remediation, SLA calc/timer/escalation, Verification/Closure/Reopen, Dashboard, RBAC, Audit)
- D1-2 Intake Channels: File Import (CSV/JSON) + Manual Registration เท่านั้น; Scanner API = mock adapter (phase ถัดไป)
- D1-3 Risk Scoring Model: Weighted แบบ configurable (เก็บ weight ใน config/master data; สอดคล้อง NFR-31)
- D1-4 Authentication: Local/mock auth provider (username/password + role seed), ออกแบบหลัง interface เพื่อสลับเป็น OIDC ภายหลัง
- D1-5 Persona Depth: ทำ persona หลักที่เกี่ยวกับ core flow (8 role) + สรุปที่เหลือ
- D1-6 Create personas.md: ใช่ สร้าง personas.md แยก
- D1-7 Dashboard/Report Scope: Operations Dashboard + Executive Dashboard (สรุป) + Advanced Search/Filter; report ละเอียด/export = phase ถัดไป
- D1-8 NFR Analysis: เปิด NFR analysis — ผูก NFR core (security, RBAC, audit, encryption, input validation) เป็น acceptance criteria/constraint
- D1-9 Out of Scope: Scanner API จริง, SIEM/ITSM/Email/Teams integration จริง, Compensating Control เต็มรูปแบบ, Report/Export ละเอียด, Archiving/Retention automation, DR/Backup, Reference data sync อัตโนมัติ (ใช้ seed)
- D1-10 Team Size: Solo (1 developer)

---

**Instructions**: กรอกคำตอบในช่อง **Answer** แต่ละข้อ แล้วตอบว่า "done" — หรือพิมพ์ "use recommendations" เพื่อให้ผมเติมตัวเลือกที่แนะนำทั้งหมด


---

## Validation Notes (D1)
ตรวจกับ D1 validation rules — ผลลัพธ์: **ไม่มี conflict ค้าง** (high-severity rules ถูกบรรเทาด้วยตัวเลือกของผู้ใช้เอง)

| Rule | Severity | ผล |
|---|---|---|
| Enterprise Scope + Solo Developer | 🔴 High | ✅ บรรเทาแล้ว — เลือก MVP core slice (D1-1=2) + กำหนด out-of-scope ชัด (D1-9) + บริบท workshop |
| Compliance Without Security Stories | 🔴 High | ✅ บรรเทาแล้ว — เปิด NFR analysis (D1-8) จะใส่ security/RBAC/audit/PDPA เป็น criteria |
| Heavy Integration Load (≥5) | 🔴 High | ✅ บรรเทาแล้ว — ใช้ mock adapter + phasing (D1-2, D1-9) |
| Complex Features Without Personas | 🟢 Low | ✅ ไม่มีปัญหา — personas=Yes (D1-6) |
| Performance Without NFR Flag | 🟡 Medium | ✅ ไม่มีปัญหา — NFR analysis=On (D1-8) |
| Broad Scope Without Boundaries | 🟡 Medium | ✅ ไม่มีปัญหา — out-of-scope กำหนดชัด (D1-9) |

สรุป: ผ่าน validation → ไปขั้นสร้าง requirements
