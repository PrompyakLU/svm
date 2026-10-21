---
name: automation-testing-intake
description: "Stage 1 (Intake) of the QA Automation Testing skill library — the first-contact step that GATHERS information from the user. Activated by the automation-testing router (or the 'start' command). Does a quick workspace scan to propose sensible defaults, then ASKS the user only for what it cannot determine: target (an app in this repo or a live URL), sources of truth (requirements, API spec, existing test cases), scope & focus (smoke/focused/full), access/credentials, constraints, and optional report metadata for the DOCX cover. It also asks whether test cases already exist (a document to import, an already-runnable suite, or none yet) and routes the hand-off accordingly: a document imports via Generate, an existing runnable suite skips straight to Scale/Re-run, and nothing-yet goes to the detailed Analyze stage. It records report_meta.json. Use when starting a QA automation run and collecting inputs."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 1 of 7
  keywords: [intake, scan, target, sources, scope, existing-test-cases, routing]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 1 — Intake (gather information)

**Purpose:** first contact — set the **direction** only. Do a *light* scan, propose
sensible **defaults**, and let the user confirm or tweak. **Reply in the user's
language** (Thai or English; if unclear, ask once).

## Environment Detection
Detect the platform so paths resolve on any tool: `.kiro/`→Kiro · `.claude/`→Claude
Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-intake`. The output tree is always
`qa-automation-testing/` under the project; the shared engine + docs are the sibling
hub `../automation-testing/`. Python is always available; Node is optional (see
domain skills). *(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | a feature request / target (an app in this repo **or** a live URL) |
| **Optional inputs** | README/manifests, existing test docs (xlsx/csv/docx), `.kiro/specs/`, credentials, report metadata |
| **Reads** | workspace (light scan only — manifests, source layout, existing `qa-automation-testing/data/test_cases.json` + specs) |
| **Writes** | `qa-automation-testing/data/report_meta.json` (metadata / language / existing-test-cases answer a/b/c) |
| **Next** | per routing table below → `-generate` (import) / `-scale` or `-rerun` (runnable) / `-analyze` (none yet) |

> **Intake is intentionally light — do NOT try to be complete here.** The
> **thorough, complete project analysis** (every module, every QA layer, full
> end-to-end coverage) is done in **Stage 2 — Analyze**, which you confirm again at
> its own gate. So incomplete info now is fine; nothing is "missed" — Intake only
> decides *where to start* and *what the target is*. Never block Intake waiting for
> details Analyze will gather.

## How the user answers (state this to the user, every time)

Make the answer path unmistakable. Tell the user **explicitly**:

> "ทุกข้อมี **ค่าเริ่มต้น (default)** ที่ผมเดาจากโปรเจกต์แล้ว — ถ้าถูกต้อง พิมพ์
> **‘OK’ / ‘ใช้ค่าเริ่มต้น’** เพื่อไปต่อได้เลย ไม่ต้องตอบครบทุกข้อ; ถ้าจะแก้ บอก
> **เฉพาะข้อที่ต้องการเปลี่ยน** (เช่น ‘ข้อ 2 ใช้ live URL …’)."
> *(English: "Every item has a default I inferred — reply 'OK' to accept all and
> continue, or tell me only the item number(s) to change. You don't have to answer
> every question.")*

- **Only two items are decision-critical:** **the target** (this repo vs a live URL)
  and **whether test cases already exist** (routing). Everything else has a safe
  default and can be left as-is.
- If you also present a quick-pick (getUserInput), keep it to a few options that map
  1:1 to this: **(1) Accept all defaults & continue · (2) Use a live URL instead ·
  (3) I'll edit specific items** — and still allow free-text edits.

## Steps

### 1. Quick scan → propose defaults (light, not deep)
Glance at the workspace (README, manifests like `package.json`/`pyproject.toml`/
`go.mod`/`pom.xml`/`composer.json`/`*.csproj`, obvious source layout, any existing
test docs) only enough to pre-fill sensible defaults for the questions below.
Never ask for something this quick scan already answers.

**Scan broadly for sources of truth (open-ended, not a fixed list).** Note any
candidate source in any location/format, and infer beyond the examples. Among these,
**Kiro Spec mode** (`.kiro/specs/<feature>/requirements.md` · `design.md` · `tasks.md`
+ `.kiro/steering/`) is a **strong source when present** and, if found, is proposed
as a default source for Q4 — but its absence must not narrow the scan: also flag
requirement/PRD docs, API specs, README/docs, existing tests, or a live URL. Do not
deep-read here; just detect and list what exists — the **complete** discovery and
the decision of which sources to reference happen in Analyze.

### 2. Report what you see + name what is MISSING
Present a short plain summary: what the project appears to be (with the evidence),
and explicitly name the gaps (requirements, API spec, credentials/test data,
target URL, report metadata).

### 3. Present proposed defaults — the user confirms or edits (don't demand answers)
Show each item **with your inferred default already filled in**, tagged so the user
sees what actually needs a decision. Skip any item the scan already answered.

**[must]** = please confirm this one · **[default ok]** = leave as-is unless changing.

1. **[must] Do you have a test-case DOCUMENT to import?** — i.e. an existing
   **test-case list as a document** (xlsx / csv / docx / md), **not** a test
   script/automation code. This decides the entry path (see "Routing" below):
   - **(a) Yes — a test-case document** (xlsx/csv/docx/md list) → **import it**, don't
     regenerate over it;
   - **(b) No** → **build the list from scratch** (go through Analyze → Generate).

   > **Resume is automatic — don't ask it here.** If this project was already run by
   > THIS skill (a `qa-automation-testing/data/test_cases.json` + specs already
   > exist), that is a **resume / re-test**, which the router detects on its own from
   > the artifacts (→ Scale or Re-run) — it is *not* what "having test cases" means
   > to the user. Check for those files first; if found, just state it and confirm
   > the resume, rather than asking this question.
2. **[must] Target** — an app in this repo, or a **live URL**? If deployed, base URL?
3. **[default ok] Type / stack** — I propose «…»; correct me only if wrong.
4. **[default ok] Sources of truth** — requirement docs / user stories / API spec to
   base NEW tests on (skip when importing a doc). **If `.kiro/specs/<feature>/` was
   found, propose those (`requirements.md` + `design.md` + `tasks.md`) as the default
   sources.** Paths? *(Analyze reads them fully.)*
5. **[default ok] Scope & focus** — proposed «…»; depth `smoke` / `focused` / `full`.
6. **[default ok] Access & test data** — login / test data needed? Give base URL /
   accounts. **Test data (mockup):** is there existing seed/fixtures/a dataset to use,
   or should the skill **generate mock data** (respecting the schema)? And **which
   environment** do tests run on (local / dev / staging / demo / live URL)? *(Never
   run data-mutating tests on production without explicit confirmation; never echo
   secrets.)* The **`automation-testing-datamock`** helper resolves this — record the
   data source + environment for the run.
7. **[default ok] Constraints** — anything to avoid, target browsers/devices, notes?
8. **[confirm] Basic report / cover info** — the DOCX cover needs real data, so
   **explicitly ask for these** rather than letting them fall back to "—" (present
   them as a short fill-in-the-blank list the user can confirm or fill in one shot):
   - **Company / organization name** — for the cover + page header (ask directly; do
     not leave blank);
   - **System name** — the app/system under test (pre-fill from the repo/URL);
   - **Author / prepared by** — who ran the tests;
   - **Version** — document version (default `1.0`);
   - **Test date** — defaults to **today** if not given (never leave a date empty).

   Then the remaining optional cover fields (leave as-is if unknown): environment,
   app URL / API base, coverage, verdict, **`language`** (`th`/`en`; else
   auto-detected), test accounts. Only genuinely-unknown optional fields render as
   "—"; skip what the user lacks; never echo secrets.

   > Even in a one-liner run, **ask at least for the company name** before the report
   > stage — a cover showing "—" for the company is not acceptable. If the user
   > truly has none, record it as "N/A" (a deliberate value), not an empty default.

**Do not stall on [default ok] items** — if the user says "OK", accept every default
and move on. Only re-ask when a [must] item is genuinely ambiguous (e.g. no app in
the repo AND no URL given). A one-liner request ("test checkout on staging") →
infer everything possible and confirm the defaults in one shot.

### 4. Scaffold the output tree, then record inputs
**Create the standard output skeleton once, here at the first stage**, so every
later stage has a place to write and the (initially empty) evidence folders already
exist:
```bash
python .kiro/skills/automation-testing/scripts/paths.py qa-automation-testing
```
This creates `qa-automation-testing/` with `data/`, `doc/`, `scripts/`, and
`results/` (incl. empty `screenshots/pass`, `screenshots/fail`, `videos/`,
`visual-baselines/`, `runs/`). It is idempotent — safe to re-run.

Then write the collected report metadata to
`qa-automation-testing/data/report_meta.json` (flat or nested keys accepted). Note
the target (repo vs URL), the chosen scope focus, and the **existing-test-cases
answer** (a/b/c) for routing.

**Record the intended responsive device(s)** as `"devices": [...]` in
`report_meta.json` (default `["desktop","tablet","mobile"]` for a UI target, or the
user's subset). This makes the **Generate preview** documents already show the
planned device matrix (Info sheet + §3 + scope) instead of "N/A"; **Scale** later
narrows/confirms it to exactly what runs. For an API/Unit-only target, omit it.

> **Folder-creation policy (which stage creates what):** Intake creates the whole
> skeleton (above). Generate fills `data/test_cases.json` + `doc/` preview. Validate
> / Scale create per-device evidence subfolders under `results/` as they run. Report
> writes the final `doc/` + a `results/runs/<ts>/` snapshot. No stage assumes a
> folder exists without it being created here or by the engine (`build_reports.py`
> also `mkdir`s `doc/` defensively).

## Routing based on existing test cases (answer to Q1)

A raw test-case **document cannot be run directly** — it must first be imported into
runnable specs, and the UI driver config/capture (reused tool, else the Playwright
default) must be set up. Route the **Next** hand-off accordingly:

| Situation | What exists | Next hand-off |
|---|---|---|
| **(a) Yes — test-case document** (xlsx/csv/docx/md) | a list to import, no runnable specs | **`automation-testing-generate`** (import — skip Analyze's "derive from scratch"; still confirm scope) → Validate One → Scale |
| **(b) No test-case document** | nothing to import | **`automation-testing-analyze`** (full flow: Analyze → Generate → Validate → Scale) |
| **(resume) prior run of THIS skill** (`test_cases.json` + specs already present) | executable suite already set up | **auto-detected by the router** → **`automation-testing-scale`** (Run), or **`automation-testing-rerun`** if prior results exist. Not asked as Q1 — confirm the resume instead. |

> Q1 is only about **(a) vs (b)** — whether the user has a test-case *document* to
> import. The **resume** row is the skill's OWN prior output and is auto-detected
> from artifacts, not a "do you have test cases?" answer.
> **You still cannot "just Run" a document**: (a) always passes through Generate
> (import) + Validate One before Scale.

## Approval Gate
Summarize the confirmed inputs, the target, and the **detected entry path**
(import a document / build from scratch / resume — which stage is next), then present the gate in the canonical tidy
layout (see router "Approval Gate rule" — blank line, bold question, then the
two choices as a numbered list, **one per line, never inline**). Reply in the
user's language; the example below is Thai (`<next>` = the detected next stage):

```markdown
**ยืนยันข้อมูลตามนี้ไหม?**

1. **Next** — ไปต่อ Stage <next> ตามเส้นทางที่ตรวจพบ
2. **Change** — บอกข้อที่จะแก้ แล้วผมจะแสดงสรุปนี้ใหม่
```

- **Change** → apply the specific edit, re-present this same gate.
- **Next / OK** → hand off to the stage from the routing table above, via the router.

> Reassure once at the gate: the **complete** analysis (all modules/layers/coverage)
> happens next in **Analyze** and is confirmed there too — Intake only sets direction.

> Shared metadata keys: `../automation-testing/reference/FIELD_REFERENCE.md`.
