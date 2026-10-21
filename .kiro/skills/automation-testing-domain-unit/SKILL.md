---
name: automation-testing-domain-unit
description: "Unit/Integration test DOMAIN (capability, not a lifecycle stage) of the QA Automation Testing skill library. Owns unit- and integration-level testing driven through the PROJECT'S OWN test runner (jest, vitest, mocha, pytest, go test, phpunit, junit, rspec, cargo test, bun test, …): how these cases are detected, generated/mapped, executed via the native runner, and evidenced (runner output / coverage). The Analyze/Generate/Validate/Scale stages consult this domain when the unit/integration layer is active; it also runs standalone. Language-agnostic — no browser, no Node required unless the runner is a Node runner. Results render into the Unit-Integration sheet."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: test-domain
  test_types: [Unit-Integration]
  keywords: [unit, integration, jest, vitest, mocha, pytest, go-test, phpunit, junit, rspec, cargo-test, coverage]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Test Domain — Unit / Integration

**Role:** the authority for *what and how* unit/integration testing works, driven
through the **project's own test runner**. Stages consult this domain when the
unit/integration layer is active; it also runs **standalone**. **Reply in the user's
language.**

> Capability skill, **not a gated stage** — the consulting stage owns the gate.

## Environment Detection
`.kiro/` → Kiro · `.claude/` → Claude Code · `.cursor/` → Cursor · `.windsurf/` →
Windsurf. `SKILL_DIR={platform}/skills/automation-testing-domain-unit`. Shared
library: sibling hub `../automation-testing/`.

## Detects when (→ `scope.py` TEST markers)
jest · vitest · mocha · pytest · go test · phpunit · junit · rspec · cargo test ·
bun test · a `tests/` dir. A test framework alone is **not** an application (does not
by itself trigger UI/API), but it does enable this layer.

## Test types owned (→ `report_model.TEST_TYPES`)
- **Unit-Integration** — function/module unit tests and integration tests run by the
  project's native runner.

## Information Contract
| | |
|---|---|
| **Reads** | `data/analysis.md` (unit/integration coverage), source modules + existing tests, the runner config |
| **Writes** | Unit-Integration rows into `data/test_cases.json` (`test_type` = `Unit-Integration`) |
| **Depends on** | the **project's own runner** (language-native). No browser. Node needed only if the runner is a Node runner (jest/vitest/mocha/bun) |
| **Owns** | no bundled templates (uses the project's runner + conventions) |

## How to generate / map
- Prefer **reusing/mapping existing tests** in the repo to cases; add missing unit
  cases for core logic (happy path + edge/error) where coverage is thin.
- Fill `scenario`, `steps` (or the test id/path), `expected`, traceability. Keep the
  test's own file/function as `source_reference` (`source_origin = Code`).

## How to execute
- Invoke the **native runner** (e.g. `pytest -q`, `npx vitest run`, `go test ./...`,
  `mvn test`) — no custom harness. Confirm the runner + dependencies are installed
  first (health check).
- Sequential, in TC-id order; capture pass/fail + failure message. Auto-fix a flaky
  **test** ≤ 3 attempts; a genuine product defect → DefectLog (never patch product
  code).

## Evidence contract
- No screenshot/video. Capture **runner output** (pass/fail, assertion message,
  optionally coverage %) into the case's `actual`/`notes`. `device_results` not used.

## Pass/Fail criteria
The case passes when the underlying test passes in the native runner. A failing
assertion / error fails the case with the runner's message recorded.

## Report mapping (→ shared Reporting_Engine, unchanged)
Cases render into the `Unit-Integration` sheet of `test_cases.xlsx` and the DOCX §5
detailed results. No engine change needed.

## Standalone use
"run the unit/integration tests and report" → map/generate → run native runner →
capture, then build reports via the hub's `build_reports.py`.

## Standalone re-test (router command: `retest unit`)
Re-run **only** the unit/integration layer against an existing suite — "Re-run scoped
to Unit-Integration":
1. **Reuse** `data/test_cases.json` (do NOT regenerate); select cases where
   `effective_test_type() == Unit-Integration`.
2. Confirm the runner + deps; re-invoke the **native runner** for just those tests,
   re-capturing pass/fail + message (and coverage if available) into `actual`/`notes`.
3. **Diff** against the baseline snapshot for those cases (`regression_diff.py`),
   rebuild reports, and snapshot.
4. Requires an existing suite + a prior run; otherwise fall back to the normal flow.
Other layers keep their previous status untouched (scoped, idempotent, no drift).

> Extending to a new test type? See `../automation-testing/reference/DOMAIN_CONTRACT.md`.
