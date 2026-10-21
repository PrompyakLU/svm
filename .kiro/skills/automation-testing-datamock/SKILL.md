---
name: automation-testing-datamock
description: "Test-data (mock/seed) helper of the QA Automation Testing skill library. A cross-cutting capability consulted during Intake/Analyze/Generate/Scale to decide WHERE test data comes from: reuse an existing seed/fixture/dataset, generate realistic mock data that respects the app's schema/constraints, or drive a live account — and on WHICH environment the tests run. It never invents production data, keeps generated data isolated and cleaned up, records the data source + environment into report_meta, and hands realistic values to the domains. Use whenever a run needs test data and it wasn't already provided."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: capability-helper
  keywords: [mock, test-data, seed, fixture, faker, dataset, environment, isolation, cleanup]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Test Data (Mock / Seed) Helper

**Role:** decide **where the test data comes from** and **which environment** a run
uses, then hand realistic, safe values to the test domains. A **capability helper**
(not a gated stage) — consulted by Intake/Analyze/Generate/Scale. **Reply in the
user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-datamock`. Hub: `../automation-testing/`.

## Information Contract
| | |
|---|---|
| **Required inputs** | the feature/fields under test (from analysis.md), the target environment |
| **Optional inputs** | existing seed/fixtures/dataset, an API/DB to seed, a schema/OpenAPI, provided accounts |
| **Reads** | `data/analysis.md`, schema/contract/fixtures in the repo, existing seed scripts |
| **Writes** | `data/test_data.json` (the resolved dataset/plan) + records data source & environment into `data/report_meta.json` |
| **Feeds** | Generate (fills `test_data` / `test_data_source` per case) + Scale (setup/teardown) |

## The 3 questions it answers (ask the user when unknown)
1. **Source of test data** — one of:
   - **(a) existing** — a seed script, fixtures, a dataset file, or a known demo account
     (e.g. saucedemo `standard_user`) → reuse it; record where it came from.
   - **(b) generate mock** — no data yet → generate realistic values that respect the
     schema/constraints (types, formats, required, unique, ranges, enums) using a
     faker-style approach; seed via the app's API/DB where possible.
   - **(c) live/provided** — the user supplies accounts/data → use as given (never echo
     secrets).
2. **Environment** — where do the tests run? (local / dev / staging / demo / a live URL).
   Never run data-mutating tests against production unless the user explicitly confirms.
3. **Coverage intent** — which cases the data must support: positive (valid data),
   negative (invalid/boundary/duplicate), and edge (empty, max-length, special chars).

## Rules
- **Never invent or use real production/PII data.** Generated data is clearly fake
  (e.g. `test_<feature>_<timestamp>`), and unique per run so cases stay isolated.
- **Isolation + cleanup:** create data in setup, tear it down after (idempotent) — the
  UI/API/Unit domains own the actual setup/teardown; this helper defines the values.
- **Respect the schema/contract** when generating (OpenAPI / DB schema / form rules) so
  mock data is accepted by the app.
- **Record provenance:** write the chosen source (`existing` / `generated` / `provided`)
  and the environment into `report_meta` so the report states how the run was fed.

## Output
Write the resolved plan/values to `qa-automation-testing/data/test_data.json`, e.g.:
```jsonc
{
  "environment": "staging",
  "source": "generated",            // existing | generated | provided
  "datasets": {
    "valid_user":   { "username": "test_user_1720085000", "password": "P@ss!123" },
    "invalid_user": { "username": "test_user_1720085000", "password": "wrong" }
  },
  "cleanup": "delete users with prefix test_user_ after the run"
}
```
Generate consumes this to fill each case's `test_data` + `test_data_source`; Scale uses
it for setup/teardown. When data already existed, `source: "existing"` and the datasets
point at the provided source.

## Standalone use
"create mock data for <feature>" / "what test data should we use?" → run the 3
questions, produce `test_data.json`, and record source + environment.

> Extending / conventions: `../automation-testing/reference/DOMAIN_CONTRACT.md` and
> `../automation-testing/reference/SKILL_DEV_STANDARD.md`.
