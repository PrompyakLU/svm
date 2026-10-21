---
name: automation-testing-evaluate
description: "Evaluation helper of the QA Automation Testing skill library. NOT a gated lifecycle stage — it is activated BY the Report stage (automation-testing-report) just before the reports are built. It aggregates everything collected across the run — the Analyze plan (data/analysis.md), the test-case model with per-device results (data/test_cases.json), the defect log, and run metadata — then computes a CORRECT, defensible evaluation (pass rate over executed cases, derived quality level, coverage, risk assessment, defect breakdown, key findings, recommendations, next steps) and AUTHORS that as real project-specific narrative prose into data/report_meta.json under `narrative`, in the project's language. This guarantees the DOCX/XLSX reports are filled with genuine analyzed content — never boilerplate or raw placeholders — even when the project had no source requirement document. Use whenever building the report, so the evaluation is analyzed correctly and written back before rendering."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-helper
  activated_by: automation-testing-report
  keywords: [evaluate, pass-rate, quality-level, risk, narrative, findings, recommendations]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Evaluate — turn collected data into a correct, authored evaluation

**Purpose:** make the report's content **real**. Aggregate the data collected from
the very start of the run, compute a **correct evaluation**, and write it back as
authored narrative so the rendered reports carry genuine analysis — not boilerplate.
**Reply / author in the user's language** (`report_meta.language`, else auto-detect).

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-evaluate`. Shared engine + docs:
sibling hub `../automation-testing/`. *(Activated by Report; can also run standalone
to (re)author the narrative.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | `data/test_cases.json` (with run results) |
| **Optional inputs** | `data/analysis.md`, `data/report_meta.json` (existing metadata) |
| **Reads** | `data/analysis.md`, `data/test_cases.json`, `data/report_meta.json`, the derived DefectLog, `results/` evidence |
| **Writes** | `data/report_meta.json` → `narrative` (+ `verdict` / `coverage` / `tool` / `language`) |
| **Returns to** | `automation-testing-report` (which then runs `build_reports.py`) |

> **This is a helper, not a gated stage.** The seven-stage lifecycle
> (Intake → Analyze → Generate → Validate → Scale → Report → Re-run) is unchanged.
> **Report (Stage 6) activates this helper first**, then runs `build_reports.py`. It
> has no Approval Gate of its own — the Report gate covers it.

## Why this exists
Evaluation is **not** a fixed template: it depends on each project's actual results.
The right numbers and judgements must be **analyzed and stored**, then rendered. This
helper is where the analysis happens so the report is trustworthy.

## Inputs — collect everything gathered from the start
Read and reconcile all of these (skip any that are absent):

| Source | What to take from it |
|---|---|
| `data/analysis.md` (Analyze) | scope level, QA layers, target, coverage outline, chosen source ranking, risk priorities |
| `data/test_cases.json` (model) | every case: status, category (positive/negative), qa_layer, priority, severity, `device_results`, evidence paths |
| `data/report_meta.json` | company/system/version/env/`tool`/`language` + any narrative already authored |
| DefectLog (derived from Fail/Blocked cases) | defect count, severities, linked TCs |
| `results/` evidence | that per-case + per-device + per-flow (positive/negative) videos and screenshots exist |

> There is no separate parser to run — `report_model.py` already exposes the
> aggregates (`defect_severity_counts`, `build_traceability`,
> `device_summary_counts`, `group_by_test_type`). Use them as the numeric truth so
> your prose matches what the workbook will show.

## Compute the evaluation (correct, deterministic)
1. **Pass rate** = Passed ÷ **Executed** (executed = total − Not Run − Skipped).
   State the denominator; never divide by cases that never ran.
2. **Quality level** (same thresholds the engine uses, so prose ⇄ numbers agree):
   **Excellent ≥ 95% · Good ≥ 85% · Acceptable ≥ 70% · Needs Improvement < 70% ·
   Not Assessed** when nothing executed.
3. **Coverage** — layers/modules actually exercised vs the Analyze plan; name any
   planned-but-not-run area explicitly (don't paper over gaps).
4. **Risk assessment** — weight failures by `risk_level`/`severity`: a failed
   Critical/auth/payment case dominates the verdict even if the overall rate is high.
5. **Positive vs negative outcome** — summarize how the happy-path (positive) and the
   error/negative journeys fared (these map to the per-flow full E2E videos).
6. **Defect breakdown** — counts by severity (Critical/High/Medium/Low) from the log.
7. **Per-device** — call out any device (desktop/tablet/mobile) that regressed.

## Author the narrative (write it back — the whole point)
Write real, professional, organization-grade prose into
`qa-automation-testing/data/report_meta.json` under **`narrative`** (create the file
/ key if missing), in the project's language, honoring the formal-language rule
(one language per document; transliterate technical terms, don't mix per-item).
Author these keys (they map 1:1 to the DOCX sections — see
`../automation-testing/reference/FIELD_REFERENCE.md`):

- **§1** `introduction`/`purpose`, `scope`, `audience`, `references`
- **§2** `objectives`, `test_types`, `out_of_scope`, `risks_assumptions`,
  `pass_fail_criteria`
- **§4** `executive_overview`, `key_findings` (list), `verdict`
- **§7** `coverage`, `quality_assessment`, `recommendations` (list),
  `next_steps` (list), `conclusion`

Rules:
- **Ground every sentence in the computed numbers** above — no invented results.
- **Real tested data only.** State only what was actually executed. Do **not** claim
  coverage/results for anything that did not run.
- **`scope` / `test_types` / `out_of_scope` must mirror the ACTUAL run** — list only
  the QA layers present in `test_cases.json` (and, for UI, only the sub-types that
  ran: E2E and/or Functional) and only the device viewports that actually have
  `device_results`. Never list UI + API + Unit "by default"; if a layer or a device
  did not run, put it under `out_of_scope`, not `scope`. (The engine's fallback prose
  is already computed this way — your authored prose must not re-introduce untested
  claims.)
- **Every section must be substantive — at least 5 lines of professional prose.** A
  one-liner is not acceptable for a formal report. Expand each heading with context,
  what was covered, what the numbers mean, and the implication.
- **When a section genuinely has no data**, do NOT leave it blank or a bare "-":
  write a clear ≥5-line explanation of *why* there is no data (e.g. "The API layer
  was out of scope for this run because the target is a front-end-only demo; no API
  endpoints were exercised; …") so the reader understands the gap. Use "-" only for
  a single empty *table cell*, never for a whole section.
- **Never leave a raw `{{placeholder}}`.** Unauthored keys fall back to the engine's
  computed sentence; replace the generic ones with project-specific prose everywhere
  you have real signal.
- Also set/confirm `report_meta.verdict`, `coverage`, `tool`, and `language`.

## No source document? Rely on the engine's built-in layout
When the project has **no real requirement/spec document** to draw content from, do
**not** leave sections empty. The report engine (`report_docx.py` / `report_xlsx.py`)
already produces the complete, neat, sectioned layout with professional fallback
prose for every section — so the structure is guaranteed regardless of authored
content. Your job is to **replace that generic fallback with project-specific prose**
wherever you have real signal.

Author it from what you DO have — the executed results, the code/URL analysis, and
the evidence — clearly marking inferred content as inferred (`source_origin =
Inferred`). The evaluation numbers still come from the real run; only the
descriptive framing leans on the template.

## Hand back
Return control to **`automation-testing-report`**, which then runs
`build_reports.py` and `parity.py`. Because the narrative + `report_meta` are now
authored, the rendered DOCX/XLSX carry genuine analysis and the layout matches the
reference templates.
