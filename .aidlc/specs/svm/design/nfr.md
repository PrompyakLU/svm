# Non-Functional Requirements & Infrastructure

> ครอบคลุม NFR ที่ผูกกับ core slice (D1-8 = NFR analysis on). ค่าตัวเลขเป้าหมายบางส่วนเป็น baseline สำหรับ workshop (ปรับได้ภายหลังตาม policy องค์กร).

## Performance (NFR-02/03/04)
- **Response Time**: หน้าจอทั่วไป < 2s, dashboard < 3s, standard API < 1s (p95, ไม่รวม external) — baseline workshop
- **Throughput**: ระดับ workshop (ปริมาณปานกลาง); list query ใช้ offset pagination + index
- **Concurrent Users**: baseline (ไม่กำหนดตัวเลข enterprise ใน workshop)

## Scalability (NFR-08)
- **Horizontal Scaling**: API เป็น stateless (JWT) → scale out ได้ (12-factor); background job (SlaMonitor) ต้องกัน double-run เมื่อ multi-instance (future: leader election/lock)
- **Data Growth**: vulnerability instance/finding โตต่อเนื่อง → index + pagination รองรับ

## Security (NFR-09..19, security-policies, OWASP Top 10)
- **Authentication**: local JWT (OIDC-ready ผ่าน `IAuthProvider`); MFA = out of core scope
- **Session inactivity (NFR-17)**: the mock UI slice uses a documented workshop baseline of 30 minutes without meaningful activity. Check expiry before renewing activity and when returning to a backgrounded tab. Persist no password or real bearer token in the demo session. Backend enforcement/revocation and organization-specific timeout policy remain C1 work; client timers are UX behavior, not a security boundary.
- **Authorization**: RBAC + policy-based + data-scope segregation; least privilege; Segregation of Duties (ผู้ยื่น ≠ ผู้อนุมัติ) (NFR-11/12/13, BR-28)
- **Encryption**: in transit = TLS (HTTPS ทุก endpoint, NFR-14); at rest = SQL Server TDE / column protection สำหรับข้อมูลสำคัญ (NFR-15)
- **Input validation**: ทุก endpoint (ป้องกัน injection/XSS/CSRF); parameterized query ผ่าน EF Core
- **Secret management**: ไม่เก็บใน source/config plaintext; user-secrets (dev) / secret store (NFR-16)
- **File upload**: ตรวจชนิด/ขนาด/นามสกุล + malware scan (mock) ก่อนรับ (NFR-18, EX-21/22)
- **API security**: authn/authz/validation/rate-limit/replay protection ตามเหมาะสม (NFR-19)

**Roles & Permissions (ย่อ — เต็มใน personas.md + RBAC seed)**:
| Role | Permissions (ตัวอย่าง) |
|---|---|
| System Administrator | admin.*, (ไม่อนุมัติ business, ไม่แก้ audit) |
| Vulnerability Mgmt Officer | finding.*, instance.assign/transition, task.*, (ไม่อนุมัติคำขอตนเอง) |
| Security Analyst | instance.risk.adjust, verification.*, reference.read |
| Asset/System Owner | asset.read (scope), task.update, (ยื่นคำขอ ไม่อนุมัติตนเอง) |
| Remediation Owner | task.update, evidence.upload, verification.submit |
| Risk Owner/Approver | *.approve (ตาม matrix, ไม่ conflict of interest) |
| Auditor | audit.read, *.read (read-only) |
| Management | dashboard.exec, *.read (scoped/masked) |

## Availability & Reliability (NFR-01/23/24)
- **Uptime**: baseline workshop (ไม่ตั้ง SLA enterprise); DR/backup = out of core scope
- **Transaction integrity**: import/assign/approve/status-change ใช้ transaction + idempotency → ไม่สร้างข้อมูลซ้ำ/ไม่ครบ (NFR-23, EX-103)
- **Integration reliability**: timeout/retry/idempotency/error-queue บน mock adapter (NFR-24)
- **Health check**: `GET /health` (Basic)

## Infrastructure
### Compute
| Component | Service | Config |
|---|---|---|
| Web (Angular) | OCI container (static serve) / `ng serve` (dev) | — |
| API (.NET 10) | OCI container / `dotnet run` (dev) | stateless |
### Storage
| Data Type | Service | Config |
|---|---|---|
| Relational data | SQL Server | EF Core, nvarchar สำหรับ Thai; TDE at rest |
| Evidence files | File system / object storage (mock IFileStorage) | malware-scanned, versioned |
### Networking
| Component | Service | Config |
|---|---|---|
| web→api | same-origin REST `/api/v1` | HTTPS |

## Caching (NFR — optional)
| Data | Technology | TTL | Invalidation |
|---|---|---|---|
| Reference data (CVE/CWE/CVSS/KEV) | in-memory cache | ยาว (seed) | on admin update |
| Risk weights / SLA policies / transitions | in-memory cache | ยาว | on config change (BR-40) |

## Data Management (NFR-22/35, PDPA)
- **Migration Tool**: EF Core Migrations
- **Retention/Archival**: automation = out of core scope; โครงสร้างรองรับ (status Archived, no hard delete BR-45)
- **Deletion**: soft (status-based); audit log append-only
- **Data classification/masking**: credential/token/personal data mask ตาม role (BR-32)

## Observability (NFR-25/26) → Minimal
| Concern | Tool | Config |
|---|---|---|
| Logging | Microsoft.Extensions.Logging + OTel (structured JSON) | retention ตาม platform |
| Metrics/Tracing/Alerting | — (Minimal; เพิ่มภายหลัง) | — |

## CI/CD & Deployment (NFR — optional/last)
- **Deploy strategy**: container images (web + api); CI/CD = deploy phase (optional)
- Environments: dev (workshop) → staging/prod (future)
