# Requirements — SVM (Security Vulnerability Management)

## Summary
<!-- 10-line max, downstream reads ONLY this -->
- **Total Stories**: 32 user stories ใน 11 functional area (core slice: Core + SLA & Verification)
- **Priority**: 22 High, 8 Medium, 2 Low
- **User Types**: System Admin, Vulnerability Mgmt Officer, Security Analyst, Asset/System Owner, Remediation Owner, Risk Owner/Approver, Auditor, Management (+ RBAC coverage ของ user class ที่เหลือ)
- **Key Entities**: Asset, VulnerabilityFinding, VulnerabilityInstance, RiskScore, RemediationTask, SlaPolicy/SlaTimer, VerificationRecord, Evidence, User/Role, AuditLog, ReferenceData (CVE/CWE/CVSS/KEV)
- **Integrations**: ทั้งหมดเป็น mock adapter (Scanner, CMDB/ITAM, ITSM, Email/Teams, SSO) — ใช้ seed data
- **Core flows**:
  1. Import/Manual finding → validate → normalize → dedup → เชื่อม asset
  2. Risk scoring (0–100, weighted configurable) → risk level → KEV/exposure prioritization
  3. Workflow (13 สถานะ) → assignment → remediation task → evidence
  4. SLA calc/timer → reminder/escalation เมื่อใกล้/เกินกำหนด
  5. Verification → closure/reopen; dashboard + search/filter; RBAC + audit ทุก action

## Scope (core slice ตาม D1)
**In scope**: Asset Management, Vulnerability Intake (File Import + Manual), Data Quality (validate/normalize/dedup), Reference Data (CVE/CWE/CVSS/KEV — seed), Risk Assessment, Workflow & Status, Assignment & Remediation, Evidence, SLA & Escalation, Verification/Closure/Reopen, Operations+Executive Dashboard, Advanced Search/Filter, RBAC + Data Segregation, Audit Trail, Administration (master data / workflow / SLA policy config), mock Integration layer.
**Out of scope (TBD/phase ถัดไป)**: Scanner API จริง, SIEM/ITSM/Email/Teams จริง, Risk Acceptance/False Positive/SLA Extension/Exception เต็มรูปแบบ + Approval Matrix, Compensating Control เต็มรูปแบบ, Report/Export ละเอียด, Data Archiving/Retention automation, DR/Backup, Reference data sync อัตโนมัติ, MFA, Notification history ครบ.

---

## Functional Area 1: Access Control & Authentication

### US-001: เข้าสู่ระบบและรับสิทธิ์ตาม role
- **As a** ผู้ใช้งานระบบ (ทุก role)
- **I want** เข้าสู่ระบบด้วย local/mock auth แล้วได้สิทธิ์ตาม role ของตน
- **So that** เข้าถึงเฉพาะฟังก์ชัน/ข้อมูลที่ได้รับอนุญาต
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้ใช้ส่ง credential ที่ถูกต้อง, THEN ระบบสร้าง session และโหลด role + data scope ของผู้ใช้
  - IF credential ไม่ถูกต้องหรือบัญชีถูกปิด/หมดอายุ, THEN ปฏิเสธการเข้าระบบและแสดงข้อความทั่วไปโดยไม่เปิดเผยสาเหตุเชิงเทคนิค (EX-01, EX-04)
  - WHILE session ไม่มีการใช้งานเกินเวลาที่กำหนด, IF ผู้ใช้เรียก API, THEN ระบบปฏิเสธและบังคับ re-authenticate (NFR-17, EX-05)
  - The system shall ออกแบบ auth หลัง interface เพื่อสลับเป็น OIDC/SSO จริงได้โดยไม่แก้ business logic (NFR-09)
  - WHEN ผู้ใช้ที่ยังไม่มี session เปิดหน้าในระบบโดยตรง, THEN แสดงหน้าเข้าสู่ระบบและเก็บเฉพาะปลายทางภายในระบบที่อนุญาตไว้ เพื่อกลับไปหลังเข้าสู่ระบบสำเร็จ; ไม่ยอมรับ URL ภายนอกหรือวนกลับหน้า login
  - The system shall มีหน้าเข้าสู่ระบบแยกจาก navigation ของผู้ใช้ที่เข้าสู่ระบบแล้ว ใช้ label ภาษาไทย, autocomplete สำหรับ username/current-password, แสดง/ซ่อนรหัสผ่าน, validation, loading และข้อความผิดพลาดที่เข้าถึงได้ด้วย keyboard/screen reader
  - WHEN ผู้ใช้เข้าสู่ระบบแล้ว, THEN แสดงตัวตนและบทบาทที่ได้รับจาก auth provider พร้อมปุ่มออกจากระบบบน desktop และ mobile
  - WHEN ผู้ใช้ออกจากระบบหรือ session หมดอายุ, THEN ล้าง session ฝั่ง client และกลับหน้า login; การ reload หรือย้อนกลับต้องไม่เปิดหน้าที่ป้องกันไว้โดยไม่มี session ที่ยังใช้ได้
  - WHERE ใช้ mock auth ใน workshop, THEN ระบุว่าเป็นข้อมูลจำลองและบัญชีตัวอย่างอย่างชัดเจน; route guard/session ฝั่ง browser ไม่ถือว่าทำ backend authentication, RBAC, data segregation หรือ audit ครบแล้ว
- **Dependencies**: —
- **Source**: D1-4, NFR-09/17, EX-01/04/05

**Frontend flow contract**: ดู [authentication.md](../../../docs/ui/authentication.md) สำหรับหน้าจอ, state, timeout, test scenarios และขอบเขต mock เทียบกับ backend จริง การทำ UI slice นี้ไม่ใช่การปิด US-001/002/003 ทั้งหมด

### US-002: ควบคุมสิทธิ์ตาม role (RBAC) ทุกฟังก์ชันและ API
- **As a** System Administrator
- **I want** กำหนดและบังคับสิทธิ์ตาม role กับทุก function และ API
- **So that** ผู้ใช้ทำได้เฉพาะสิ่งที่ได้รับอนุญาต (least privilege)
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall ตรวจสอบ role และ data scope ก่อนอนุญาตทุก action และทุก API endpoint (NFR-11)
  - IF ผู้ใช้เรียกฟังก์ชันที่ไม่มีสิทธิ์, THEN ระบบปฏิเสธด้วย 403 และบันทึก audit (EX-06)
  - The system shall กำหนดสิทธิ์เริ่มต้นเป็นขั้นต่ำ ไม่มีการเข้าถึงข้อมูลโดยอัตโนมัติ (NFR-12)
- **Dependencies**: US-001
- **Source**: FR-62, NFR-11/12, EX-06

### US-003: จำกัดการเข้าถึงข้อมูลตามหน่วยงาน/ขอบเขต (Data Segregation)
- **As a** Asset Owner
- **I want** เห็นเฉพาะ finding/asset ในขอบเขตความรับผิดชอบของตน
- **So that** ไม่เห็นข้อมูลของหน่วยงานอื่นที่ไม่เกี่ยวข้อง
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้ใช้ query รายการ finding/asset, THEN ระบบ filter ตาม business unit / asset scope / responsibility scope ของผู้ใช้ (FR-63, BR-30)
  - IF ผู้ใช้พยายามเปิด record นอก data scope โดยตรง (เช่น ผ่าน id), THEN ปฏิเสธและบันทึก audit (EX-07)
  - WHERE ผู้ใช้มี role ระดับองค์กร (เช่น Auditor/Management), WHEN query, THEN เห็นข้ามหน่วยงานได้ตามสิทธิ์
- **Dependencies**: US-002
- **Source**: FR-63, BR-30, EX-07

---

## Functional Area 2: Asset Management

### US-004: ดูรายการทรัพย์สิน (Asset Inventory)
- **As a** Vulnerability Management Officer
- **I want** ดูรายการ asset ที่อยู่ภายใต้การบริหารช่องโหว่พร้อมข้อมูลสำคัญ
- **So that** เข้าใจบริบทของ asset ก่อนจัดการช่องโหว่
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall แสดงรายการ asset พร้อม Asset ID, Name, Type, IP/Hostname, Environment, Business Unit, Owner, Criticality, Internet Exposure, Status (FR-01)
  - WHEN ผู้ใช้ค้นหา/กรองตาม field ที่กำหนด, THEN แสดงเฉพาะ asset ที่ตรงเงื่อนไขและอยู่ใน data scope
- **Dependencies**: US-003
- **Source**: FR-01

### US-005: นำเข้า/ซิงก์ข้อมูล Asset จาก CMDB (mock)
- **As a** System Administrator
- **I want** นำเข้า/ปรับปรุงข้อมูล asset จาก CMDB mock adapter
- **So that** ข้อมูล asset เป็นปัจจุบันโดยไม่ต้องกรอกมือ
- **Priority**: Medium
- **Acceptance Criteria**:
  - WHEN ถึงรอบซิงก์หรือสั่ง sync, THEN ระบบรับข้อมูลจาก CMDB mock และบันทึกผล (สำเร็จ/ล้มเหลว/mapping ไม่ได้) (FR-02)
  - IF CMDB mock ส่งข้อมูลไม่ได้, THEN บันทึก integration error และคงข้อมูลเดิมไว้ (EX-30)
  - IF asset เดิมไม่ปรากฏในรอบล่าสุด, THEN ทำเครื่องหมายทบทวน ไม่ลบอัตโนมัติ (EX-31, BR-38)
- **Dependencies**: US-004
- **Source**: FR-02, EX-30/31, BR-38

### US-006: กำหนด Asset Criticality และ Ownership
- **As a** Asset Owner
- **I want** กำหนด/ยืนยัน criticality และเจ้าของของ asset
- **So that** การคำนวณความเสี่ยงและ SLA ถูกต้อง
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall รองรับ criticality อย่างน้อย Critical/High/Medium/Low พร้อมบันทึกผู้แก้ไข เวลา และเหตุผล (FR-04)
  - WHEN asset ใหม่เข้าระบบ, THEN เชื่อม Asset/System/Application Owner จาก source หรือ assignment rule (FR-03)
  - IF asset ใน production scope ไม่มี criticality, THEN ระบบเตือนและกันไม่ให้คำนวณ SLA ขั้นสุดท้ายจนกว่าจะกำหนด (BR-05, EX-28)
- **Dependencies**: US-004
- **Source**: FR-03/04, BR-05, EX-28

---

## Functional Area 3: Vulnerability Intake

### US-007: นำเข้า Finding จากไฟล์ (CSV/JSON)
- **As a** Vulnerability Management Officer
- **I want** นำเข้า vulnerability finding จากไฟล์ตาม template
- **So that** รวบรวมผลจาก scanner ที่ export เป็นไฟล์ได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้ใช้อัปโหลดไฟล์ที่รองรับ, THEN ระบบตรวจโครงสร้างและนำเข้า finding พร้อมแสดงผลเป็น Success/Partial/Failed (FR-05)
  - IF ไฟล์ไม่ใช่รูปแบบที่รองรับ หรือเกินขนาด หรือว่าง, THEN ปฏิเสธพร้อมข้อความระบุสาเหตุ (EX-11/12/13)
  - IF โครงสร้าง/ชื่อ column ไม่ตรง template, THEN รายงาน field ที่ผิดและไม่สร้างข้อมูลบางส่วนที่ไม่ครบ (EX-14, FR-08)
  - IF บางรายการสำเร็จบางรายการล้มเหลว, THEN บันทึกผลรายรายการและสรุป partial result (EX-18)
- **Dependencies**: US-004
- **Source**: FR-05/08, EX-11/12/13/14/18

### US-008: บันทึก Finding ด้วยตนเอง (Manual Registration)
- **As a** Security Analyst
- **I want** บันทึก finding จาก pentest/manual assessment ที่ไม่มี integration
- **So that** ครอบคลุมช่องโหว่ที่ไม่ได้มาจาก scanner
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้มีสิทธิ์กรอกข้อมูล finding ครบ mandatory field, THEN ระบบสร้าง finding ใหม่สถานะ New (FR-07)
  - IF ข้อมูล mandatory ไม่ครบ (asset, source, finding name, severity), THEN ปฏิเสธและชี้ field ที่ขาด (EX-15)
- **Dependencies**: US-004
- **Source**: FR-07, EX-15

### US-009: Import Template Management
- **As a** System Administrator
- **I want** มี template กลางสำหรับนำเข้าข้อมูล
- **So that** ผู้ใช้นำเข้าไฟล์ในรูปแบบที่ระบบรองรับได้ถูกต้อง
- **Priority**: Medium
- **Acceptance Criteria**:
  - The system shall มี template ระบุ mandatory field, data type และรูปแบบที่รองรับ (FR-08)
  - WHEN ผู้ใช้ดาวน์โหลด template, THEN ได้ไฟล์ที่ตรงกับ validation ปัจจุบันของระบบ
- **Dependencies**: US-007
- **Source**: FR-08

---

## Functional Area 4: Data Quality

### US-010: ตรวจสอบความถูกต้องของข้อมูล (Validation)
- **As a** ระบบ (ในนามผู้ดูแลข้อมูล)
- **I want** ตรวจสอบ finding ก่อนจัดเก็บ
- **So that** ข้อมูลมีคุณภาพและคำนวณความเสี่ยงได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ระบบรับ finding, THEN ตรวจ mandatory field, data type, asset reference, severity, CVSS, source identifier ก่อนจัดเก็บ (FR-09)
  - IF ค่าข้อมูลไม่ตรงประเภท (วันที่/คะแนน/identifier), THEN ปฏิเสธรายการนั้นพร้อมเหตุผล (EX-16)
- **Dependencies**: US-007, US-008
- **Source**: FR-09, EX-16

### US-011: ปรับข้อมูลให้เป็นมาตรฐาน (Normalization)
- **As a** ระบบ
- **I want** แปลงข้อมูลจากหลายแหล่งให้อยู่ใน common data model
- **So that** finding จากต่างแหล่งเทียบและประมวลผลร่วมกันได้
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall map ข้อมูลจาก source ต่าง ๆ เข้าสู่ common data model เดียวกัน (FR-10)
  - WHERE source ส่ง severity/field ในรูปแบบต่างกัน, WHEN normalize, THEN แปลงเป็นค่ามาตรฐานของระบบ
- **Dependencies**: US-010
- **Source**: FR-10

### US-012: ตรวจจับและรวมรายการซ้ำ (Deduplication)
- **As a** Vulnerability Management Officer
- **I want** ระบบตรวจและรวม finding ซ้ำกับ instance เดิม
- **So that** ไม่มีงานซ้ำและนับช่องโหว่ถูกต้อง
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN รับ finding ใหม่, THEN ตรวจซ้ำด้วย asset + vulnerability identifier + location + port + URL + parameter + component + source ตาม rule (FR-11, BR-12)
  - IF พบรายการซ้ำ, THEN เชื่อม finding ใหม่กับ instance เดิม ปรับ Last Detected Date และไม่สร้าง remediation task ซ้ำ (FR-12)
  - IF คล้ายหลายรายการจนตัดสินไม่ได้, THEN ส่งเข้าคิวให้ผู้ใช้ตัดสิน (EX-45)
- **Dependencies**: US-011, US-004
- **Source**: FR-11/12, BR-12, EX-45

---

## Functional Area 5: Reference Data (seed)

### US-013: เชื่อมโยง CVE/CWE และ CVSS
- **As a** Security Analyst
- **I want** finding ที่มี CVE/CWE เชื่อมกับข้อมูลอ้างอิง และเก็บ CVSS
- **So that** วิเคราะห์เชิงเทคนิคและคำนวณความเสี่ยงได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding มี CVE หรือ CWE, THEN เชื่อมกับ reference data (seed) และแสดงรายละเอียด (FR-13)
  - The system shall รองรับ CVSS version ที่อนุมัติ และเก็บ Base Score, Vector String, Severity (FR-14)
  - IF CVSS version ไม่รองรับ หรือ vector ผิดรูปแบบ, THEN บันทึก finding แต่ทำเครื่องหมายให้ตรวจสอบ (EX-48/49)
  - IF CVE ไม่พบใน reference data, THEN คงค่าที่ส่งมาและทำเครื่องหมาย CVE Not Found (EX-47)
- **Dependencies**: US-010
- **Source**: FR-13/14, EX-47/48/49

### US-014: ระบุ KEV (Known Exploited Vulnerabilities)
- **As a** SOC Analyst / Security Analyst
- **I want** ให้ระบบระบุว่า CVE อยู่ในรายการ KEV (seed) หรือไม่
- **So that** จัดลำดับความสำคัญช่องโหว่ที่ถูกใช้โจมตีจริงได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN CVE ของ finding ตรงกับรายการ KEV (seed), THEN ตั้งค่า KEV indicator และเพิ่ม priority (FR-18, BR-03)
  - WHILE finding เป็น KEV, THEN แสดง KEV badge ชัดเจนในรายการและหน้า detail
- **Dependencies**: US-013
- **Source**: FR-18, BR-03

---

## Functional Area 6: Risk Assessment

### US-015: คำนวณ Risk Score (0–100, weighted configurable)
- **As a** Cybersecurity Administrator
- **I want** ระบบคำนวณ risk score จากหลายปัจจัยด้วย weight ที่ปรับได้
- **So that** จัดลำดับความเสี่ยงตามบริบทเทคนิค+ธุรกิจ ไม่พึ่ง CVSS อย่างเดียว
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding ผ่าน validation, THEN คำนวณ risk score 0–100 จาก CVSS, Asset Criticality, Business Impact, Internet Exposure, Exploit Availability, KEV, Threat Intelligence, Vulnerability Age, Existing Control (FR-16)
  - The system shall อ่าน weight จาก configuration/master data (ไม่ hardcode) และคำนวณซ้ำได้เมื่อ weight เปลี่ยน (NFR-31, D1-3)
  - IF ข้อมูลไม่ครบจนคำนวณไม่ได้ หรือผลออกนอกช่วง 0–100, THEN ทำเครื่องหมาย error และไม่ให้ score ที่ผิดถูกใช้งาน (EX-50/51)
  - IF ผลรวม weight ไม่ถูกต้องหรือมีค่าติดลบ, THEN ปฏิเสธ configuration นั้น (EX-54)
- **Dependencies**: US-010, US-006, US-013, US-014
- **Source**: FR-16, BR-02, NFR-31, EX-50/51/54

### US-016: จำแนก Risk Level
- **As a** Vulnerability Management Officer
- **I want** ให้ระบบจำแนก risk level จาก score ตาม rule
- **So that** สื่อสารและกำหนด SLA ตามระดับได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN คำนวณ risk score เสร็จ, THEN จำแนกเป็น Critical/High/Medium/Low/Informational ตาม threshold ที่อนุมัติ (FR-17, BR-01)
  - WHERE finding เป็น KEV หรือ asset เป็น internet-facing + ระดับ High/Critical, THEN ยกระดับการแจ้งเตือนตาม escalation matrix (FR-18/19, BR-04)
- **Dependencies**: US-015
- **Source**: FR-17/18/19, BR-01/04

### US-017: ปรับ Risk Score/Level ด้วยตนเอง และคำนวณใหม่
- **As a** Security Analyst
- **I want** ขอปรับ risk score/level พร้อมเหตุผล และให้ระบบ recalc เมื่อปัจจัยเปลี่ยน
- **So that** ความเสี่ยงสะท้อนบริบทจริงและเป็นปัจจุบัน
- **Priority**: Medium
- **Acceptance Criteria**:
  - WHEN ผู้มีสิทธิ์ปรับ risk, THEN บันทึกค่าเดิม ค่าใหม่ เหตุผล ผู้แก้ไข และเวลา (FR-20)
  - WHEN asset criticality / KEV / threat intelligence เปลี่ยน, THEN ระบบคำนวณ risk score ใหม่ (FR-21)
  - IF scanner severity, CVSS severity และ risk level ไม่ตรงกัน, THEN ทำเครื่องหมาย conflicting severity ให้ตรวจสอบ (EX-53)
- **Dependencies**: US-015, US-016
- **Source**: FR-20/21, EX-53

---

## Functional Area 7: Workflow & Status

### US-018: บริหารสถานะช่องโหว่ (13 สถานะ)
- **As a** Vulnerability Management Officer
- **I want** ให้ finding มีสถานะตามวงจรที่กำหนด
- **So that** ติดตามความคืบหน้าได้ชัดเจน
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall รองรับสถานะ New, Under Review, Confirmed, Assigned, In Progress, Pending Verification, Resolved, Closed, Reopened, False Positive, Risk Accepted, Exception Approved, Archived (FR-22)
  - WHEN finding ถูกสร้าง, THEN สถานะเริ่มต้นเป็น New
- **Dependencies**: US-010
- **Source**: FR-22

### US-019: ควบคุมการเปลี่ยนสถานะ (Transition Control)
- **As a** ระบบ (บังคับ business rule)
- **I want** ตรวจเงื่อนไขก่อนเปลี่ยนสถานะ
- **So that** กระบวนการเป็นไปตาม workflow และมีหลักฐานครบ
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้ใช้ขอเปลี่ยนสถานะ, THEN ตรวจ source status, target status, permission, mandatory field, evidence, approval ก่อนดำเนินการ (FR-23, BR-27)
  - IF transition ไม่อยู่ใน workflow ที่อนุญาต, THEN ปฏิเสธพร้อมเหตุผล (EX-55)
  - IF ส่งเข้า Pending Verification/Closed โดยไม่มีหลักฐานครบ, THEN ปฏิเสธ (EX-56, BR-14)
  - IF ผู้ใช้สองคนแก้ finding เดียวกันพร้อมกัน, THEN ตรวจ concurrent update และกันการเขียนทับ (EX-57)
- **Dependencies**: US-018, US-002
- **Source**: FR-23, BR-27/14, EX-55/56/57

---

## Functional Area 8: Assignment & Remediation

### US-020: มอบหมายผู้รับผิดชอบ (Auto/Manual/Bulk)
- **As a** Vulnerability Management Officer
- **I want** มอบหมาย finding ให้ owner/ทีม ทั้งอัตโนมัติ ด้วยตนเอง และแบบกลุ่ม
- **So that** งานถึงผู้รับผิดชอบที่ถูกต้องรวดเร็ว
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding ได้รับการยืนยัน, THEN มอบหมายตาม asset/business unit/technology/assignment rule โดยอัตโนมัติ (FR-24)
  - WHEN officer มอบหมาย/เปลี่ยนผู้รับผิดชอบด้วยตนเอง, THEN บันทึกเหตุผลและผู้ทำ (FR-25)
  - WHERE เลือกหลายรายการ, WHEN bulk assign, THEN มอบหมายทุกรายการที่ผ่าน validation ให้บุคคล/ทีมเดียวกัน (FR-26)
  - IF หา assignee ไม่ได้ หรือ assignee ถูกปิดบัญชี/นอก scope, THEN ส่งเข้าคิวและแจ้ง (EX-59/60, BR-06)
- **Dependencies**: US-019, US-006
- **Source**: FR-24/25/26, BR-06/07, EX-59/60

### US-021: สร้างและบริหาร Remediation Task + Plan + Progress
- **As a** Remediation Owner
- **I want** รับ task บันทึกแผน root cause/action และปรับความคืบหน้า
- **So that** จัดการการแก้ไขอย่างเป็นระบบ
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding ถูกมอบหมาย, THEN สร้าง remediation task ที่มี Task ID, Owner, Due Date, Risk Level, Recommendation, Status (FR-27)
  - WHEN remediation owner บันทึกแผน, THEN เก็บ root cause, action plan, target date, change reference, dependency (FR-28)
  - WHEN owner ปรับความคืบหน้า, THEN อัปเดต % progress, status, comment, obstacle (FR-29)
  - IF target date เกิน SLA โดยไม่มี extension, THEN เตือนและทำเครื่องหมาย (EX-63)
- **Dependencies**: US-020
- **Source**: FR-27/28/29, EX-63

### US-022: แนบหลักฐาน (Evidence Management)
- **As a** Remediation Owner / Security Analyst
- **I want** แนบหลักฐาน (scan result, screenshot, config, change record)
- **So that** ใช้ประกอบการ verify และปิดรายการ
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall รองรับการแนบ evidence และเชื่อมโยงกับ finding/task/ผู้อัปโหลด (FR-31, BR-42)
  - WHEN อัปโหลดไฟล์, THEN ตรวจชนิด ขนาด นามสกุล และ malware ก่อนรับ (NFR-18)
  - IF ตรวจพบ malware หรือไฟล์ถูกเข้ารหัสจนตรวจไม่ได้, THEN ปฏิเสธและบันทึก (EX-21/22)
  - The system shall ไม่อนุญาตให้แทนที่ evidence โดยไม่มี version history (BR-42)
- **Dependencies**: US-021
- **Source**: FR-31, NFR-18, BR-42, EX-21/22

### US-023: Comment และ Mention
- **As a** ผู้ใช้งานที่มีสิทธิ์
- **I want** เพิ่ม comment และ mention ผู้เกี่ยวข้องใน finding
- **So that** ประสานงานในบริบทของช่องโหว่ได้
- **Priority**: Low
- **Acceptance Criteria**:
  - WHEN ผู้มีสิทธิ์เพิ่ม comment, THEN บันทึก comment พร้อมผู้เขียนและเวลา (FR-30)
  - WHERE mention ผู้ใช้, WHEN บันทึก, THEN แจ้งเตือน in-app ไปยังผู้ถูก mention
- **Dependencies**: US-018
- **Source**: FR-30

---

## Functional Area 9: SLA & Escalation

### US-024: คำนวณ SLA ตาม Risk Level
- **As a** Vulnerability Management Officer
- **I want** ให้ระบบคำนวณ due date ตาม SLA policy
- **So that** รู้กรอบเวลา acknowledge/start/resolve
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding ได้ risk level, THEN คำนวณ Acknowledgement Due, Remediation Start, Resolution Due ตาม SLA policy (FR-35, BR-08)
  - The system shall เริ่มนับ SLA เมื่อ finding ผ่าน validation และมีข้อมูล asset เพียงพอ (BR-09)
  - IF ไม่พบ SLA policy ที่ตรง หรือ calendar ไม่สมบูรณ์, THEN ทำเครื่องหมายและแจ้งผู้ดูแล (EX-65/66/67)
- **Dependencies**: US-016, US-006
- **Source**: FR-35, BR-08/09, EX-65/66/67

### US-025: SLA Timer และสถานะใกล้/เกินกำหนด
- **As a** Vulnerability Management Officer
- **I want** เห็นเวลาคงเหลือและสถานะ SLA
- **So that** จัดการรายการที่ใกล้/เกินกำหนดได้ทัน
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall แสดงเวลาคงเหลือ สถานะใกล้ครบกำหนด และ SLA Breach ของแต่ละ finding (FR-36)
  - WHEN risk level หรือ SLA policy เปลี่ยน, THEN คำนวณ SLA ใหม่โดยเก็บค่าเดิมและเหตุผล (FR-37, BR-10)
  - IF risk level สูงขึ้น, THEN ใช้ due date ที่เข้มงวดกว่า (BR-10)
- **Dependencies**: US-024
- **Source**: FR-36/37, BR-10, EX-68

### US-026: แจ้งเตือนและ Escalation
- **As a** Remediation Owner / Asset Owner / ผู้บังคับบัญชา
- **I want** ได้รับการแจ้งเตือนก่อน/เมื่อเกิน SLA และกรณี critical
- **So that** ไม่พลาดรายการสำคัญ
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN finding/task ถูกมอบหมาย, THEN แจ้งผู้รับผิดชอบผ่าน in-app (Email/Teams = mock adapter) (FR-38)
  - WHEN ใกล้ครบ SLA ตามช่วงที่กำหนด, THEN ส่ง reminder (FR-39)
  - WHEN finding เกิน SLA, THEN escalate ไปยัง owner/asset owner/ผู้บังคับบัญชา ตาม escalation matrix และจำนวนวันที่เกิน (FR-40, BR-11)
  - WHEN พบ Critical/KEV/active exploitation, THEN แจ้งผ่านช่องทางเร่งด่วน (FR-41)
  - IF ส่ง notification ล้มเหลว (mock) หรือ recipient ไม่ถูกต้อง, THEN บันทึกสถานะ delivery และ retry ตาม policy (EX-42/43/73, FR-42)
- **Dependencies**: US-025, US-020
- **Source**: FR-38/39/40/41/42, BR-11, EX-42/43/70/73

---

## Functional Area 10: Verification, Closure & Reopen

### US-027: ส่งตรวจสอบและขอ Rescan
- **As a** Remediation Owner / Security Analyst
- **I want** ส่ง finding เข้า Pending Verification พร้อมหลักฐาน และขอ rescan (mock)
- **So that** ยืนยันการแก้ไขก่อนปิด
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN remediation เสร็จ, THEN owner แนบหลักฐานและส่ง finding ไป Pending Verification (FR-43, BR-14)
  - WHEN analyst ขอ rescan, THEN สร้าง rescan request ผ่าน scanner mock หรือ manual rescan request (FR-44)
  - IF ส่ง rescan ไม่สำเร็จ หรือไม่พบ target, THEN บันทึกและแจ้งผู้ขอ (EX-74/75)
- **Dependencies**: US-022
- **Source**: FR-43/44, BR-14, EX-74/75

### US-028: บันทึกผล Verification
- **As a** Security Analyst
- **I want** บันทึกผล verified / not resolved / partially resolved พร้อมหลักฐาน
- **So that** ตัดสินใจปิดหรือ reopen ได้
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผู้ตรวจสอบบันทึกผล, THEN เก็บผล (Verified/Not Resolved/Partially Resolved) พร้อมหลักฐาน (FR-45)
  - WHERE finding เป็น Critical, WHEN verify, THEN ต้องเป็น independent verifier (ไม่ใช่ผู้แก้ไข) จึงจะผ่าน (BR-16, EX-80)
  - IF ผล rescan ยังพบช่องโหว่หรือไม่สรุป, THEN คงสถานะไม่ปิดและแจ้ง (EX-76/77/78)
- **Dependencies**: US-027
- **Source**: FR-45, BR-16, EX-76/77/78/80

### US-029: ปิดและเปิดรายการกลับ (Closure & Reopen)
- **As a** Vulnerability Management Officer
- **I want** ปิด finding เมื่อ verify ผ่าน และเปิดกลับเมื่อพบซ้ำ
- **So that** วงจรช่องโหว่ครบถ้วนและสะท้อนความจริง
- **Priority**: High
- **Acceptance Criteria**:
  - WHEN ผล verification ยืนยันว่าไม่พบช่องโหว่, THEN อนุญาตให้เปลี่ยนเป็น Closed ตาม approval rule (FR-46, BR-15)
  - WHEN scanner (mock) หรือ import ตรวจพบช่องโหว่เดิมอีกครั้ง หรือ verify ไม่ผ่าน, THEN เปลี่ยนเป็น Reopened และแจ้งผู้รับผิดชอบ (FR-47, BR-13)
  - IF Critical finding ถูกขอปิดโดยไม่มี independent verification, THEN ปฏิเสธการปิด (EX-80, BR-16)
- **Dependencies**: US-028, US-019
- **Source**: FR-46/47, BR-13/15/16, EX-80/81

---

## Functional Area 11: Dashboard, Search, Administration & Audit

### US-030: Operations & Executive Dashboard + Search/Filter
- **As a** Management / Vulnerability Management Officer
- **I want** เห็น dashboard ภาพรวมและค้นหา/กรองรายการช่องโหว่
- **So that** ติดตามสถานะและเจาะหารายการที่ต้องจัดการ
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall แสดง Operations Dashboard: รายการใหม่/unassigned, pending verification, overdue, import error, integration error (FR-57)
  - The system shall แสดง Executive Dashboard (สรุป): open vulnerabilities, risk distribution, critical trend, SLA compliance, KEV, top risk assets (FR-56)
  - WHEN ผู้ใช้ค้นหา/กรองตาม asset, owner, business unit, risk level, CVE, CWE, scanner, status, SLA, age, date range, THEN แสดงผลที่ตรงเงื่อนไขและอยู่ใน data scope (FR-58)
  - WHILE แสดงสถานะด้วยสี, THEN ต้องมีไอคอน/label กำกับเสมอ (ไม่พึ่งสีอย่างเดียว) (NFR-34)
- **Dependencies**: US-003, US-016, US-025
- **Source**: FR-56/57/58, NFR-34

### US-031: Administration — Master Data / Workflow / SLA Policy Config
- **As a** System Administrator / Cybersecurity Administrator
- **I want** จัดการ master data, workflow transition, SLA policy แบบ config-driven
- **So that** ปรับพฤติกรรมระบบได้โดยไม่แก้โค้ด
- **Priority**: Medium
- **Acceptance Criteria**:
  - The system shall ให้จัดการ severity, risk level, asset type, finding source, status reason, reference data (FR-64)
  - WHEN ผู้มีสิทธิ์กำหนด status transition, mandatory field, required evidence, approval rule, THEN ระบบใช้ค่าใหม่กับ workflow (FR-65)
  - WHEN กำหนด SLA ตาม risk level/criticality/exposure/KEV, THEN ระบบใช้ในการคำนวณ SLA (FR-66)
  - The system shall ปรับ risk weight, SLA, workflow, notification ผ่าน configuration ได้ (NFR-31) และการแก้ต้องผ่านสิทธิ์+change control (BR-40)
- **Dependencies**: US-002
- **Source**: FR-64/65/66, NFR-31, BR-40

### US-032: Audit Trail (immutable)
- **As an** Auditor / Compliance Officer
- **I want** ให้ทุก action สำคัญถูกบันทึกและแก้ไขไม่ได้
- **So that** ตรวจสอบย้อนหลังและพิสูจน์การปฏิบัติตามข้อกำหนด
- **Priority**: High
- **Acceptance Criteria**:
  - The system shall บันทึก audit log สำหรับ login, view, create, update, assign, status change, approve, reject, export, configuration change (FR-68, BR-33)
  - The system shall เก็บ user, date-time, source, action, object, before value, after value, result ในแต่ละ audit record (NFR-20)
  - The system shall ไม่อนุญาตให้ผู้ใช้ทั่วไปและผู้ดูแล application แก้ไข/ลบ audit log (NFR-21)
  - IF เขียน audit log ไม่สำเร็จสำหรับ action ควบคุม, THEN ปฏิเสธ action นั้นหรือทำเครื่องหมายและแจ้ง (EX-98/100)
  - WHEN auditor ค้นหา/export audit record (ตามสิทธิ์), THEN บันทึกการ export นั้นด้วย
- **Dependencies**: US-002
- **Source**: FR-68, NFR-20/21, BR-33, EX-98/100

---

## Story Summary

| ID | Title | Area | Priority | Dependencies |
|----|-------|------|----------|--------------|
| US-001 | Login + role resolution | Access/Auth | High | — |
| US-002 | RBAC enforcement | Access/Auth | High | US-001 |
| US-003 | Data segregation | Access/Auth | High | US-002 |
| US-004 | Asset inventory | Asset | High | US-003 |
| US-005 | Asset sync (CMDB mock) | Asset | Medium | US-004 |
| US-006 | Asset criticality + ownership | Asset | High | US-004 |
| US-007 | File import finding | Intake | High | US-004 |
| US-008 | Manual finding registration | Intake | High | US-004 |
| US-009 | Import template | Intake | Medium | US-007 |
| US-010 | Data validation | Data Quality | High | US-007/008 |
| US-011 | Normalization | Data Quality | High | US-010 |
| US-012 | Deduplication | Data Quality | High | US-011/004 |
| US-013 | CVE/CWE/CVSS mapping | Reference | High | US-010 |
| US-014 | KEV identification | Reference | High | US-013 |
| US-015 | Risk score (configurable) | Risk | High | US-010/006/013/014 |
| US-016 | Risk level classification | Risk | High | US-015 |
| US-017 | Manual risk adjust + recalc | Risk | Medium | US-015/016 |
| US-018 | Status management (13) | Workflow | High | US-010 |
| US-019 | Transition control | Workflow | High | US-018/002 |
| US-020 | Assignment (auto/manual/bulk) | Assign/Remediation | High | US-019/006 |
| US-021 | Remediation task/plan/progress | Assign/Remediation | High | US-020 |
| US-022 | Evidence management | Assign/Remediation | High | US-021 |
| US-023 | Comment + mention | Assign/Remediation | Low | US-018 |
| US-024 | SLA calculation | SLA | High | US-016/006 |
| US-025 | SLA timer + recalc | SLA | High | US-024 |
| US-026 | Notification + escalation | SLA | High | US-025/020 |
| US-027 | Submit verification + rescan | Verification | High | US-022 |
| US-028 | Verification result | Verification | High | US-027 |
| US-029 | Closure + reopen | Verification | High | US-028/019 |
| US-030 | Dashboards + search/filter | Dashboard | High | US-003/016/025 |
| US-031 | Admin config (master/workflow/SLA) | Admin | Medium | US-002 |
| US-032 | Audit trail (immutable) | Audit | High | US-002 |

## Story × Persona Matrix

| Story | SysAdmin | VM Officer | Sec Analyst | Asset/Sys Owner | Remediation Owner | Risk Owner/Approver | Auditor | Management |
|-------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| US-001 | P | P | P | P | P | P | P | P |
| US-002 | P | S | — | — | — | — | S | — |
| US-003 | S | P | S | P | S | S | P | S |
| US-004 | S | P | S | P | S | — | S | S |
| US-005 | P | S | — | — | — | — | — | — |
| US-006 | S | S | — | P | — | — | — | — |
| US-007 | S | P | S | — | — | — | — | — |
| US-008 | — | S | P | — | — | — | — | — |
| US-009 | P | S | — | — | — | — | — | — |
| US-010–012 | S | P | S | — | — | — | — | — |
| US-013–014 | — | S | P | — | — | S | — | — |
| US-015–017 | S(CyberAdmin) | P | P | S | — | S | — | — |
| US-018–019 | S | P | S | S | S | — | — | — |
| US-020 | — | P | — | S | S | — | — | — |
| US-021–022 | — | S | S | S | P | — | — | — |
| US-023 | — | S | S | S | S | — | — | — |
| US-024–025 | — | P | S | S | S | — | — | S |
| US-026 | — | P | S | P(asset) | P | S | — | S |
| US-027–029 | — | P | P | — | P(submit) | S | — | — |
| US-030 | S | P | S | S | S | S | S | P |
| US-031 | P | S(CyberAdmin) | — | — | — | — | — | — |
| US-032 | S | — | — | — | — | — | P | S |

(P = Primary, S = Secondary, — = ไม่เกี่ยวข้องโดยตรง)

## Non-Functional Notes (ผูกกับ core slice)
- **Security**: OWASP Top 10, input validation ทุก endpoint (ป้องกัน injection/XSS/CSRF), encryption at rest/in transit (NFR-14/15), secrets ไม่อยู่ใน code/config (NFR-16), secure file upload + malware scan (NFR-18), API security (authn/authz/validation/rate limit) (NFR-19)
- **Access**: RBAC + least privilege + Segregation of Duties (NFR-11/12/13) — บังคับทุก story ที่มี approval/assignment
- **Audit**: immutable audit log + integrity (NFR-20/21) — cross-cutting ทุก consequential action
- **Privacy (PDPA)**: data masking ของ credential/token/personal data (BR-32), ไม่ใช้ข้อมูลจริงใน dev/test
- **Performance/Capacity**: page/dashboard/API response ตามเป้าหมาย (NFR-02/03/04), async batch import (NFR-07) — รับทราบเป็น constraint, ตั้งค่าจริงที่ design
- **Usability/Accessibility**: responsive desktop/tablet/mobile (NFR-33), keyboard/label/contrast + สื่อความหมายไม่พึ่งสีอย่างเดียว (NFR-34) — ดู current UI direction ที่ [SVM design system](../../../.kiro/steering/design-system.md)
- **Reliability/Integration**: transaction integrity ไม่สร้างข้อมูลซ้ำ/ไม่ครบ (NFR-23), integration mock มี timeout/retry/idempotency/error queue (NFR-24, BR-36/37)
- **Maintainability**: modular architecture + configuration-driven (NFR-30/31)

## External References
| Source | Stories Derived | What Was Used |
|--------|-----------------|---------------|
| requirements .md (FR/NFR/EX/BR) | US-001..032 | FR-01..70 (core), NFR, EX, BR mapping |
| `.kiro/steering/design-system.md` | US-030 + NFR notes | current UI direction, status communication, contrast and accessibility |
| dipd-mcp development-guideline | NFR notes | security/authz/API conventions baseline |
