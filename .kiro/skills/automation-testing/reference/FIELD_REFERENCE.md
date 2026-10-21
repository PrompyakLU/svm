# Field Reference

Detailed field and metadata reference for the QA Automation Testing skill,
split out of `SKILL.md` to keep the orchestrator flow short. `SKILL.md` links
here; this document carries no orchestrator behavior of its own.

## test_cases Field Reference

The single source of truth is `qa-automation-testing/test_cases.json` — a list of
`TestCase` records (see `scripts/report_model.py`). Generation fills the
generate-time fields; execution fills the run-time fields. The columns are grouped
for readability — **Identification**, **Test Definition**, **Execution Result**,
**Evidence** — and the XLSX column letters are shown for reference (**Status is
column R**, which every Summary formula references via a computed column letter;
`user_story_id` is column **B**, right after `id`; the embedded `screenshot`
image is column **Y** and its `screenshot_path` text note is column **Z**).

| Field | Group | XLSX col | Filled at | Notes |
|---|---|---|---|---|
| `id` | Identification | A | Generate | `TC_XXX`, ordered by E2E journey |
| `user_story_id` | Identification | B | Generate | maps the test case to its feature / user story (e.g. `US-12`, `AUTH-3`); placed right after `id` |
| `module` | Identification | C | Generate | functional module / area under test (e.g. `Authentication`, `Cart`) |
| `qa_layer` | Identification | D | Generate | `UI` / `API` / `Unit-Integration` |
| `scenario` | Identification | E | Generate | short description |
| `category` | Identification | F | Generate | Positive / Negative / etc. |
| `priority` | Identification | G | Generate | High / Medium / Low |
| `severity` | Identification | H | Generate | defect severity (High / Medium / Low / Critical); blank → derived from `priority` |
| `risk_level` | Identification | I | Generate | risk-based execution level (Critical / High / Medium / Low); blank → derived from `priority` × module criticality |
| `preconditions` | Test Definition | J | Generate | state that must hold before the steps run |
| `steps` | Test Definition | K | Generate | `;`-separated, ordered — the XLSX renders them as a numbered, one-per-line list (1. / 2. / 3.) |
| `test_data` | Test Definition | L | Generate | concrete inputs / fixtures used by the steps |
| `test_data_source` | Test Definition | M | Generate | origin of the test data (`inline` / `file` / `fixture` / `api`) |
| `expected` | Test Definition | N | Generate | measurable expected result |
| `rule_id` | Test Definition | O | Generate | business-rule id or `-` |
| `source_reference` | Test Definition | P | Generate | `file:section` or `URL#anchor` (non-empty) |
| `source_origin` | Test Definition | Q | Generate | `Doc` / `Code` / `UI` / `URL` / `User-Provided` / `Inferred` |
| `status` | Execution Result | **R** | Run | `Pass` / `Fail` / `Blocked` / `Not Run` / `Skipped` (blank pre-run) — **every Summary formula references this computed column** |
| `actual` | Execution Result | S | Run | actual result |
| `executed_by` | Execution Result | T | Run | tester / automation agent that executed the case |
| `executed_at` | Execution Result | U | Run | ISO 8601 + timezone |
| `environment` | Execution Result | V | Run | environment the case ran in (e.g. `staging`, `local`) |
| `defect_id` | Execution Result | W | Run | linked defect id when failed/blocked (mirrors the DefectLog) |
| `notes` | Execution Result | X | Run | duration, spec path, fix info |
| `screenshot` | Evidence | Y | Run | the actual captured image embedded into the report cell (from `screenshot_path`); its text value stays empty |
| `screenshot_path` | Evidence | Z | Run | text path note to the captured screenshot image — `…/screenshots/{pass\|fail}/TC_XXX.png` |
| `video_path` | Evidence | AA | Run | `…/videos/{pass\|fail}/TC_XXX.webm` |

> Note: `test_type` is a **model-only** field (not an XLSX column). When blank,
> `report_model.effective_test_type()` derives it from `qa_layer`
> (UI → UI-Functional) to group the per-type view sheets.

> Note: `device_results` is a **model-only** field (not an XLSX column) that holds
> the per-device (responsive) outcomes. It maps a device name
> (`desktop` / `tablet` / `mobile`) to that device's own result, e.g.:
> ```jsonc
> "device_results": {
>   "desktop": { "status": "Pass", "actual": "...", "screenshot_path": "...", "video_path": "..." },
>   "tablet":  { "status": "Pass" },
>   "mobile":  { "status": "Fail", "actual": "button overflow" }
> }
> ```
> The top-level `status` (and actual/screenshot/video) remain the **roll-up** across
> devices (worst-of status). Per-device **sheets** in `doc/test_cases.xlsx`
> (Desktop / Tablet / Mobile), the Summary **By Device** table, and the DOCX
> "Pass/Fail Summary by Device" are all rendered from this field
> (`report_model.device_view` / `device_summary_counts`). Evidence per device lives
> under `results/videos/{device}/` — per-Test_Case clips, one full end-to-end
> `full_run__{device}.webm`, plus two full end-to-end **process** videos, one per
> flow: `full_process__{device}__positive.webm` and
> `full_process__{device}__negative.webm` (paths from
> `paths.full_process_video_path(flow, device)`). Whatever UI driver ran
> (`report_meta.tool` — a reused Cypress/WebdriverIO/Selenium/Puppeteer setup or the
> Playwright default), these evidence paths are identical.

## Analysis plan (`data/analysis.md`)

The **Analyze** stage persists its confirmed plan to
`qa-automation-testing/data/analysis.md` (human-readable): the sources discovered +
the chosen reference ranking (primary vs supporting), the QA layers enabled, the
scope level + target, a per-feature coverage outline (each item tagged with its
intended `user_story_id` / `source_reference`), and risk priorities. It is the
auditable record **Generate** consumes to produce `test_cases.json`; it is
overwritten on re-analysis and captured in each `results/runs/<ts>/` snapshot.

## Report metadata (`report_meta.json`)

The DOCX report's cover, approval, revision, environment, and summary prose are
filled from an **optional** `report_meta.json` the skill writes during Intake
(ask the user for the values; never echo secrets). `report_docx.py` reads it via
`load_meta()` and normalizes it with `normalize_meta()`, which accepts **flat or
nested** keys (e.g. both `system_name` and `{"project": {"systemName": …}}`).
Recognized fields: `company`, `system_name`, `project_company`,
`project_company_en`, `version`, `created_date`, `updated_date`, `executed_date`,
`start_date`, `end_date`,
`environment`, `os`, `node_version`, `browser_version`, `memory`,
`playwright_version`, `app_url`, `api_base`, `author`, `tool`, `scope`,
`coverage`, `verdict`, `test_types`, `test_accounts` (a list of
`{role, username, privileges}`), and **`language`** (`"th"` or `"en"`).

**Report language is NOT fixed in code.** Every heading, table header, caption,
and default sentence lives in `labels.py` in both `th` and `en`. The writer
picks the set from `language`: the orchestrator SHOULD determine the project's
language during **Intake & Analyze** (from requirements/UI/app content or the
user's request) and record it as `language`. When `language` is absent the
writer **auto-detects** Thai vs. English from the content, defaulting to English.
**Any missing field renders as an em dash ("—") — never a raw `{{placeholder}}`.**
`created_date` / `updated_date` are shown on the DOCX cover and the XLSX **Info**
sheet; when omitted, `updated_date` defaults to the report's generation date and
`created_date` to the test start date (else the generation date).

### Authored narrative (`narrative`) — REQUIRED at the Report stage

`report_meta.json` also accepts an **optional `narrative` object** (the key may
also be named `report` or `sections`) holding the **authored prose for each
report section**. Each value is either free text (a string) or a bullet list (an
array of strings). Recognized keys (case-insensitive):

- **Introduction (§1):** `introduction` / `purpose`, `scope`, `audience`, `references` (list)
- **Test Strategy & Scope (§2):** `objectives`, `test_types`, `out_of_scope`, `risks_assumptions`, `pass_fail_criteria`
- **Executive Summary (§4):** `executive_overview`, `key_findings` (list), `verdict`
- **Conclusion & Recommendations (§7):** `coverage`, `quality_assessment`, `recommendations` (list), `next_steps` (list), `conclusion`

During the **Report** stage the orchestrator **MUST author each report section
from the actual analysis and results, in the user's chosen language** — real,
professional, organization-grade prose plus a real evaluation (overall pass
rate, derived quality level, key findings, recommendations, next steps) — and
write them into `report_meta.json` under `narrative` so the DOCX renders genuine
project content rather than generic boilerplate. This authoring is performed by
the **`automation-testing-evaluate`** helper, which Report activates first; when
no real source document exists it fills every section using the report engine's
built-in fallback layout and prose so the page layout stays neat. The Conclusion's Quality
Assessment (§7.2) additionally computes a **real evaluation** (pass rate over
executed cases, a derived quality level — Excellent ≥ 95%, Good ≥ 85%,
Acceptable ≥ 70%, Needs Improvement < 70%, or Not Assessed when nothing ran, and
defect counts by severity); any authored `quality_assessment` text is appended
to it. **Unauthored fields fall back to the computed/default sentence — never a
raw `{{placeholder}}`.**

## Report outputs (contents of each document)

The Report stage produces two complementary documents from the same serialized
`test_cases.json` (one model → two writers: XLSX, DOCX). No HTML report is produced.

> **Filenames are system-named** from `report_meta.system_name`:
> `Test_Case_Summary - <system>.xlsx`, `Application_Testing_Report - <system>.docx`
> (base names when no system name). Before a Re-test, the previous `doc/` set is
> copied to `Backup/<timestamp>/`.

- **`Test_Case_Summary - <system>.xlsx`** — a single **standard, industry-style QA workbook** (per
  ISTQB / IEEE-829), in tab order:
  **Info** (run metadata) → **Summary** (Total / Passed / Failed / Skipped /
  Blocked / Not Run / Pass Rate plus breakdowns by QA layer, **by device**, by
  test type, by priority, and defect counts by severity — Total = `COUNTA` of the
  index ids, Pass Rate = Passed / Total) → the **TestCases index** (a concise
  sheet with only the key columns — **TC ID · Test Type · Module · Scenario ·
  Priority · Device Type · Status** — where each **TC ID is a hyperlink** to that
  case's full detail row in its per-type sheet; the Summary formulas read this
  sheet's id/status columns) → the **per-type detail sheets** (**UI-E2E** /
  **UI-Functional** / **API** / **Unit-Integration**, only those with cases — full
  canonical columns + embedded screenshots; these are the link targets) → the
  **per-device sheets** (**Desktop** / **Tablet** / **Mobile**, one per device
  tested, each showing that device's own results via `device_view`) →
  **Traceability** (RTM) → **DefectLog**. There is exactly one workbook — no
  master-vs-by-device files and no `by-device/` folder.
- **`Application_Testing_Report - <system>.docx`** — a **standard, sectioned Test Execution Report** with
  bilingual Thai/English headings: a clean one-page cover (Thai title
  **"รายงานผลการทดสอบระบบ / Test Execution Report"**, system / company / companyEN,
  version, date, prepared-by, created & updated dates, and a **CONFIDENTIAL**
  marking), a page **header**
  carrying the company name (top-left) and a **footer** page number (bottom-right),
  Document Approval, Document Revision History, a **pre-populated Table of Contents**
  (a real Word `TOC` field whose cached result already lists every
  section/subsection, so it shows complete on open with **no "update fields?"
  popup**; page numbers fill in automatically on print / PDF export / F9), a
  **List of Tables** and **List of Figures** (real Word `TOC \c` fields backed by
  numbered `SEQ` captions on every table, so they auto-update and stay correct on
  F9), and
  numbered sections **1–8** (Introduction, Test Strategy & Scope, Test Environment
  & Tools, Executive Summary, Detailed Results, Issues & Defects, Conclusion &
  Recommendations, Appendix). Note: Performance and Security test-result sections
  are intentionally **excluded**. The report is **image-free**: black body text, table
  headers in a light-blue band (`EBF4FF`) with black bold labels, thin black
  (0.5pt) grid lines, professional pagination (each section starts on a new page,
  headings kept with their content, table rows never split across pages). The
  Appendix points to the companion **`test_cases.xlsx`** for the detailed cases,
  traceability, and defect log.
*(No standalone HTML report is generated — the deliverables are the XLSX + DOCX.
Playwright's own `results/html-report/` still exists for the browser-run UI specs.)*

### Data-driven sections & parity

The Executive Summary's **pass/fail-by-test-type** table and every count are
tallied **directly from the model** (never re-derived from the XLSX). Detailed
subsections are emitted **only for the QA layers that actually have cases**
(UI / API / Unit-Integration); **Performance** and **Security** subsections
appear **only when** cases exist whose `category` contains "Performance" /
"Security" — empty placeholder sections are never padded in.

The DOCX **Detailed Results** tables (`TC ID | user_story_id | Scenario |
Status | Notes`, split into Successful / Failed per layer) are the
machine-readable surface the **report parity** check relies on. Parity is
**relaxed to id + status**: `scripts/parity.py` asserts that every Test_Case id
in the XLSX `TestCases` sheet also appears in the DOCX detailed tables **with
the same status**.

### Screenshot evidence in the reports

The screenshot for each Test_Case is taken at the **end of that test case, before
the next one begins** — the `afterEach` hook in `_helpers.js` runs under
`workers:1`, so each `screenshots/{pass|fail}/{TC id}__{viewport}.png` corresponds
to exactly one Test_Case at one responsive viewport (desktop / tablet / mobile).
In each Detailed Results subsection, after the Successful/Failed tables,
`report_docx.py` writes an **Evidence** area that lists each `screenshot_path` as
**text only** (the Word report embeds no images). The XLSX_Report independently
embeds the actual screenshot image into its dedicated `screenshot` cell (column
**Y**), sourced from the file at `screenshot_path` when it exists; the text paths
for the screenshot and video remain in columns **Z** / **AA**.
