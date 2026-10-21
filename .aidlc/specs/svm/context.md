# Context Assessment

## Summary
<!-- 10-line max. Downstream phases read ONLY this section. -->
- **Type**: Greenfield
- **Scope**: new
- **Stack**: Angular 22 (TS strict) + Tailwind 4 + daisyUI 5 / ASP.NET Core .NET 10 Web API + EF Core 10 / SQL Server
- **Architecture**: Layered modular monolith — separate Web (Angular) + API (ASP.NET Core) OCI images, REST `/api/v1`
- **Feature**: Security Vulnerability Management (SVM) — แพลตฟอร์มกลางรวบรวม ประเมินความเสี่ยง และบริหารวงจรช่องโหว่ขององค์กร
- **Impact**: New standalone
- **Complexity**: High — ~70 FR / 40 NFR / 114 exception cases / 45 business rules / 16 user classes (workshop targets a core end-to-end slice first)
- **Recommendations**: Personas Yes, Units No (hands-on policy skips decomposition — comprehensive mode), NFR Yes

## Project Overview
- **Type**: Greenfield
- **Assessment Date**: 2026-10-08

## Technology Stack
- **Languages**: TypeScript (strict), C# (nullable)
- **Frameworks**: Angular 22.x (standalone APIs), ASP.NET Core Web API on .NET 10 LTS, EF Core 10.x
- **Build System**: Node.js 24 LTS + npm (web, committed `package-lock.json`); .NET SDK pinned via `global.json` + `Directory.Packages.props` (API, `packages.lock.json`)
- **Testing**: Angular CLI/Vitest + Playwright (critical browser flows); xUnit/.NET test (API)
- **Infrastructure**: SQL Server (EF Core SqlServer provider); OCI container images (web + API, same-origin routes)

## Patterns & Conventions
<!-- Baseline conventions inherited from dipd-mcp development-guideline (approved baseline). Full design-phase detail deferred to D3. -->
- **Architecture pattern**: Layered — Angular SPA → REST API controllers → application/domain services → EF Core repositories → SQL Server
- **API style**: REST/JSON over HTTPS, versioned `/api/v1`, OpenAPI, `ProblemDetails` for errors
- **Authorization**: Policy-based authorization (RBAC + organizational data-scope segregation)
- **Observability**: Structured logs + OpenTelemetry
- **Styling**: Tailwind CSS 4 + daisyUI 5 + Angular CDK; current UI direction is the [SVM design system](../../../.kiro/steering/design-system.md). Ferrari is historical input.

## Codebase Analysis
N/A — greenfield project.

## Feature Impact

**Affected Areas**: New standalone application (web + API + database, all created from scratch).

| Area | Impact | Reason |
|------|--------|--------|
| Angular web app | New | SVM UI: dashboards, vulnerability lists, workflow, admin |
| ASP.NET Core API | New | REST `/api/v1` for assets, findings, risk, workflow, SLA, approvals |
| SQL Server schema | New | Assets, findings, instances, risk, tasks, SLA, approvals, audit, reference data |
| Reference/seed data | New | CVE/CWE/CVSS/KEV reference, mock scanner/CMDB/ITSM adapters, RBAC seed |

## Recommendations

- Story Count: High (11+) — core slice spans Asset, Intake, Dedup, Risk Scoring, Workflow, Assignment, SLA, Dashboard, RBAC, Audit
- Domain Boundaries: Asset Management, Vulnerability Data (Intake/Dedup/Reference), Risk Assessment, Workflow & Remediation, SLA & Escalation, Governance (Risk Acceptance/False Positive/Exception), Dashboard & Reporting, Access Control & Audit, Administration/Config, Integration (mock adapters)
- User Types: 16 user classes (System Admin, Cybersecurity Admin, Vulnerability Mgmt Officer, Security Analyst, SOC Analyst, Asset Owner, System/Application Owner, Remediation Owner, Infrastructure Team, Application Dev Team, Risk Owner, Approver, Auditor/Compliance, Management/Executive, External Security Assessor, External System/API Client)
- Integration Points: Entra ID/AD (SSO — workshop uses mock/local auth), CMDB/ITAM, ITSM, SIEM, Email, Microsoft Teams, Scanners (SAST/DAST/SCA/Infra/Web/Cloud/Container) — all behind mock adapters per workshop policy
- **Personas**: Yes — 16 distinct user classes with different permissions and data scope; persona definitions drive RBAC and journeys
- **Units**: No — hands-on-project policy mandates comprehensive mode and skips decomposition (no splitting into units)
- **NFR**: Yes — 40 NFRs covering availability, performance, security, audit integrity, DR, accessibility; security NFRs are first-class

## Scope

- **Detected scope**: new
- **Rationale**: Empty workspace (no source code), greenfield build-from-scratch of a new product. User confirmed building the SVM application fresh.
- **Phases skipped**: decomposition (per hands-on-project policy — comprehensive mode, no unit split). Full remaining workflow otherwise: context → requirements → design → tasks → implement → build → deploy.

## Recommended Workflow

```
        ┌─────────────┐
        │   Context   │  ✅ (current)
        └──────┬──────┘
               ▼
        ┌─────────────┐
        │ Requirements│  user stories + EARS, personas, NFR
        └──────┬──────┘
               ▼
        ┌─────────────┐
        │   Design    │  D3 tech decisions, architecture, data model,
 │             │  BPMN/sequence, API, ER, UI (current SVM design system)
        └──────┬──────┘
               ▼
        ┌─────────────┐
        │    Tasks    │  sequenced tasks + execution waves
        └──────┬──────┘   (Setup → FE+mock JSON → FE+Backend/DB → ...)
               ▼
        ┌─────────────┐
        │  Implement  │  code generation per design
        └──────┬──────┘
               ▼
        ┌─────────────┐
        │ Build & Test│  compile, test suites, quality gates
        └──────┬──────┘
               ▼
        ┌─────────────┐
        │   Deploy    │  optional (last)
        └─────────────┘

(decomposition skipped — comprehensive mode)
```

## External References

| Source | Type | What was used |
|--------|------|---------------|
| `initial-requirements/project-requirements/(MSC) AIDLC Workshop - Usecase - Nattapong.md` | Requirements | FR/NFR/EX/BR, user classes, scope, assumptions |
| `.kiro/steering/design-system.md` | Current design authority | Approved light theme, app shell, dashboard, typography, accessibility |
| `initial-requirements/design-reference/DESIGN-ferrari.md` | Historical input | Superseded for current UI direction |
| dipd-mcp `development-guideline` | Tech standard | Approved baseline stack (Angular + .NET + EF Core), API/security/ops conventions |
