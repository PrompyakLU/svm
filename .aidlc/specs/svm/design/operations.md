# Operations Design

## Summary
- **Observability Level**: Minimal (D3-11) — structured logging + health endpoint
- **Error Tracking**: Log-based only (D3-12)
- **Lifecycle Management**: Basic health endpoint only (D3-13)
- **Logging Format**: Structured JSON (production), human-readable (dev)
- **Logging Library**: `Microsoft.Extensions.Logging` + OpenTelemetry logging (baseline); structured JSON console exporter

> ระดับ Minimal → รวมเฉพาะ Logging + Health + Graceful Shutdown (เบา) + Configuration ตาม template rule

---

## Logging

### Strategy
- **Format**: structured JSON (prod), readable (dev)
- **Levels**: Error, Warning, Information, Debug
- **Correlation**: request-id (`X-Request-Id` — generate ถ้าไม่มี, forward ถ้ามี; behind ALB อ่าน `X-Forwarded-*`)
- **Sampling**: none สำหรับ Error/Warning; Info/Debug ปรับได้ผ่าน config

### Per-Component Logging
| Component | Key Log Events | Level | Context Fields |
|---|---|---|---|
| AuthModule (C1) | login success/fail, token reject | Info/Warn | requestId, userId, result |
| AssetModule (C2) | asset create/update, CMDB sync result | Info | requestId, userId, assetCode, counts |
| IntakeModule (C3) | import start/finish, batch result | Info | requestId, batchId, success/failedCount |
| DataQualityService (C4) | dedup match, validation fail | Info/Warn | requestId, instanceId, dedupKey |
| ReferenceDataService (C5) | CVE/CVSS not found/unsupported | Warn | requestId, cveId, cvssVersion |
| RiskScoringEngine (C6) | calc done, out-of-range, weight error | Info/Error | requestId, instanceId, score, breakdown |
| WorkflowModule (C7) | transition applied/rejected, concurrency | Info/Warn | requestId, instanceId, from/toStatus |
| Assignment/Remediation (C8) | assign, bulk result, evidence malware | Info/Warn | requestId, instanceId, assignee |
| SlaModule (C9) | sla calc, near-due, breach, escalation | Info/Warn | requestId, instanceId, breachStatus |
| VerificationModule (C10) | submit, rescan, result, independent-check fail | Info/Warn | requestId, instanceId, result |
| Dashboard/Reporting (C11) | slow query | Warn | requestId, userId, durationMs |
| Administration (C12) | config change | Info | requestId, userId, configType |
| AuditModule (C13) | audit write fail, integrity fail | Error | requestId, objectType, objectId |
| NotificationModule (C14) | delivery fail | Warn | requestId, recipientId, channel |
| Integration mock (C15) | adapter call fail/timeout/retry | Warn | requestId, service, attempt |

### Sensitive Data Rules
- **Never log**: password, passwordHash, JWT signing key, JWT token, API key/token ของ integration
- **Mask**: email (first 3 + domain), personal data (PDPA, BR-32), credential/secret ใน finding/evidence
- **Always include**: requestId, timestamp, duration, userId (ถ้า authed), operation

---

## Health & Lifecycle (Basic)
### Endpoint
| Endpoint | Purpose | Auth | Response |
|---|---|---|---|
| `GET /health` | liveness | internal | `200 {"status":"ok","version":"1.0.0"}` หรือ `503` |

- Basic liveness เท่านั้น (D3-13). ไม่มี separate readiness/metrics ใน MVP (เพิ่มภายหลังเมื่อ containerize/deploy)
- `GET /health` path ไม่ชนกับ `/api/v1/*`

### Graceful Shutdown (เบา)
- ASP.NET Core host จัดการ SIGTERM → หยุดรับ request ใหม่ → รอ in-flight เสร็จ (default host timeout) → ปิด EF Core DbContext pool → flush logs → exit. ไม่ตั้ง drain delay พิเศษใน MVP.

---

## Error Handling & Reporting (Log-based)
### Error Classification
| Category | HTTP | Log Level | Example |
|---|---|---|---|
| Client error | 4xx | Warning | validation (400), unauthorized (401/403), not found (404) |
| Business rule | 422 | Warning | invalid transition, missing evidence, independent-verification required, invalid risk weight |
| Conflict | 409 | Warning | concurrent update (rowVersion) |
| Operational | 5xx (retriable) | Error | DB timeout, mock adapter fail |
| Programming | 5xx (bug) | Error | unhandled exception |
| Fatal | — | Critical | DB unreachable on startup, missing required config |

- ทุก error → ProblemDetails (NFR-40: ไม่เปิด stack trace/secret ใน prod). Error ลง structured log (log-based tracking); query ผ่าน log platform.
- Audit write failure สำหรับ consequential action → block หรือ mark+alert (EX-98).

---

## Configuration Management
### Required Environment Variables
| Variable | Required | Default | Sensitive | Description |
|---|---|---|---|---|
| `ConnectionStrings__Svm` | Yes | — | Yes | SQL Server connection |
| `Auth__Jwt__SigningKey` | Yes | — | Yes | JWT signing key (local auth) |
| `Auth__Jwt__Issuer` | No | `svm-local` | No | JWT issuer |
| `Auth__Mode` | No | `Local` | No | `Local`/`Oidc` |
| `Integration__UseMock` | No | `true` | No | ใช้ mock adapter |
| `ASPNETCORE_ENVIRONMENT` | No | `Development` | No | runtime env |
| `Logging__LogLevel__Default` | No | `Information` | No | log level |

### Startup Validation
1. ตรวจ required env/config ครบ (ConnectionString, SigningKey) → exit ด้วยข้อความชัดถ้าขาด
2. ตรวจรูปแบบ (connection string valid, signing key length พอ)
3. ทดสอบเชื่อม DB; ถ้าไม่ได้ → exit 1 พร้อม diagnostic
4. seed master/reference/users (dev) ถ้ายังว่าง

---

## Traceability
| Operations Concern | Related Artifacts |
|---|---|
| Per-component logging | design/components.md (C1–C15) |
| Config variables | design/implementation.md (env setup) |
| Error classification | design/api-spec.md (ProblemDetails) |
| Health path | design/api-spec.md (no `/api/v1` conflict) |
| Sensitive-data rules | requirements NFR-16, BR-32 (PDPA) |
