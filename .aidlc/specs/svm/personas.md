# Personas

## Summary
<!-- Compact digest for downstream phases. -->
- **User Types**: 8 personas หลัก (core flow) + สรุปอีก 8 user class ที่เหลือ (รวม 16 ตามเอกสาร)
- **Key Roles**: System Administrator, Vulnerability Management Officer, Security Analyst, Asset/System Owner, Remediation Owner, Risk Owner/Approver, Auditor/Compliance, Management/Executive
- **Design Implications**: ต้องมี RBAC + organizational data-scope segregation, Segregation of Duties (ผู้ยื่น ≠ ผู้อนุมัติ), audit log ทุก action สำคัญ, responsive (desktop หลัก + tablet/mobile); UI ให้ยึด [SVM design system](../../../.kiro/steering/design-system.md)

## Overview
ระบบ SVM ให้บริการผู้ใช้ 16 user class ที่มีสิทธิ์และ data scope ต่างกัน เอกสารนี้ลงรายละเอียด 8 persona หลักที่ขับเคลื่อน core end-to-end flow (intake → risk → workflow → SLA → verification → dashboard) และสรุป persona ที่เหลือเพื่อให้ RBAC ครบ

---

## System Administrator

**Role**: ผู้ดูแลระบบ (เทคนิค/ตั้งค่า)

**Goals**:
- จัดการ user, role, permission, master data, workflow/SLA/notification config
- ตรวจสอบ system health และสถานะ integration

**Pain Points**:
- ตั้งค่ากระจายหลายที่ ไม่มีจุดเดียวจบ
- ไม่เห็นสถานะ integration ว่าล้มเหลวตรงไหน

**User Journey**: เข้าเมนู Administration → ตั้งค่า master data / role / SLA policy → ตรวจ integration monitoring

**Implications**: ต้องมีเมนู Administration แยก, แก้ไข config ได้แต่ **ห้ามแก้/ลบ audit log**, ไม่ควรอนุมัติ business decision (Risk Acceptance/False Positive)

---

## Vulnerability Management Officer

**Role**: เจ้าหน้าที่บริหารช่องโหว่ (เจ้าของกระบวนการหลัก)

**Goals**:
- คัดกรอง/ยืนยัน finding, ตรวจ duplicate, มอบหมาย owner
- ติดตาม SLA และ escalation ให้ปิดช่องโหว่ทันเวลา

**Pain Points**:
- Finding ซ้ำจำนวนมากจากหลาย scanner
- ไม่รู้ว่ารายการไหนใกล้/เกิน SLA

**User Journey**: ดูรายการ finding ใหม่ → ยืนยัน/ปฏิเสธ → ตรวจ duplicate → มอบหมาย remediation owner → ติดตาม SLA

**Implications**: ต้องมี worklist + filter + bulk assignment, SLA timer เด่นชัด, **ห้ามอนุมัติ Risk Acceptance ที่ตนยื่น**, ห้ามปิด Critical โดยไม่มี verification

---

## Security Analyst

**Role**: นักวิเคราะห์ความปลอดภัย (เชิงเทคนิค/ตรวจสอบ)

**Goals**:
- วิเคราะห์ CVE/CWE/CVSS/Exploit, ยืนยันผลตรวจพบ
- ทำ verification ผลการแก้ไข หรือเสนอ reopen

**Pain Points**:
- ข้อมูล finding ไม่มีบริบทเทคนิคครบ
- ไม่มีที่แนบ/ดูหลักฐานการ rescan

**User Journey**: เปิด finding → วิเคราะห์เทคนิค + เพิ่ม comment/recommendation → ปรับสถานะ Under Review/Confirmed → verification

**Implications**: ต้องเข้าถึง technical detail + evidence, ปรับ severity/risk ได้แต่ต้องมีเหตุผล+audit, Independent Verification สำหรับ Critical (ผู้ verify ≠ ผู้แก้ไข)

---

## Asset Owner / System Owner

**Role**: เจ้าของทรัพย์สิน/ระบบ (รับผิดชอบความเสี่ยง)

**Goals**:
- ดูช่องโหว่ของ asset/ระบบที่รับผิดชอบ และวางแผนแก้ไข
- ยื่น business justification / risk acceptance เมื่อจำเป็น

**Pain Points**:
- เห็นช่องโหว่กระจาย ไม่รู้ลำดับความสำคัญ
- ไม่มีช่องทางขอยอมรับความเสี่ยงอย่างเป็นระบบ

**User Journey**: ดู dashboard ของ asset ตน → มอบหมายทีมแก้ไข → ติดตาม progress → ยื่น risk acceptance/SLA extension

**Implications**: data scope จำกัดเฉพาะ asset/หน่วยงานตน, **ยื่นได้แต่ไม่อนุมัติของตนเอง** (SoD), เห็น SLA + remediation progress

---

## Remediation Owner

**Role**: ผู้รับผิดชอบการแก้ไข (Infra/Dev/ทีมเทคนิค)

**Goals**:
- ดำเนินการแก้ไข บันทึกแผน/ความคืบหน้า แนบหลักฐาน
- ส่งรายการเข้า Pending Verification

**Pain Points**:
- ไม่รู้ว่าต้องแก้อะไรก่อน (ขาด recommendation/priority)
- งานถูกมอบหมายโดยไม่รู้ context

**User Journey**: รับงานที่ถูก assign → ปรับสถานะ In Progress → บันทึก root cause/action/evidence → ส่ง verification

**Implications**: เห็นเฉพาะงานในขอบเขตที่ได้รับมอบหมาย, **ปิด/อนุมัติงานตนเองไม่ได้**, เชื่อมโยง change/ITSM reference (mock)

---

## Risk Owner / Approver

**Role**: ผู้พิจารณาความเสี่ยง / ผู้อนุมัติ

**Goals**:
- อนุมัติ/ปฏิเสธ Risk Acceptance, False Positive, SLA Extension, Exception, Closure ตาม approval matrix
- กำหนดเงื่อนไข/วันหมดอายุและติดตาม compensating control

**Pain Points**:
- ขาดข้อมูล/หลักฐานประกอบการตัดสินใจ
- ไม่มี worklist ของคำขอที่รออนุมัติ

**User Journey**: เปิด approval worklist → ตรวจเหตุผล/หลักฐาน/ผลกระทบ → อนุมัติ/ปฏิเสธ/ส่งกลับ + comment

**Implications**: ต้องมี approval worklist + approval matrix ตาม risk level, **ห้ามอนุมัติคำขอที่มี conflict of interest / ของตนเอง**, Critical ต้องหลายระดับ, ลบประวัติอนุมัติไม่ได้

---

## Auditor / Compliance Officer

**Role**: ผู้ตรวจสอบ / กำกับการปฏิบัติตามข้อกำหนด

**Goals**:
- ตรวจ audit trail, SLA, approval, evidence, สิทธิ์และ SoD
- จัดทำ compliance report

**Pain Points**:
- หา audit record ย้อนหลังยาก
- ไม่มั่นใจว่า log ถูกแก้ไขหรือไม่

**User Journey**: ค้นหา audit record ตามเงื่อนไข → ดูประวัติการเปลี่ยนแปลง/อนุมัติ → export (ตามสิทธิ์)

**Implications**: สิทธิ์ **read-only** เป็นหลัก, เปลี่ยนสถานะ/แก้ข้อมูลไม่ได้, การ export ต้องได้รับอนุญาตและถูกบันทึก, audit log ต้อง immutable + integrity check

---

## Management / Executive

**Role**: ผู้บริหาร

**Goals**:
- ติดตามภาพรวมความเสี่ยง, Critical/KEV, SLA breach, ประสิทธิภาพการแก้ไข
- ใช้ข้อมูลตัดสินใจและจัดสรรทรัพยากร

**Pain Points**:
- ข้อมูลเยอะเกิน ไม่เห็นภาพรวมเชิงบริหาร
- ไม่รู้หน่วยงานไหนมีความเสี่ยงค้างสูง

**User Journey**: เปิด Executive Dashboard → ดูสรุปตาม business unit/risk level/trend → drill-down ตามสิทธิ์

**Implications**: Executive dashboard สรุป + KPI (MTTA/MTTR, SLA compliance), แก้ technical finding ไม่ได้, ข้อมูลละเอียดอาจถูก mask ตาม data classification

---

## สรุป Persona ที่เหลือ (RBAC coverage)

| User Class | หน้าที่หลัก | ข้อจำกัดสำคัญ |
|---|---|---|
| Cybersecurity Administrator | กำหนดมาตรฐาน risk model/severity/SLA/scanner source/escalation | เปลี่ยนนโยบายต้องผ่าน change control; แก้ audit log ไม่ได้ |
| SOC Analyst | เชื่อมโยง threat intelligence/incident, ติดตาม critical/KEV | เปลี่ยน ownership/ปิดรายการไม่ได้ (ถ้าไม่มี role เพิ่ม) |
| Application Owner | รับผิดชอบ application ที่มีช่องโหว่, วางแผนแก้ไข | ปิดช่องโหว่โดยไม่มี verification ไม่ได้ |
| Infrastructure Team | แก้ช่องโหว่ server/OS/DB/network/cloud | เห็นเฉพาะ infra asset ในขอบเขต |
| Application Development Team | แก้ช่องโหว่ SAST/DAST/SCA, source/dependency | เข้าถึง source finding จำกัดตาม project |
| Approver (ทั่วไป) | พิจารณาคำขอตามประเภท | อนุมัติคำขอที่ตนสร้างไม่ได้; มี approval SLA |
| External Security Assessor | บันทึก/ส่งผล pentest ตาม project | เข้าถึงเฉพาะ project ที่ได้รับมอบหมาย; บัญชีมีวันหมดอายุ |
| External System / API Client | ส่ง/รับข้อมูลผ่าน API | service account, scoped, rate limit, audit |

---

## Design Implications

- **Architecture**: ต้องมี RBAC + policy-based authorization + organizational data-scope segregation; บังคับ Segregation of Duties (ผู้ยื่น ≠ ผู้ตรวจ ≠ ผู้อนุมัติ); audit log immutable กับทุก consequential action
- **UI/UX**: worklist/queue + filter เป็นหัวใจ (officer/approver), dashboard แยกตาม role (operations vs executive), SLA timer/status เด่นชัด, สื่อความหมายด้วยสี+ไอคอน+label (ไม่พึ่งสีอย่างเดียว), responsive; current theme and layout follow the [SVM design system](../../../.kiro/steering/design-system.md)
- **Data & Privacy**: data scope จำกัดตาม business unit/asset/responsibility, mask credential/token/personal data ตาม role (BR-32, PDPA), export ต้องตรวจสิทธิ์+บันทึก
