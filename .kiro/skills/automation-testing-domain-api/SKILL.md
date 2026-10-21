---
name: automation-testing-domain-api
description: "API test DOMAIN (capability, not a lifecycle stage) of the QA Automation Testing skill library. Owns API-layer testing over HTTP / GraphQL / gRPC: how API cases are detected (openapi/swagger, route files, *.proto, GraphQL schema), generated (contract + functional, positive/negative, status/schema/auth checks), executed (any HTTP client or the project's test runner — no browser, no Node required unless the client is Node-based), and evidenced (request/response captured into the case). The Analyze/Generate/Validate/Scale stages consult this domain when the API layer is active; it also runs standalone ('test this API'). Results render into the API sheet of the XLSX workbook and the DOCX report."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: test-domain
  test_types: [API]
  keywords: [api, rest, http, graphql, grpc, openapi, swagger, contract, endpoint]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Test Domain — API (HTTP / GraphQL / gRPC)

**Role:** the authority for *what and how* API testing works. The lifecycle stages
consult this domain when the API layer is active; it also runs **standalone**.
**Reply in the user's language.**

> Capability skill, **not a gated stage** — the consulting stage owns the gate.

## Environment Detection
`.kiro/` → Kiro · `.claude/` → Claude Code · `.cursor/` → Cursor · `.windsurf/` →
Windsurf. `SKILL_DIR={platform}/skills/automation-testing-domain-api`. Shared library:
sibling hub `../automation-testing/`.

## Detects when (→ `scope.py` API markers)
`openapi.json/yaml` · `swagger.json/yaml` · route files · `*.proto` ·
`schema.graphql` / GraphQL dir · an API base URL to probe.

## Test types owned (→ `report_model.TEST_TYPES`)
- **API** — endpoint contract + functional behavior (status codes, response schema,
  auth/authorization, validation, error handling, pagination).

## Information Contract
| | |
|---|---|
| **Reads** | `data/analysis.md` (API coverage), the API contract (openapi/proto/schema) or a live API base |
| **Writes** | API rows into `data/test_cases.json` (`test_type` = `API`) |
| **Depends on** | any HTTP/GraphQL/gRPC client or the project's own test runner. **No browser.** Node needed only if the chosen client is Node-based |
| **Owns** | no bundled templates (client-agnostic; uses the project's runner or a language-native request) |

## How to generate
- One positive + one negative case per endpoint/operation minimum: valid request →
  expected status + schema; invalid/unauthorized → expected error.
- Map each OpenAPI/GraphQL operation to case(s); fill `steps`, `test_data`
  (request body/params), `expected` (status + key fields), traceability.

## How to execute
- Drive the endpoint with the project's client or a language-native HTTP request
  (curl, requests, fetch, a gRPC stub). Health-check `/health` `/status` `/ping` or
  the API base first.
- Sequential, in TC-id order; record actual status/body summary. Auto-fix a flaky
  **test** ≤ 3 attempts; genuine defects → DefectLog.

## Evidence contract
- API has **no screenshot/video**. Capture the **request + response summary** into the
  case's `actual`/`notes` (method, URL, status, key response fields). `device_results`
  is not used for API (not responsive).

## Pass/Fail criteria
Status code matches expected AND response satisfies the contract (required fields /
schema / auth rule). A contract violation fails the case.

## Report mapping (→ shared Reporting_Engine, unchanged)
API cases render into the `API` sheet of the XLSX workbook and the DOCX §5 API
detailed results (with an API-scoped traceability matrix + defect log). No engine
change needed. *(No separate HTML report is produced.)*

## Standalone use
"test this API / OpenAPI spec" → generate → execute → capture, then build reports via
the hub's `build_reports.py`.

## Standalone re-test (router command: `retest api`)
Re-run **only** the API layer against an existing suite — "Re-run scoped to API":
1. **Reuse** `data/test_cases.json` (do NOT regenerate); select cases where
   `effective_test_type() == API`.
2. Re-run the API health check; re-execute just those cases with the project's HTTP
   client, re-capturing request/response into `actual`/`notes`.
3. **Diff** against the baseline snapshot for the API cases (`regression_diff.py`),
   rebuild reports (XLSX + DOCX), and snapshot.
4. Requires an existing suite + a prior run; otherwise fall back to the normal flow.
Non-API cases keep their previous status untouched (scoped, idempotent, no drift).

> Extending to a new test type? See `../automation-testing/reference/DOMAIN_CONTRACT.md`.
