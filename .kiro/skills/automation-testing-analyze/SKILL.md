---
name: automation-testing-analyze
description: "Stage 2 (Analyze) of the QA Automation Testing skill library — the DETAILED project analysis step. Activated by the automation-testing router (or the 'analyze' command) after Intake. Using the intake inputs, it thoroughly analyzes the project — source code, route definitions, API specs, docs, and any user-provided requirement/test documents — maps the applicable QA layers (UI / API / Unit-Integration, non-exclusive), derives end-to-end coverage per feature, computes a scope level (smoke/focused/full), applies risk-based prioritization, and proposes the test plan for the user to confirm or override. Hands off to automation-testing-generate. Use when analyzing a project in depth before generating test cases."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 2 of 7
  keywords: [analyze, coverage, qa-layers, scope-level, risk, source-ranking, ui-driver]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 2 — Analyze (detailed project analysis)

**Purpose:** with the intake inputs in hand, analyze the project **in depth** and
propose a concrete test plan. **Reply in the user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-analyze`. Shared engine + docs:
sibling hub `../automation-testing/`. *(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | the target (repo / URL) and the QA-layer direction from Intake |
| **Optional inputs** | `data/report_meta.json`, and any source of truth — specs/PRD/API contracts/code/README/live URL |
| **Reads** | the whole workspace (open-ended discovery), all discovered sources, `../automation-testing/scripts/scope.py` heuristics |
| **Writes** | `qa-automation-testing/data/analysis.md` (layers, active domains, scope level, coverage outline, source ranking, risk, UI driver) |
| **Next** | `automation-testing-generate` (Stage 3) |

> **This is where completeness is guaranteed.** Intake only set the direction and may
> be incomplete — Analyze is responsible for the **thorough, complete** picture:
> read the code/sources yourself, cover **every module and every applicable QA
> layer**, and fill gaps Intake left. If something is still unknown after analysis,
> surface it here and ask — do not assume. The user confirms the full plan at this
> stage's gate before any test cases are generated.

## Steps

### 1. Discover ALL sources first — open-ended, then read them in full
**Do not limit discovery to a fixed list of paths.** Scan the whole workspace and
enumerate **every** candidate source of truth, in **any location and any format**,
inferring beyond the examples below (the list is illustrative, NOT exhaustive):

- **Kiro Spec mode** — `.kiro/specs/<feature>/requirements.md` · `design.md` ·
  `tasks.md`, and always-on `.kiro/steering/` (a very strong source **when present**);
- **Requirements / product docs** — PRD / SRS / BRD, user stories, `*.feature`
  (Gherkin), acceptance criteria anywhere;
- **API contracts** — `openapi`/`swagger`, route files, `*.proto`, GraphQL schema;
- **Code + comments**, README / docs / wikis, changelogs, tickets;
- **Existing tests / test docs** (xlsx/csv/docx), and a **live URL** to explore.

Read everything relevant in full. **Completeness must never depend on one
convention** — if `.kiro/specs/` is absent, fall back to whatever exists (code +
docs + live URL) and still achieve full coverage. Map findings to one or more
**QA layers** (enable every layer that applies).

### 1b. Decide which sources to REFERENCE (rank, then confirm)
After reading, **rank the discovered sources by authority** and choose what each
test case will trace to — this is a decision, not a hardcoded path:

- **Priority order (typical):** approved specs / requirements → design docs →
  API contracts → code/README → inferred. Kiro specs are usually the strongest
  **primary** when they exist; otherwise pick the best available.
- For **each** case set traceability from the **chosen** source:
  **`user_story_id`**, **`source_reference`** (`file:section` or `URL#anchor`),
  **`source_origin`** (`Doc` / `Code` / `UI` / `URL` / `User-Provided` / `Inferred`).
- **When a Kiro `requirements.md` is chosen:** map each EARS criterion
  ("WHEN… THEN… SHALL…") to test case(s) — criteria become the `expected` results and
  drive positive/negative variants; `design.md` → edge cases / error paths;
  `tasks.md` → implemented scope (test done items; flag not-yet-built as out of scope).
- **Present the source ranking to the user** and let them confirm or override which
  sources are authoritative before generating.

| QA layer | Enable when the project has… |
|---|---|
| **UI** | a front end / pages, or a live URL (`index.html`; react/vue/svelte/next/angular; `*.{jsx,tsx,vue}`) |
| **API** | HTTP/GraphQL/gRPC endpoints (`openapi`/`swagger`, route files, `*.proto`, GraphQL schema) |
| **Unit-Integration** | source modules with/for a test suite (jest, vitest, mocha, pytest, go test, phpunit, junit, rspec, cargo test) |

> `../automation-testing/scripts/scope.py` is a bundled **reference heuristic** for
> these signals and the thresholds below — a convenience, not the authority; your
> analysis and the user's answers win.

> **Each enabled layer maps to a TEST DOMAIN skill** that owns its specifics —
> UI → `automation-testing-domain-ui`, API → `automation-testing-domain-api`,
> Unit-Integration → `automation-testing-domain-unit`. Consult the matching domain for
> that layer's detect/generate/execute/evidence rules, and record the active domain(s)
> in `data/analysis.md`. (Full registry: `../automation-testing/SKILL.md`.)

### 1c. Detect the UI e2e DRIVER (tool-agnostic — reuse what the project has)
The skill is **not fixed to one UI test tool.** When the UI layer is enabled, decide
which driver Validate One / Scale Suite will use — **reuse the project's existing
driver** rather than forcing a switch:

| Existing marker | Driver to reuse |
|---|---|
| `playwright.config*`, `@playwright/test` | **Playwright** |
| `cypress.config*`, `cypress` dep | **Cypress** |
| `wdio.conf*`, `webdriverio` dep | **WebdriverIO** |
| `selenium-webdriver` dep / selenium setup | **Selenium** |
| `puppeteer` dep | **Puppeteer** |
| *(none of the above)* | **Playwright — the bundled default reference driver** |

> `scope.py` → `detect_ui_driver(workspace)` encodes this (marker → driver, else
> `DEFAULT_UI_DRIVER`). It is advisory; the user may override at the gate.

Whatever driver runs, the **evidence contract is identical** (a screenshot per case,
a per-case video, per-device + per-flow full videos, `device_results`) so the reports
never depend on the tool. Record the chosen driver as **`report_meta.tool`** so the
report states which tool actually ran. **Node is only required when the chosen driver
(or the health check) needs it** — a pure Python/Go project testing only API/Unit
layers needs no Node.

### 2. Derive end-to-end coverage
For each feature/module, derive the journeys and checks needed for **complete
end-to-end coverage** — pair each feature's functional checks (UI-Functional / API
contract / unit) with at least one full UI-E2E journey where a UI exists. Note the
`source_reference` + `source_origin` + `user_story_id` each future case will carry.

### 3. Propose a scope level (user can override)
| Project state | Scope level |
|---|---|
| Many layers / rich sources | `full` (full E2E) |
| One or two layers, prior run exists | `focused` (changed/active areas) |
| Single layer, few sources, first run | `smoke` (fast critical path) |

> Reference thresholds in `scope.py`: `MODERATE_SOURCE_THRESHOLD = 3`,
> `RICH_SOURCE_THRESHOLD = 10`.

### 4. Risk-based prioritization (for smoke/focused)
Assign each planned case a `risk_level` (Critical/High/Medium/Low) from
priority × module criticality (auth, payment, user data, core logic = critical).
Scale Suite later runs Critical → High → Medium → Low. **Smoke early-termination:**
if a Critical case fails, stop after the Critical batch and ask the user.

### 5. External_URL_Mode
If there is no in-project app (or the user pointed at a deployed app), the target
is the **live URL**: plan **UI** cases discovered by driving the URL with the
chosen UI driver (§1c), stamping `source_origin = URL`. If the URL is unreachable,
report and re-prompt — never plan against an unreachable URL.

### 6. Record the analysis (persist the plan — auditable)
Write the confirmed analysis to **`qa-automation-testing/data/analysis.md`** as a
**comprehensive project context** — it is the ground-truth record the whole run (and
the report narrative) builds on, so make it **thorough, detailed, and complete across
every dimension of the project under test**, not a thin outline. Include:

- **Project overview** — what the system is, its purpose, primary users/personas, and
  the business domain (2–3 paragraphs, from the sources/URL you read).
- **Tech stack & architecture** — languages, frameworks, runtime, how the app is
  structured (pages/services/modules), key data flows, and integrations/dependencies
  (as far as discoverable).
- **Features / modules inventory** — every functional area found, with a one-line
  description each (so coverage can be checked against the full feature set).
- **Sources discovered** (path + type) and the **chosen reference ranking** (primary
  vs supporting, and why).
- **QA layers enabled** + the **active test domain(s)** (`domain-ui` / `domain-api` /
  `domain-unit`) + the **scope level** and target (repo / URL).
- **UI driver chosen** (reused existing tool, or Playwright default) → also record as
  `report_meta.tool`; and the **browser(s)** + **devices/viewports** to test.
- **Coverage outline** — per feature/module, the planned journeys + checks (positive
  AND negative), each tagged with its intended `user_story_id` and `source_reference`;
  explicitly list features that are **out of scope** and why.
- **Risk priorities** (Critical/High/Medium/Low) with the reasoning (auth/payment/
  core = critical).
- **Test data & environment** — consult **`automation-testing-datamock`**: the data
  source (existing seed/fixtures · generated mock · provided accounts), the target
  environment, and (for generated data) how it respects the schema + is cleaned up.
- **Assumptions, constraints, and open questions** — anything unverified.

Aim for a document a new engineer could read to understand the project and the test
plan end-to-end. This context feeds Generate (test cases) and the Evaluate helper
(report narrative), so richer analysis here → more complete, accurate documents.

This is a human-readable decision record; **Generate** consumes it to produce
`data/test_cases.json` (where the same fields become per-case columns). Keep it in
`data/` next to `report_meta.json`. On re-analysis, overwrite it (the run snapshot
in `results/runs/<ts>/` preserves history).

## Where things are stored (quick map)
```
qa-automation-testing/data/report_meta.json   ← Intake (metadata/language/narrative)
qa-automation-testing/data/analysis.md         ← Analyze (this stage's plan)
qa-automation-testing/data/test_data.json       ← datamock helper (data source + env)
qa-automation-testing/data/test_cases.json     ← Generate (source of truth)
qa-automation-testing/doc/                      ← Report (xlsx/docx/html)
qa-automation-testing/results/                  ← evidence + runs/<ts> snapshots
```

## Approval Gate
Present the proposed **QA layers + scope level + target + coverage outline + source
ranking** (and note that the plan was saved to `data/analysis.md`), let the user
add / remove / change anything, then present the gate in the canonical tidy
layout (see router "Approval Gate rule" — blank line, bold question, then the
two choices as a numbered list, **one per line, never inline**):

```markdown
**Scope/coverage correct?**

1. **Next** — go to Stage 3 (Generate): create the test-case list from this plan
2. **Change** — tell me what to revise and I'll rewrite `data/analysis.md` and re-present
```

- **Change** → revise the plan, rewrite `data/analysis.md`, re-present this gate.
- **Next** → hand off to **`automation-testing-generate`** (Stage 3) via the router.
