---
name: automation-testing-report
description: "Stage 6 (Report) of the QA Automation Testing skill library. Activated by the automation-testing router (or the 'report' command) after Scale Suite. First activates the automation-testing-evaluate helper to compute a correct evaluation and author real per-section narrative into report_meta.json, then summarizes results from the single source test_cases.json into the two deliverables — Test_Case_Summary.xlsx and Application_Testing_Report.docx — via the shared Reporting_Engine (one model → two writers, no drift) in the project's language, then snapshots the run as the next baseline. Hands off to automation-testing-rerun. Use to build the QA reports after a run."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 6 of 7
  keywords: [report, xlsx, docx, narrative, parity, snapshot, evaluate]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 6 — Report (dual-format, no drift)

**Purpose:** render the reports from the *same* `test_cases.json`, via the **bundled
Reporting_Engine only** (never external `xlsx`/`docx` skills). **Reply in the user's
language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-report`. Shared engine + docs: sibling
hub `../automation-testing/`. Python is required (the engine); Node is not needed here.
*(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | `data/test_cases.json` (with run results) |
| **Optional inputs** | `data/report_meta.json` (metadata + authored `narrative`); `data/analysis.md` |
| **Reads** | `data/test_cases.json`, `data/report_meta.json`, `data/analysis.md` (via the Evaluate helper) |
| **Writes** | `doc/Test_Case_Summary - <system>.xlsx`, `doc/Application_Testing_Report - <system>.docx`; a run snapshot under `results/runs/<ts>/` |
| **Delegates to** | `automation-testing-evaluate` (author narrative first) |
| **Next** | `automation-testing-rerun` (Stage 7) |

## Pre-check: are the cases actually executed?
Before treating this as the FINAL report, confirm `data/test_cases.json` carries
real results. **If every case is still `Status = Not Run`** (nothing executed),
this is only a Generate-stage **preview**, not a test report — go back and run
**Scale Suite** first (and make sure it persisted `status` + `device_results`).
Building the final report on all-`Not Run` cases yields honest-but-empty results
(0 passed / 0 failed, no evidence, no per-device breakdown) — say so plainly rather
than presenting it as a completed test.

## First: run the Evaluate helper (author the narrative from real data)
Before building, **activate `automation-testing-evaluate`** — the non-gated
evaluation helper. It aggregates everything collected across the run
(`data/analysis.md`, `data/test_cases.json` with per-device results, the DefectLog,
run metadata), computes a **correct evaluation** (pass rate over executed cases,
derived quality level, coverage, risk assessment, positive/negative outcome, defect
breakdown), and **authors real project-specific prose** into
`qa-automation-testing/data/report_meta.json` under `narrative` (purpose, scope,
audience, objectives, out_of_scope, risks_assumptions, pass_fail_criteria,
executive_overview, key_findings, recommendations, next_steps, coverage, verdict,
quality_assessment) in the project's language. It also sets `report_meta.language`
(`th`/`en`) and `report_meta.tool` (the UI driver that ran). When the project has **no
real source document**, the helper still fills every section, relying on the report
engine's built-in fallback layout and per-section prose (a guaranteed-neat document
the engine produces from code).

This guarantees the documents carry genuine analyzed content — never boilerplate.
Unauthored fields fall back to computed/default sentences (never a raw
`{{placeholder}}`). **Section headings default to English** (body/narrative stays in
the project language; override with `report_meta.heading_language`).

**State what was NOT tested — explicitly and clearly.** For any layer/feature/section
that was not exercised, write a clear ≥5-line explanation of *what* was not tested and
*why* (out of scope, no such layer, blocked, etc.) — never leave a section blank, and
never use a bare "-" for a whole section (a single dash is only for one empty table
cell). Only real, executed results may be presented as results.

## Build (one canonical command → same output on any tool)
```bash
python .kiro/skills/automation-testing/scripts/build_reports.py qa-automation-testing
```
Outputs under `qa-automation-testing/doc/` (files are **system-named** from
`report_meta.system_name`, e.g. `… - Swag Labs.xlsx`; base names when no system):
- **`Test_Case_Summary - <system>.xlsx`** — one workbook: Info → Summary → TestCases
  (index, TC-ID links to per-type detail) → per-type detail → per-device
  (Desktop/Tablet/Mobile) → Traceability → DefectLog. Summary has a By-Device table +
  a Defects-by-Severity note; Info records the browser + device/viewport matrix.
- **`Application_Testing_Report - <system>.docx`** — consolidated Test Execution
  Report (sections 1–8): cover (with system name + date), approval, revision,
  TOC / List of Tables / List of Figures, per-device pass/fail summary, detailed
  results, defects (with severity classification), conclusion, and Appendix 8.6
  **evidence for every test case** (embedded screenshots with captions).
*(No HTML report is produced — the deliverables are the XLSX workbook + the DOCX
report only.)*

## Verify (must pass; file must not be corrupted)
Run report parity (id + status agree across xlsx/docx):
```bash
python .kiro/skills/automation-testing/scripts/parity.py qa-automation-testing/data/test_cases.json
```

## Snapshot the run (next baseline)
At the end of Report, snapshot the completed run for future regression diffs:
```bash
python .kiro/skills/automation-testing/scripts/runs.py snapshot qa-automation-testing
```

## Approval Gate
Summarize the produced documents (paths, pass rate, quality level, defect counts),
then present the gate in the canonical tidy layout (see router "Approval Gate
rule" — blank line, bold question, then the two choices as a numbered list,
**one per line, never inline**):

```markdown
**Reports OK?**

1. **Approve** — approve the reports and finish the run (Re-test later is optional — see below)
2. **Change** — tell me what to adjust and I'll rebuild and re-present
```

> This is the final stage of the run, so the confirming choice is **Approve**
> (sign-off on the results), not "Next".

- **Change** → adjust narrative/data, rebuild, re-present this gate.
- **Approve** → the reports are confirmed; emit the **completion message** below and
  **stop** (do NOT auto-start Stage 7 — Re-test is a later, on-demand action).

## Completion message (when the user approves the reports)
Once the user approves (Approve / OK), the first-run flow is **complete**. Reply in the
user's language with a short closing message that (1) states the flow is finished and
**confirmed by the user**, (2) lists the delivered documents, and (3) explains how to
re-test later as a **numbered list, one item per line** (same tidy format as the
gates — never inline). Then stop. Template:

```markdown
✅ The QA automation flow is complete and has been confirmed by you. Deliverables:
- `qa-automation-testing/doc/Test_Case_Summary - <system>.xlsx`
- `qa-automation-testing/doc/Application_Testing_Report - <system>.docx`

To re-test later (after a fix or a new development cycle), run one of:

1. **Full regression** — `rerun`: re-execute the whole suite and diff against this run
2. **One layer only** — `retest ui` / `retest api` / `retest unit`: re-run a single domain
3. **Start over** — `start`: re-gather and regenerate everything (new scope / requirements)
```

Do not proceed past this point without an explicit request from the user.

> Report contents, parity rules, metadata + narrative keys, and full CLI:
> `../automation-testing/reference/FIELD_REFERENCE.md` and `../automation-testing/reference/CLI_REFERENCE.md`.
