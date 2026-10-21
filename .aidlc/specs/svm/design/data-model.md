# Data Model

## Overview
- **Database**: SQL Server (EF Core 10 SqlServer provider)
- **ORM/Client**: EF Core 10 — explicit `IEntityTypeConfiguration<T>` per entity, `ToTable(...)` ตาม IRPC naming (`master_`/`config_`/`trans_` + lowerCamelCase), columns lowerCamelCase, Thai text = `nvarchar`
- **Keys**: `uniqueidentifier` (GUID) PK ทุก entity; audit columns (`createdAt`, `createdBy`, `updatedAt`, `updatedBy`) ทุก mutable entity
- **Enums**: เก็บเป็น `nvarchar` (string-backed) หรือ FK ไป config table (configurable master data)

## Entities

### User & Access (RBAC)
#### config_user (User)
- **Purpose**: บัญชีผู้ใช้ (local/mock auth, OIDC-ready)
- **Fields**: `id` GUID PK, `username` nvarchar(100) unique required, `displayName` nvarchar(200), `email` nvarchar(256), `passwordHash` nvarchar(max) (local auth; null เมื่อ OIDC), `isActive` bit, `expiresAt` datetime2 null (external user), `createdAt/By`, `updatedAt/By`
- **Relationships**: M:N → config_role (ผ่าน config_userRole); 1:N → config_userScope
- **Indexes**: unique(`username`), (`isActive`)
- **Business rules**: BR-43 (inactive → reassign open work), BR-44 (external account expiry)

#### config_role (Role)
- **Purpose**: บทบาท RBAC (16 user class)
- **Fields**: `id` GUID PK, `code` nvarchar(50) unique (เช่น `VULN_MGMT_OFFICER`), `name` nvarchar(200), `description` nvarchar(max), `isSystem` bit
- **Relationships**: M:N → config_permission (config_rolePermission); M:N → config_user

#### config_permission (Permission)
- **Purpose**: สิทธิ์ย่อย (least privilege) — action + resource
- **Fields**: `id` GUID PK, `code` nvarchar(100) unique (เช่น `finding.assign`, `riskacceptance.approve`), `description` nvarchar(max)

#### config_userRole / config_rolePermission
- **Purpose**: join tables (M:N)
- **Fields**: composite FK + `createdAt/By`

#### config_userScope (Data Segregation)
- **Purpose**: ขอบเขตข้อมูลของผู้ใช้ (FR-63, BR-30)
- **Fields**: `id` GUID PK, `userId` FK, `scopeType` nvarchar(30) (`BusinessUnit`|`Asset`|`Responsibility`|`Organization`), `scopeValue` nvarchar(200)
- **Indexes**: (`userId`)

### Authentication sessions

`trans_authSession` supports C1 server-side revocation and idle/absolute expiry. See the [local authentication contract](../../../../docs/authentication-server.md) for fields, rotation/concurrency and validation rules. It belongs to the existing SQL Server model; test-only database replacements do not change the production database decision.

### Asset
#### master_asset (Asset)
- **Purpose**: ทรัพย์สินภายใต้การบริหารช่องโหว่ (FR-01)
- **Fields**: `id` GUID PK, `assetCode` nvarchar(100) unique required, `name` nvarchar(300), `assetType` nvarchar(50) (Application/Server/Database/NetworkDevice/CloudResource/Container), `ipAddress` nvarchar(45), `hostname` nvarchar(300), `url` nvarchar(500), `environment` nvarchar(50) (Prod/Staging/Dev), `businessUnit` nvarchar(200), `criticality` nvarchar(20) (Critical/High/Medium/Low), `internetExposure` bit, `status` nvarchar(30) (Active/Decommissioned), `cloudResourceId`/`repositoryId` nvarchar(300) null, audit cols
- **Relationships**: 1:N → trans_vulnerabilityInstance; 1:N → master_assetOwnership
- **Indexes**: unique(`assetCode`), (`businessUnit`), (`criticality`), (`internetExposure`), (`status`)
- **Business rules**: BR-05 (production ต้องมี criticality), BR-38 (decommissioned ไม่ลบอัตโนมัติ), EX-28 (missing criticality)

#### master_assetOwnership (Asset Ownership)
- **Purpose**: เชื่อม asset ↔ owner หลายบทบาท (FR-03)
- **Fields**: `id` GUID PK, `assetId` FK, `ownerUserId` FK null, `ownerType` nvarchar(30) (AssetOwner/SystemOwner/ApplicationOwner/RemediationTeam), `teamName` nvarchar(200) null
- **Indexes**: (`assetId`,`ownerType`)
- **Business rules**: BR-06 (ต้องมี owner ก่อน Assigned), EX-25 (owner not found)

### Reference Data (seed)
#### master_cve / master_cwe
- **Fields (cve)**: `id` GUID PK, `cveId` nvarchar(30) unique (CVE-YYYY-NNNN), `description` nvarchar(max), `publishedDate` datetime2, `isKev` bit, `kevDateAdded` datetime2 null
- **Fields (cwe)**: `id` GUID PK, `cweId` nvarchar(20) unique (CWE-NNN), `name` nvarchar(300), `description` nvarchar(max)
- **Business rules**: EX-47 (CVE not found → mark, keep value)

#### config_cvssVersion
- **Purpose**: CVSS version ที่รองรับ (FR-14, EX-48)
- **Fields**: `id` GUID PK, `version` nvarchar(10) unique (3.1/4.0), `isSupported` bit

### Vulnerability
#### trans_vulnerabilityFinding (Vulnerability Finding)
- **Purpose**: ผลตรวจพบดิบจากแต่ละแหล่ง/รอบ (FR-05/07, BR-35)
- **Fields**: `id` GUID PK, `instanceId` FK null (หลัง dedup), `sourceSystem` nvarchar(100), `sourceRecordId` nvarchar(200), `importBatchId` FK null, `findingName` nvarchar(500), `rawSeverity` nvarchar(20), `cveId`/`cweId` nvarchar null, `cvssVersion` nvarchar(10) null, `cvssBaseScore` decimal(4,1) null, `cvssVector` nvarchar(200) null, `location` nvarchar(500), `port` int null, `component` nvarchar(300) null, `detectedAt` datetime2, `isDuplicate` bit, `validationStatus` nvarchar(20) (Valid/Invalid/NeedsReview), audit cols
- **Indexes**: (`instanceId`), (`sourceSystem`,`sourceRecordId`), (`cveId`), (`importBatchId`)
- **Business rules**: BR-35 (source traceability), FR-09 (validation), EX-15/16

#### trans_vulnerabilityInstance (Vulnerability Instance)
- **Purpose**: ช่องโหว่ที่ผูกกับ asset+location เฉพาะ (dedup target) — เป็น entity หลักของ workflow
- **Fields**: `id` GUID PK, `assetId` FK, `title` nvarchar(500), `cveId`/`cweId` null, `location` nvarchar(500), `port` int null, `component` nvarchar(300) null, `firstDetectedAt` datetime2, `lastDetectedAt` datetime2, `status` nvarchar(30) (13 states), `riskScore` decimal(5,2) null, `riskLevel` nvarchar(20) null, `isKev` bit, `assignedToUserId` FK null, `createdAt/By`, `updatedAt/By`, `rowVersion` rowversion (optimistic concurrency — EX-57)
- **Relationships**: 1:N → trans_vulnerabilityFinding, trans_remediationTask, trans_riskScoreHistory, trans_evidence, trans_verificationRecord, config_slaTimer, trans_comment, trans_statusHistory
- **Indexes**: (`assetId`), (`status`), (`riskLevel`), (`assignedToUserId`), (`isKev`), unique(`assetId`,`cveId`,`location`,`port`,`component`) filtered — dedup key
- **Business rules**: BR-12 (dedup), BR-13 (recurrence/reopen), FR-22 (status), EX-57 (concurrent update via `rowVersion`)

#### config_importBatch (Import Batch)
- **Purpose**: ติดตามการนำเข้าไฟล์ (FR-05, idempotency)
- **Fields**: `id` GUID PK, `fileName` nvarchar(300), `transactionId` nvarchar(200) unique null, `totalRows` int, `successCount` int, `failedCount` int, `status` nvarchar(20) (Success/Partial/Failed/Processing), `importedBy` FK, `importedAt` datetime2, `errorDetail` nvarchar(max) null
- **Business rules**: BR-36 (idempotent — unique `transactionId`), EX-17 (duplicate batch)

### Risk
#### config_riskWeight (Risk Weight — configurable)
- **Purpose**: น้ำหนักปัจจัย risk scoring (FR-16, NFR-31, D3-3)
- **Fields**: `id` GUID PK, `factor` nvarchar(50) unique (Cvss/AssetCriticality/BusinessImpact/InternetExposure/ExploitAvailability/Kev/ThreatIntel/VulnAge/ExistingControl), `weight` decimal(5,4), `isActive` bit, `updatedAt/By`
- **Business rules**: EX-54 (invalid weight — ผลรวม/ติดลบ), BR-40 (change control)

#### config_riskThreshold (Risk Level Threshold)
- **Fields**: `id` GUID PK, `riskLevel` nvarchar(20), `minScore` decimal(5,2), `maxScore` decimal(5,2)
- **Business rules**: BR-01 (classification)

#### trans_riskScoreHistory (Risk Score History)
- **Purpose**: ประวัติการคำนวณ/ปรับ risk (FR-20/21)
- **Fields**: `id` GUID PK, `instanceId` FK, `score` decimal(5,2), `riskLevel` nvarchar(20), `calculationType` nvarchar(20) (Auto/Manual/Recalc), `factorBreakdown` nvarchar(max) (JSON), `reason` nvarchar(max) null, `previousScore` decimal(5,2) null, `createdAt/By`
- **Indexes**: (`instanceId`,`createdAt`)

### Workflow
#### config_statusTransition (State Machine — table-driven, D3-4)
- **Purpose**: นิยาม transition ที่อนุญาต + guard (FR-23, BR-27, NFR-31)
- **Fields**: `id` GUID PK, `fromStatus` nvarchar(30), `toStatus` nvarchar(30), `requiredPermission` nvarchar(100) null, `requiresEvidence` bit, `requiresApproval` bit, `isActive` bit
- **Indexes**: unique(`fromStatus`,`toStatus`)
- **Business rules**: BR-27, EX-55 (invalid transition), EX-56 (missing evidence)

#### trans_statusHistory (Status History)
- **Fields**: `id` GUID PK, `instanceId` FK, `fromStatus`/`toStatus` nvarchar(30), `reason` nvarchar(max) null, `changedBy` FK, `changedAt` datetime2
- **Indexes**: (`instanceId`,`changedAt`)

### Assignment & Remediation
#### config_assignmentRule (Assignment Rule)
- **Fields**: `id` GUID PK, `matchType` nvarchar(30) (BusinessUnit/AssetType/Technology), `matchValue` nvarchar(200), `assignToUserId`/`assignToTeam` null, `priority` int, `isActive` bit
- **Business rules**: BR-07, EX-59 (rule failure)

#### trans_remediationTask (Remediation Task)
- **Purpose**: งานแก้ไข (FR-27/28/29)
- **Fields**: `id` GUID PK, `instanceId` FK, `ownerUserId` FK null, `ownerTeam` nvarchar(200) null, `dueDate` datetime2, `riskLevel` nvarchar(20), `recommendation` nvarchar(max), `rootCause` nvarchar(max) null, `actionPlan` nvarchar(max) null, `targetDate` datetime2 null, `changeReference` nvarchar(200) null, `progressPercent` int, `status` nvarchar(30), `obstacle` nvarchar(max) null, audit cols
- **Indexes**: (`instanceId`), (`ownerUserId`), (`status`), (`dueDate`)
- **Business rules**: EX-63 (target date > SLA)

#### trans_evidence (Evidence)
- **Purpose**: หลักฐาน (FR-31, BR-42, NFR-18)
- **Fields**: `id` GUID PK, `instanceId` FK, `taskId` FK null, `verificationId` FK null, `fileName` nvarchar(300), `storagePath` nvarchar(500), `contentType` nvarchar(100), `fileSize` bigint, `scanStatus` nvarchar(20) (Clean/Infected/Unscannable), `version` int, `uploadedBy` FK, `uploadedAt` datetime2
- **Business rules**: BR-42 (no replace without version history), NFR-18 (malware scan), EX-21/22

#### trans_comment (Comment)
- **Fields**: `id` GUID PK, `instanceId` FK, `body` nvarchar(max), `mentions` nvarchar(max) null (JSON userIds), `createdBy` FK, `createdAt` datetime2

### SLA
#### config_slaPolicy (SLA Policy)
- **Purpose**: นิยาม SLA ตาม risk level/criticality/exposure/KEV (FR-66, BR-08)
- **Fields**: `id` GUID PK, `riskLevel` nvarchar(20), `assetCriticality` nvarchar(20) null, `internetExposure` bit null, `kevOnly` bit, `acknowledgeHours` int, `startHours` int, `resolveHours` int, `isActive` bit
- **Business rules**: BR-08, EX-66 (no applicable policy)

#### config_businessCalendar (Business Calendar)
- **Fields**: `id` GUID PK, `date` date unique, `isWorkingDay` bit, `description` nvarchar(200) null
- **Business rules**: EX-67 (invalid calendar)

#### trans_slaTimer (SLA Timer)
- **Fields**: `id` GUID PK, `instanceId` FK, `acknowledgeDue` datetime2, `remediationStartDue` datetime2, `resolutionDue` datetime2, `startedAt` datetime2, `breachStatus` nvarchar(20) (OnTrack/NearDue/Breached), `policyId` FK, `previousDue` nvarchar(max) null (JSON — recalc history)
- **Indexes**: (`instanceId`), (`resolutionDue`), (`breachStatus`)
- **Business rules**: BR-09/10, FR-37, EX-68

### Verification
#### trans_verificationRecord (Verification Record)
- **Fields**: `id` GUID PK, `instanceId` FK, `result` nvarchar(20) (Verified/NotResolved/PartiallyResolved), `method` nvarchar(20) (Rescan/Manual), `isIndependent` bit, `verifiedBy` FK, `verifiedAt` datetime2, `notes` nvarchar(max) null
- **Business rules**: BR-15/16 (mandatory + independent for Critical), EX-80

### Notification
#### trans_notification (Notification)
- **Fields**: `id` GUID PK, `recipientUserId` FK, `channel` nvarchar(20) (InApp/Email/Teams), `type` nvarchar(50), `subject` nvarchar(300), `body` nvarchar(max), `relatedInstanceId` FK null, `deliveryStatus` nvarchar(20) (Pending/Sent/Failed/Read), `createdAt` datetime2
- **Indexes**: (`recipientUserId`,`deliveryStatus`), (`createdAt`)
- **Business rules**: FR-42 (history), EX-71 (storm — dedupe), EX-73 (invalid recipient)

### Audit
#### trans_auditLog (Audit Log — immutable, append-only)
- **Purpose**: บันทึกกิจกรรมควบคุม (FR-68, NFR-20/21, D3-8)
- **Fields**: `id` bigint IDENTITY PK (sequential), `userId` FK null, `action` nvarchar(50), `objectType` nvarchar(100), `objectId` nvarchar(100), `beforeValue` nvarchar(max) null, `afterValue` nvarchar(max) null, `result` nvarchar(20), `source` nvarchar(100), `timestamp` datetime2, `prevHash` nvarchar(64) null, `rowHash` nvarchar(64) (SHA-256 ของ record + prevHash)
- **Indexes**: (`timestamp`), (`userId`), (`objectType`,`objectId`), (`action`)
- **Business rules**: BR-33, NFR-21 (no update/delete — append-only + restricted DB perm + hash chain), EX-98/100 (write failure/integrity)

## ER Diagram (ASCII, cardinality)
```
config_user ──M:N── config_role ──M:N── config_permission
     │ 1:N
     └── config_userScope

master_asset 1 ──── N master_assetOwnership N ──── 1 config_user
     │ 1
     │ N
trans_vulnerabilityInstance ──N:1── master_asset
     │ 1          │ 1          │ 1          │ 1         │ 1
     │ N          │ N          │ N          │ N         │ N
     ├ trans_vulnerabilityFinding          ├ trans_remediationTask ─1:N─ trans_evidence
     ├ trans_riskScoreHistory              ├ trans_verificationRecord
     ├ trans_statusHistory                 ├ trans_slaTimer ──N:1── config_slaPolicy
     ├ trans_comment                       └ trans_notification (relatedInstance)
     │
master_cve (isKev) ─ referenced by ─ trans_vulnerabilityInstance/Finding
config_riskWeight / config_riskThreshold ─ used by ─ RiskScoringEngine
config_statusTransition ─ guards ─ status changes (trans_statusHistory)
config_assignmentRule ─ drives ─ assignment
config_importBatch 1 ──── N trans_vulnerabilityFinding
trans_auditLog (append-only, hash-chained) ─ records all consequential actions
```

## Access Patterns
| Query | Frequency | Index used |
|---|---|---|
| List instances by data scope + filter (status/riskLevel/BU/CVE/age/date) | สูงมาก | (`assetId`),(`status`),(`riskLevel`),(`isKev`) + join asset(`businessUnit`) |
| Dedup lookup (asset+cve+location+port+component) | สูง (ทุก intake) | unique filtered dedup key |
| SLA breach/near-due scan | สูง (scheduled) | (`resolutionDue`),(`breachStatus`) |
| Operations dashboard (unassigned/overdue/pending verification) | สูง | (`status`),(`assignedToUserId`) |
| Executive dashboard (risk distribution/trend) | ปานกลาง | (`riskLevel`),(`isKev`) + aggregate |
| Audit search by object/user/time | ปานกลาง | (`objectType`,`objectId`),(`userId`),(`timestamp`) |
| Finding history per instance | ปานกลาง | (`instanceId`,`createdAt`) |

## Rules & Constraints (ไม่แสดงใน schema โดยตรง)
- `riskScore` ต้องอยู่ 0–100 (ตรวจใน engine + CHECK constraint) — EX-51
- Dedup key filtered unique เฉพาะ instance ที่ยัง active (ไม่รวม Archived)
- `trans_auditLog` ไม่มี FK cascade delete, ไม่มี update path; DB login ของ app = INSERT/SELECT เท่านั้นบน table นี้
- Optimistic concurrency ด้วย `rowVersion` บน instance (EX-57)
- Soft handling: Decommissioned asset → instance เข้าสู่ archive flow ไม่ลบ (BR-38/45)
