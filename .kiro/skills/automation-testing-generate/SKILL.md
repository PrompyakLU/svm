---
name: automation-testing-generate
description: "Stage 3 (Generate) of the QA Automation Testing skill library. Activated by the automation-testing router (or the 'generate' command) after Analyze. Produces the test-case list — either generated from the analysis or IMPORTED from an existing test-case document (xlsx/csv/docx) without regenerating over it — covering each feature end-to-end, ordered by user journey, with every case tagged with test_type, source_reference, source_origin, and user_story_id. Writes qa-automation-testing/data/test_cases.json plus a preview report (Status blank) so the plan is visible before any run. Hands off to automation-testing-validate. Use when creating or importing the test-case list."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 3 of 7
  keywords: [generate, test-cases, import, traceability, test_type, preview]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 3 — Generate (test-case list)

**Purpose:** turn the confirmed plan into the concrete `test_cases.json` model plus
a preview report. **Reply in the user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-generate`. Shared engine + docs:
sibling hub `../automation-testing/`. *(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | `data/analysis.md` (the confirmed plan) **or** an existing test-case document to import |
| **Optional inputs** | `data/report_meta.json`; the active domain skills for per-type generation rules |
| **Reads** | `data/analysis.md`, imported doc (xlsx/csv/docx), the active `automation-testing-domain-*` skills |
| **Writes** | `qa-automation-testing/data/test_cases.json` (the model) **+** preview `doc/` reports (Status blank) via `build_reports.py` |
| **Next** | `automation-testing-validate` (Stage 4) |

## Generate from analysis
Read the confirmed plan in **`qa-automation-testing/data/analysis.md`** (written by
the Analyze stage: sources + chosen references, QA layers, scope, per-feature
coverage outline, risk priorities) and materialize it into test cases. Produce the
list covering each feature **end-to-end**, ordered by journey. Tag every case with:
`test_type`, `source_reference`, `source_origin`, `user_story_id`, `qa_layer`,
`priority`, `module`, `steps`, `expected`, `test_data` — carrying through exactly the
`user_story_id` / `source_reference` / `source_origin` chosen in `analysis.md`.

**Record the QA layer + test type** on each case:

| `test_type` | Use for |
|---|---|
| **UI-E2E** | full user journeys spanning multiple pages/features |
| **UI-Functional** | single feature/function behaviors (field validation, one control) |
| **API** | endpoint contracts (status, envelope, error codes, rules) |
| **Unit-Integration** | module / integration logic via the project's runner |

Aim for complete end-to-end coverage per feature. `test_type` is a model field (not
an XLSX column); `report_model.effective_test_type()` derives it from `qa_layer`
when blank. *(Performance / Security / Accessibility are out of scope for v1.)*

> **Consult the active TEST DOMAIN** for each layer's generation specifics —
> `automation-testing-domain-ui` (UI-E2E + UI-Functional, incl. positive/negative
> journeys), `automation-testing-domain-api` (contract + functional per endpoint),
> `automation-testing-domain-unit` (map/extend native-runner tests). The active
> domain(s) were recorded in `data/analysis.md`.

## Source traceability (never blank)
Set on each case: **`source_reference`** (`file:section` or `URL#anchor`),
**`source_origin`** (`Doc`/`Code`/`UI`/`URL`/`User-Provided`/`Inferred`), and
**`user_story_id`** (stable backlog id or derived from the feature).

## Test data (from the datamock helper)
Fill each case's **`test_data`** + **`test_data_source`** from
`qa-automation-testing/data/test_data.json` when present (produced by
**`automation-testing-datamock`** — existing seed / generated mock / provided). If it
is absent and a case needs data, consult the datamock helper first (source +
environment). Positive cases use valid data; negative cases use invalid/boundary/
duplicate values. Generated data must be clearly fake + unique per run.

## Import an existing test-case doc (do NOT regenerate over it)
When the user named an existing doc (xlsx/csv/docx) at Intake, **import it as the
basis**:
1. **Parse & map** columns to model fields; if ambiguous, show the *column → field*
   mapping and confirm. Stamp `source_origin = User-Provided`, `source_reference` =
   the file location.
2. **Preserve the user's ids** (mint new only for rows lacking one; never renumber).
3. **Gap-fill only with approval** — propose additions for missing features/layers;
   never silently overwrite/drop/reorder.

## Write outputs (BOTH — the model AND the preview documents)
1. Write the model → **`qa-automation-testing/data/test_cases.json`**.
2. **Always build the preview documents** (this step is mandatory — Generate must
   never end with only the JSON). Run the shared engine so the plan is visible as
   real documents (Status column blank, since nothing has run yet):
   ```bash
   python .kiro/skills/automation-testing/scripts/build_reports.py qa-automation-testing
   ```
   This writes to **`qa-automation-testing/doc/`**:
   - `Test_Case_Summary - <system>.xlsx`
   - `Application_Testing_Report - <system>.docx`

   (`<system>` comes from `report_meta.system_name`; without it the files are named
   `Test_Case_Summary.xlsx` / `Application_Testing_Report.docx`.)
3. **Verify the two files exist** under `doc/` before presenting the gate; if the
   build printed a validation error, fix `test_cases.json` (every case needs a
   non-empty `id` + `scenario`) and re-run. Report the preview file paths to the
   user so they can open the plan.

## Approval Gate
Summarize the case count by layer/type, then present the gate in the canonical
tidy layout (see router "Approval Gate rule" — blank line, bold question, then
the two choices as a numbered list, **one per line, never inline**):

```markdown
**Review the test-case list — is it correct?**

1. **Next** — go to Stage 4 (Validate One): run a single case to prove the capture setup
2. **Change** — tell me what to revise and I'll regenerate the list and re-present
```

- **Change** → revise/regenerate, re-present this gate.
- **Next** → hand off to **`automation-testing-validate`** (Stage 4) via the router.

> Model fields: `../automation-testing/reference/FIELD_REFERENCE.md`.
