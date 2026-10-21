---
name: automation-testing
description: "Central router for the QA Automation Testing skill library. Use this skill for QA test automation across UI, API, and unit/integration layers on any project or tech stack. Trigger when the user asks to \"automation testing\", \"qa test\", \"test automation\", \"generate tests\", \"run tests\", \"scale the suite\", \"rerun\", \"regression\", or \"test this URL\" — or in Thai: \"ทดสอบ\", \"ทำ test\", \"สร้าง test case\", \"ตรวจสอบระบบ\", \"E2E test\", \"integration test\", \"ทดสอบอัตโนมัติ\", \"รัน test\", \"ทดสอบ URL\". This router analyzes the command and the existing artifacts, decides which lifecycle stage to enter, and AUTOMATICALLY activates the matching specialized skill (automation-testing-intake / -analyze / -generate / -validate / -scale / -report / -rerun). It is tool-agnostic — it reuses whatever UI e2e driver a project already has (Cypress/WebdriverIO/Selenium/Puppeteer) and falls back to a bundled Playwright default only when none exists. Testing is organized on two axes: lifecycle STAGES (intake…report) and TEST DOMAINS (automation-testing-domain-ui / -api / -unit) that own each test type and can be extended. It owns the shared Reporting_Engine (scripts/); each domain owns its own capture assets."
license: Proprietary. LICENSE.txt has complete terms
---

# QA Automation Testing — Skill Library Router

This is the **entry point and router** of a QA test-automation **skill library**.
It does not perform a stage itself; it **analyzes the user's command + the current
project artifacts, selects the lifecycle stage, and activates the matching
specialized skill**. It also holds the shared implementation every stage reuses.

## Environment Detection
Detect the platform so paths resolve on any AI tool: `.kiro/`→Kiro · `.claude/`→Claude
Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing`. Sibling skills live at
`{platform}/skills/automation-testing-*`. The project output tree is always
`qa-automation-testing/`. Python is always required (the engine); Node is optional
(only for Node-based UI drivers/health checks).

## Information Contract
| | |
|---|---|
| **Required inputs** | a user command (or none → auto-detect) and workspace access |
| **Reads** | the workspace, `qa-automation-testing/` artifacts (`data/`, `results/`), the stage + domain SKILL.md files it dispatches |
| **Writes** | nothing directly — each dispatched stage/domain skill owns its own reads/writes |
| **Owns** | the shared Reporting_Engine (`scripts/`) and `reference/DOMAIN_CONTRACT.md` |
| **Dispatches to** | the 7 stage skills, the Evaluate helper, and the `-domain-*` capability skills |

## Skill library (one skill per lifecycle stage)

QA automation runs as a **seven-stage lifecycle**, in this fixed canonical order.
Each stage is its own skill; this router hands off to them in turn:

```
Intake → Analyze → Generate → Validate One → Scale Suite → Report → Re-run/Regression
```

| # | Stage | Skill to activate | What it does |
|---|---|---|---|
| 1 | **Intake** | `automation-testing-intake` | Quick scan, then ASK the user for target (repo/URL), sources, scope, access, constraints, report metadata. |
| 2 | **Analyze** | `automation-testing-analyze` | Deep project analysis using intake inputs → QA layers, end-to-end coverage, scope level, plan. |
| 3 | **Generate** | `automation-testing-generate` | Produce/import the test-case list → `test_cases.json` + preview report. |
| 4 | **Validate One** | `automation-testing-validate` | Set up the chosen UI driver (reused tool, else Playwright default) + run ONE case (screenshot + video). |
| 5 | **Scale Suite** | `automation-testing-scale` | Ask device(s), run the full set sequentially per device, capture evidence. |
| 6 | **Report** | `automation-testing-report` | Build the XLSX workbook + DOCX report from `test_cases.json` via the shared Reporting_Engine. |
| 7 | **Re-run/Regression** | `automation-testing-rerun` | Re-run existing specs (no regen) + diff vs a baseline. |

> **Helpers (not gated stages):**
> • `automation-testing-evaluate` — activated **by Report** before building; aggregates
>   the run's data, computes the correct evaluation (pass rate, quality level, risk,
>   findings), and authors the narrative into `report_meta.json`.
> • `automation-testing-datamock` — consulted during **Intake/Analyze/Generate/Scale**;
>   resolves the **test-data source** (existing seed / generated mock / provided) and
>   the **environment**, and records them for the run.
> Neither has a gate of its own; the surrounding stage gate covers them.

## Test Domains (capability registry — what this skill can test)

The second axis: each **test type** is owned by a small **domain skill** (a
capability, not a gated stage). Stages stay generic and **consult the active
domain(s)** for the specifics of detection, generation, execution, evidence, and
report mapping. Adding a new kind of testing = add one row here + one
`automation-testing-domain-*` skill (see
[`DOMAIN_CONTRACT.md`](reference/DOMAIN_CONTRACT.md)) — **no stage edits**.

| Domain skill | Test types | Detect signal | Owns assets | Status |
|---|---|---|---|---|
| `automation-testing-domain-ui` | UI-E2E, UI-Functional | web app / live URL | Playwright capture templates | ✅ |
| `automation-testing-domain-api` | API | openapi/swagger, routes, `*.proto`, GraphQL | — (client-agnostic) | ✅ |
| `automation-testing-domain-unit` | Unit-Integration | jest/vitest/pytest/go test/… | — (native runner) | ✅ |
| `automation-testing-domain-performance` | Performance/Load | k6/jmeter/locust | *(planned)* | 🔒 |
| `automation-testing-domain-security` | Security | zap/semgrep/… | *(planned)* | 🔒 |
| `automation-testing-domain-accessibility` | Accessibility | axe/pa11y/… | *(planned)* | 🔒 |

**Domain dispatch:** Analyze detects which layers/domains apply and records them;
Generate/Validate/Scale then consult the matching `automation-testing-domain-*`
skill for that layer's specifics. Evidence + report shape stay identical across
domains because all reports come from the one shared Reporting_Engine.

## Routing procedure

1. **Reply in the user's language** (Thai or English; if unclear, ask once). These
   router instructions stay in English.
2. **Detect the entry stage** from an explicit command first, else from artifacts:

   | Command | Entry stage → skill |
   |---|---|
   | **start** | Intake → `automation-testing-intake` |
   | **analyze** | Analyze → `automation-testing-analyze` |
   | **generate** | Generate → `automation-testing-generate` |
   | **test one** | Validate One → `automation-testing-validate` |
   | **scale** | Scale Suite → `automation-testing-scale` |
   | **report** | Report → `automation-testing-report` |
   | **rerun** | Re-run/Regression → `automation-testing-rerun` |
   | **retest ui** / **retest api** / **retest unit** / **test-domain `<x>`** | Scoped domain re-test → activate `automation-testing-domain-<x>` in **standalone re-test** mode (see below) |
   | **status** | (read-only) report position + latest results; no stage change |

   **Domain re-test (scoped):** `retest <domain>` re-runs **only that test type**
   without touching the others — it activates the matching
   `automation-testing-domain-*` skill in its "Standalone re-test" mode: reuse the
   existing `data/test_cases.json` filtered by `test_type`, run just that layer,
   diff against the baseline snapshot, and rebuild the same reports (no regenerate,
   no drift). Requires an existing suite + prior run; otherwise route to the normal
   flow. This is "Re-run scoped to one domain".

   **Add / change a device on the EXISTING test cases:** route to **`rerun`** —
   it asks up front whether to **Replace** the chosen device(s)' results or **Add**
   them (merge, keeping every device already recorded). "Add" is how you run a new
   device (e.g. tablet/mobile) on the existing suite without losing the devices
   already tested; results are merged and rolled up via
   `scripts/device_results.py`. No regenerate.

   Auto-detect when no explicit command is given:

   | Project state | Entry stage → skill |
   |---|---|
   | No `test_cases.json` and no specs | Intake → `automation-testing-intake` |
   | `test_cases.json` exists, specs missing | Generate → `automation-testing-generate` |
   | `test_cases.json` + specs + prior results | Re-run/Regression → `automation-testing-rerun` |
   | `test_cases.json` + specs, no prior results | Scale Suite → `automation-testing-scale` |
   | Testing a live URL, nothing generated yet | Intake → `automation-testing-intake` |

3. **Activate that stage's skill** and let it do its work.
4. **At each Approval Gate**, the stage skill stops and asks **"Change / Next?"**.
   - On **Change** → re-activate the *same* stage skill (stage index unchanged).
   - On **Next** → activate the **canonical successor** stage skill (advance one).
   - Never auto-advance across a gate; never skip stages.

## Approval Gate rule (applies to every stage)

After **every** stage the active skill MUST present the gate in this **exact,
tidy layout** — never mash the choices onto one line. Emit it as real markdown so
each part sits on its own line:

1. A short **summary** of what just happened (counts, pass/fail, evidence, problems).
2. A blank line, then a bold one-line **question** for this stage
   (e.g. `**Scope/coverage correct?**`).
3. A blank line, then the **two choices as a numbered markdown list — one per
   line, in this order** (fill the em-dash text for the current stage):

```markdown
**<one-line question for this stage>**

1. **Next** — <what proceeding does, e.g. go to Stage N (Name)>
2. **Change** — tell me what to revise and I'll re-present this step
```

**Formatting rules (mandatory):**
- Each choice is its **own list item on its own line**. Never write
  "1. Next … 2. Change …" inline in a single sentence/paragraph.
- Keep exactly **two** choices, `Next` before `Change`, both **bold**. The
  confirming choice is normally `Next`, but the **final stage of a run uses a
  sign-off word instead**: **Report → `Approve`**, Re-run/Regression → `Done`.
- Put a **blank line** before the question and before the list.

Then pause until the user responds. `1`/`next`/`approve` advances (or, at the
final stage, finishes); `2`/`change` re-enters the same gate. No other
transitions are permitted.

**Report (Stage 6) — final gate of a run:** the confirming choice is **`Approve`**
(not "Next"). On approve, emit the completion message + a short "how to re-test"
note, then **stop** (Re-test is a later, on-demand action — never auto-start it).

**Final stage exception (Re-run/Regression):** the last stage does **NOT** offer
"Next" and must **not** auto-continue. After it finishes, summarize the result and
**let the user decide** whether to re-test, with a short "how to re-test" note (e.g.
"run `rerun` for a full regression, or `retest ui|api|unit` for one layer") — then stop.

## Status announcements

Keep the user oriented in their language: announce the stage starting and what it
will do (e.g. "Stage 4/7 — Validate One"); during Scale/Re-run announce per-layer,
per-case progress in id order; for suites > 20 cases surface an ETA every 5 cases;
summarize at every gate before the Change/Next question. Never run silently for long.

## Shared implementation (owned here, referenced by stage skills)

This central skill holds the code every stage reuses, referenced by sibling paths:

- **`scripts/`** — the bundled **Reporting_Engine** (Python; one model → XLSX + DOCX
  writers, plus `scope.py`, `gate.py`, `paths.py`, `runs.py`,
  `regression_diff.py`, `parity.py`, `labels.py`). Canonical build command:
  ```bash
  python .kiro/skills/automation-testing/scripts/build_reports.py qa-automation-testing
  ```
  The report layout + per-section fallback prose are **built into the engine** (no
  separate template files needed). Domain-specific capture assets live with their
  domain (e.g. the Playwright templates are owned by
  `automation-testing-domain-ui/assets/templates/`).
- **Test-domain skills (`../automation-testing-domain-*`)** — one per test type; each
  owns its own capture assets and describes how that type is detected/generated/run/
  evidenced. See the Capability Registry above and `reference/DOMAIN_CONTRACT.md`.
- **Reference docs (in [`reference/`](reference/)):** [`FIELD_REFERENCE.md`](reference/FIELD_REFERENCE.md) (model fields,
  `report_meta.json` keys, report contents, parity), [`CLI_REFERENCE.md`](reference/CLI_REFERENCE.md)
  (all script commands), [`DOMAIN_CONTRACT.md`](reference/DOMAIN_CONTRACT.md) (add a new test type),
  [`SKILL_DEV_STANDARD.md`](reference/SKILL_DEV_STANDARD.md) (house dev standard + review checklist),
  [`README.md`](README.md), [`requirements.txt`](requirements.txt).

Stage skills reference these as `../automation-testing/scripts/…` and
`../automation-testing/assets/…` — there is **one** shared copy, never duplicated.

## Cross-cutting rules (all stages honor these)

- **Language:** all user-facing text + DOCX narrative in the user's language;
  report language is data-driven (`report_meta.language`, else auto-detected).
- **Report language & prose are never hardcoded** — headings/labels live in
  `scripts/labels.py` (th/en); section prose is authored per project into
  `report_meta.json` under `narrative`.
- **Tool-agnostic UI driver:** the UI layer reuses the project's existing e2e tool
  (Cypress/WebdriverIO/Selenium/Puppeteer); the bundled Playwright templates are the
  **default only when the project has none**. The chosen tool is recorded as
  `report_meta.tool`, and the evidence contract (screenshot/case, per-case video,
  per-device + per-flow full videos, `device_results`) is identical for every tool.
- **Node is optional** — required only when the chosen UI driver or the JS health
  check is Node-based. Pure Python/Go/etc. projects testing only API/Unit layers
  need no Node; the Reporting_Engine is Python-only.
- **Tool-agnostic output:** all documents are rendered only by the Reporting_Engine
  via `scripts/build_reports.py`. Never hand-craft reports; never call external
  `xlsx`/`docx` skills.
- **Defect duty:** the skill auto-fixes only *test scripts* (≤3 attempts); genuine
  product defects are logged to the DefectLog and assigned to the developer, never
  fixed in product code.
- **Output layout** lives under the target's `qa-automation-testing/`
  (`data/` · `doc/` · `scripts/` · `results/`). See `reference/FIELD_REFERENCE.md`.
