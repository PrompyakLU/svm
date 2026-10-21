# Product

## Summary
- **Product**: Security Vulnerability Management (SVM) — แพลตฟอร์มกลางขององค์กรสำหรับรวบรวม ประเมินความเสี่ยง บริหารวงจรการแก้ไข และกำกับดูแลช่องโหว่ด้านความมั่นคงปลอดภัยสารสนเทศ
- **Users**: ทีม Cybersecurity, Security/SOC Analyst, Vulnerability Management Officer, Asset/System/Application Owner, Remediation Team, Risk Owner/Approver, Auditor, Management และระบบภายนอก (API Client) — รวม 16 user class
- **Type + scope**: Greenfield, Enterprise Web Application (scope `new`). Workshop โฟกัส core slice ที่ทำงานได้ end-to-end ก่อน แล้วขยายสู่ scope เต็ม

## Overview
SVM เป็นแพลตฟอร์มกลางที่รวมผลการตรวจพบช่องโหว่จากหลายแหล่ง (Infra/Web Scanner, SAST, DAST, SCA, Cloud/Container Scanner, Penetration Test, Manual Assessment, Threat Intelligence) เข้ากับข้อมูลทรัพย์สิน เจ้าของระบบ และบริบททางธุรกิจ เพื่อประเมินและจัดลำดับความเสี่ยง กำหนด SLA บริหารกระบวนการแก้ไขตั้งแต่ตรวจพบจนปิดรายการ รองรับการยอมรับความเสี่ยง/False Positive/Exception พร้อม Dashboard, Report, RBAC และ Audit Trail ครบวงจร ระบบไม่ได้แทนที่ Scanner แต่ทำหน้าที่รวมศูนย์ ประเมิน และกำกับดูแล

## Problem Statement
องค์กรมีผลการตรวจช่องโหว่กระจายอยู่หลายเครื่องมือ ไม่มีศูนย์กลางในการรวม จัดลำดับความเสี่ยงตามบริบทธุรกิจ ติดตาม SLA และกำกับการแก้ไข ทำให้ช่องโหว่สำคัญถูกมองข้าม การแก้ไขล่าช้าเกิน SLA ขาดหลักฐานการตรวจสอบย้อนหลัง และไม่สามารถรายงานภาพรวมความเสี่ยงต่อผู้บริหารได้

## Target Users
<!-- Phase 2 (requirements) จะขยายเป็น persona เต็ม — ตอนนี้ย่อจาก 16 user class ในเอกสาร -->
- **System Administrator**: จัดการ configuration, user/role/permission, master data, workflow/SLA/notification template, ตรวจ system health/integration
- **Cybersecurity Administrator**: กำหนดมาตรฐาน vulnerability management, risk model, severity mapping, SLA policy, scanner source, escalation matrix
- **Vulnerability Management Officer**: คัดกรอง/ยืนยัน finding, ตรวจ duplicate, มอบหมาย owner, ติดตาม SLA, escalation
- **Security Analyst**: วิเคราะห์เชิงเทคนิค (CVE/CWE/CVSS/Exploit), ยืนยันผล, verification, เสนอ reopen
- **SOC Analyst**: เชื่อมโยง threat intelligence/incident, ติดตาม critical/KEV, active exploitation
- **Asset Owner / System Owner / Application Owner**: รับผิดชอบความเสี่ยงของ asset/ระบบ, วางแผนแก้ไข, ยื่น business justification/risk acceptance
- **Remediation Owner / Infrastructure Team / Application Development Team**: ดำเนินการแก้ไข บันทึกแผน/ความคืบหน้า/หลักฐาน ส่ง verification
- **Risk Owner / Approver**: พิจารณาและอนุมัติ risk acceptance / false positive / SLA extension / exception / closure
- **Auditor / Compliance Officer**: ตรวจสอบการปฏิบัติตามนโยบาย, audit trail, SLA, approval (read-only)
- **Management / Executive**: ติดตามภาพรวมความเสี่ยงและประสิทธิภาพการแก้ไขผ่าน executive dashboard
- **External Security Assessor**: บันทึก/ส่งผล penetration test ตาม project ที่ได้รับมอบหมาย (บัญชีมีวันหมดอายุ)
- **External System / API Client**: ส่ง/รับข้อมูลผ่าน API (service account, scoped)

## Key Features
<!-- Phase 2 จะ map เป็น user story + EARS ต่อ FR -->
- **Asset Management**: inventory, synchronization (CMDB/ITAM mock), ownership mapping, criticality
- **Vulnerability Data Collection**: file import, scanner API integration (mock), manual finding registration, import template
- **Data Quality**: validation, normalization (common data model), duplicate detection & consolidation
- **Reference Data**: CVE/CWE mapping, CVSS mapping, KEV
- **Risk Assessment**: risk score (0–100), risk level classification, KEV/internet-facing prioritization, manual adjustment, recalculation
- **Workflow & Status**: 13 สถานะ (New → Under Review → Confirmed → Assigned → In Progress → Pending Verification → Resolved → Closed → Reopened → False Positive → Risk Accepted → Exception Approved → Archived) + transition control
- **Assignment & Remediation**: auto/manual/bulk assignment, remediation task/plan/progress, collaboration (comment/mention), evidence
- **SLA & Escalation**: SLA calculation/timer/recalculation, reminders, overdue escalation, critical notification
- **Verification & Closure**: submit for verification, rescan, verification result, closure, reopen
- **Governance**: risk acceptance, false positive, SLA extension, security exception, compensating control (with approval matrix + expiry)
- **Dashboard & Reporting**: executive/operations dashboards, advanced search/filter, operational/executive reports, data export
- **Access Control & Audit**: RBAC, organizational data segregation, audit trail, data archiving/retention
- **Administration**: master data, workflow config, SLA policy config, integration monitoring
- **API**: REST `/api/v1` external API for assets/findings/status/tasks/integration

## Core Slice Scope (confirmed in requirements D1)
**In scope (ทำครบจริง)**: Access Control & Auth (local/mock), Asset Management, Vulnerability Intake (File Import + Manual), Data Quality (validate/normalize/dedup), Reference Data (CVE/CWE/CVSS/KEV — seed), Risk Assessment (weighted configurable), Workflow & Status (13 states), Assignment & Remediation + Evidence, SLA & Escalation, Verification/Closure/Reopen, Operations+Executive Dashboard + Search/Filter, Administration (master/workflow/SLA config), Audit Trail (immutable). → 32 user stories (US-001..032).
**Out of scope (TBD/phase ถัดไป)**: Scanner API จริง, SIEM/ITSM/Email/Teams integration จริง, Risk Acceptance/False Positive/SLA Extension/Exception เต็มรูปแบบ + Approval Matrix, Compensating Control เต็มรูปแบบ, Report/Export ละเอียด, Archiving/Retention automation, DR/Backup, auto reference-data sync, MFA.

## Domain Language
| Term | Definition | Example |
|------|------------|---------|
| Vulnerability | จุดอ่อน/ข้อบกพร่องที่อาจกระทบ CIA ของข้อมูล/ระบบ | SQL Injection ใน endpoint |
| Vulnerability Finding | ผลตรวจพบจาก scanner/assessment/manual | Finding จาก SAST |
| Vulnerability Instance | ช่องโหว่ที่ผูกกับ asset/URL/IP/Port/Component เฉพาะรายการ | CVE-x บน server A:443 |
| Asset | ทรัพย์สินสารสนเทศภายใต้การบริหารช่องโหว่ | Application, Server, DB, Cloud Resource |
| Asset Criticality | ระดับความสำคัญต่อธุรกิจ | Critical/High/Medium/Low |
| Risk Score | คะแนนความเสี่ยง 0–100 คำนวณจากปัจจัยเทคนิค+ธุรกิจ | 87 |
| Risk Level | ระดับความเสี่ยง | Critical/High/Medium/Low/Informational |
| CVE / CWE / CVSS / KEV | รหัส/มาตรฐานอ้างอิงด้านช่องโหว่ | CVE-2024-xxxx, CWE-89, CVSS 9.8, KEV |
| SLA | กรอบเวลา acknowledge/start/resolve | Critical = 7 วัน |
| Remediation / Mitigation | การแก้ต้นเหตุ / ลดโอกาส-ผลกระทบ | Patch / Compensating control |
| Risk Acceptance | การยอมรับความเสี่ยงอย่างเป็นทางการ มีวันหมดอายุ | อนุมัติโดย Risk Owner |
| False Positive | รายการที่ยืนยันว่าไม่ใช่ช่องโหว่จริง | ปิดด้วยหลักฐาน |
| Approval Matrix | กฎผู้อนุมัติตามความเสี่ยง/ประเภท/asset/หน่วยงาน | Critical → Risk Owner + Executive |
| Audit Log | บันทึกกิจกรรมสำคัญ (view/edit/approve/export/config) | immutable |

## Success Criteria
- Working software: รัน dev server + เปิดใช้งานบน browser ได้จริง ครบทุก function ใน core slice ตาม requirements
- Core end-to-end flow ทำงานได้: import/intake finding → dedup → risk scoring → workflow/assignment → SLA → dashboard
- RBAC + Audit log ทำงานจริงบนทุก action สำคัญ
- (รายละเอียด metric เชิงตัวเลข เช่น MTTA/MTTR target, SLA compliance %) — To be defined during requirements phase

## Constraints & Assumptions
- **Constraints**:
  - Tech stack ยึด dipd-mcp approved baseline: Angular 22 + Tailwind 4 + daisyUI 5 / ASP.NET Core .NET 10 + EF Core 10; Database = **SQL Server** (project decision)
  - Secure by Design + OWASP Top 10, PDPA, ห้าม hardcode secret, RBAC least privilege, encryption at rest/in transit
  - UI follows the current [SVM design system](../../.kiro/steering/design-system.md); earlier Ferrari guidance is historical.
  - ระบบภายนอกทั้งหมด (Scanner, CMDB, ITSM, SIEM, Email, Teams, SSO) ใช้ **mock adapter + seed data** (ไม่ต่อระบบจริง); ออกแบบหลัง interface เพื่อสลับเป็น API จริงภายหลังได้
  - ไม่ใช้ข้อมูลส่วนบุคคลจริงใน dev/test/demo
- **Assumptions**:
  - องค์กรมี policy/process vulnerability management, risk scoring model, SLA, approval matrix ที่กำหนดได้
  - Asset มี unique identifier สำหรับ mapping; มี asset/system owner ระบุได้
  - การปิด finding ต้องมีหลักฐาน/verification ตามระดับความเสี่ยง; Critical ต้องผ่าน independent verification
  - Risk Acceptance / False Positive / Exception ต้องมีวันหมดอายุ/วันทบทวน
  - Requirement ที่ยังไม่ยืนยันถูกระบุเป็น Assumption/TBD และต้องอนุมัติก่อนพัฒนา

## Project Type
- **Type**: Greenfield
- **Scope**: New product (standalone enterprise web application)
