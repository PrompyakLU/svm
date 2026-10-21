---
name: automation-testing-rerun
description: "Stage 7 (Re-run / Regression) of the QA Automation Testing skill library. Activated by the automation-testing router (or the 'rerun' command) after a prior run exists. Re-executes the EXISTING specs WITHOUT regenerating test cases, first asking which responsive device(s) to re-run and which baseline to diff against (default = latest snapshot), re-running the health check, then diffing the current results against the baseline for newly-failing and newly-passing cases. Does NOT loop back through Intake/Analyze/Generate. Use to check for regressions after a fix or a later development cycle."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 7 of 7
  keywords: [rerun, regression, baseline, diff, snapshot, no-regenerate]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 7 — Re-run / Regression

**Purpose:** re-execute the existing suite and report the delta vs a baseline —
**without regenerating** test cases. **Reply in the user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-rerun`. Shared engine + docs: sibling
hub `../automation-testing/`. *(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | an existing runnable suite (`data/test_cases.json` + specs) and at least one prior run snapshot |
| **Optional inputs** | a chosen baseline snapshot (default = latest); the device(s) to re-run (asked first) |
| **Reads** | `data/test_cases.json`, the specs, `results/runs/<baseline_ts>/data/test_cases.json` |
| **Writes** | updated results into `data/test_cases.json` + rebuilt `doc/` reports + a new run snapshot |
| **Next** | lifecycle complete for this cycle (return to the router) |

## Rules
- **Do NOT regenerate.** Reuse the confirmed language, scope, layers, target, and
  the existing `test_cases.json` + specs. Changing scope/layers/cases or
  regenerating is a fresh **`start`** run (re-enter at Intake), not a Re-run.
- **Back up the previous reports FIRST.** Before re-running (which will overwrite
  `doc/`), preserve the latest `.docx` / `.xlsx` into `Backup/<timestamp>/`:
  ```bash
  python .kiro/skills/automation-testing/scripts/runs.py backup qa-automation-testing
  ```
- **Re-run the health check** (see Validate One) before executing.
- Execute sequentially (`workers:1`) in TC-id order, **one device per Playwright
  project, one at a time** — `npx playwright test --project=<device>` for each
  chosen device (never a bare run, which would execute every viewport). Capture
  evidence exactly as Scale Suite does; auto-fix flaky scripts ≤ 3.
- **ADD mode = merge, never lose devices.** Load the existing `test_cases.json`,
  run only the NEW device(s), and write their results into each case's
  `device_results[<device>]` **without touching the device keys already there**.
  Then update `report_meta.devices` to the **union** (existing + new) so the report
  shows all devices. REPLACE mode overwrites only the re-run device(s).
- After running, rebuild the reports (`build_reports.py`) so the per-device tables
  reflect the merged set.

## First: ASK the re-test questions — ALL of them, never assume
Before touching anything, ASK the user the full set below (present them as a
short numbered list with the inferred default on each line, so nothing is missed).
Accept "OK/ใช้ค่าเริ่มต้น" to take all defaults; otherwise apply only the items
they change. **Never silently pick devices or a result-mode** — a wrong guess here
either loses prior results or wastes a full re-run.

1. **Result mode — REPLACE or ADD? (decision-critical, ask explicitly)**
   - **(a) Replace** — this re-run's device(s) overwrite the stored results for
     those device(s) (a normal regression on the same devices).
   - **(b) Add / keep existing** — run the chosen device(s) and **merge** them into
     the existing results, **preserving every device already recorded** (use this to
     add tablet/mobile to a suite already run on desktop). This is the
     "run a device on the existing test cases" case.
2. **Which device(s)** to run this time — desktop (1440×900) / tablet (768×1024) /
   mobile (390×844). In **Add** mode, list only the NEW/target device(s); the others
   stay as they are.
3. **Scope of this re-run** — all cases / only one layer (`ui`/`api`/`unit`) / only
   the cases that previously **failed**. Default: all cases.
4. **Baseline to diff against** — latest snapshot (default) or a specific one
   (`runs.py list` shows them).
5. **Target/health unchanged?** — same URL/credentials/environment as before, or
   updated? (Re-run the health check regardless.)

State the resolved answers back to the user (mode, devices, scope, baseline) before
running so there is no ambiguity.

## Persist results — MERGE (add mode) vs REPLACE, then ALWAYS roll up
How you write results back to `data/test_cases.json` depends on the mode chosen in
Q1 — this is the step that most often loses data if done by hand, so use the helper:

- **Add / keep-existing mode (MERGE):** update ONLY the executed device(s) under each
  case's `device_results`, and **never delete a device key that was not re-run**. Do
  not hand-merge — run the freshly captured results as a separate model and merge it,
  or edit only the new device keys, then let the helper recompute the roll-up:
  ```bash
  # optional: merge a separate new-run model additively into the main one
  python .kiro/skills/automation-testing/scripts/device_results.py merge \
      qa-automation-testing <path/to/new_run_test_cases.json>
  ```
- **Replace mode:** overwrite the chosen device(s)' `device_results` for this run
  (other devices, if any, are still preserved unless the user explicitly asked to
  reset them).
- **ALWAYS finish with the roll-up** so each case's top-level status is the correct
  worst-of across ALL devices present (Fail > Blocked > Skipped > Not Run > Pass) and
  the headline evidence points at the worst device — never left stale/hand-computed:
  ```bash
  python .kiro/skills/automation-testing/scripts/device_results.py rollup qa-automation-testing
  ```
  Then rebuild the reports (Stage 6 engine). The per-device sheets / "By Device"
  summary will now include every device recorded (old + newly added).

## Pick a baseline + diff
Run snapshots make baselines explicit. Default baseline = the latest snapshot.
```bash
python .kiro/skills/automation-testing/scripts/runs.py list qa-automation-testing
python .kiro/skills/automation-testing/scripts/runs.py baseline qa-automation-testing
python .kiro/skills/automation-testing/scripts/regression_diff.py \
    qa-automation-testing/results/runs/<baseline_ts>/data/test_cases.json \
    qa-automation-testing/data/test_cases.json
```
Report the delta over the common ids: cases **newly into Fail** and **newly into
Pass**. Then rebuild the reports (Stage 6 engine) and snapshot this run as the new
baseline.

> **Run vs document version:** every execution is a new run snapshot (compare
> against a prior snapshot, not a version bump); `report_meta.version` is a semantic
> version bumped only when code/scope meaningfully changes.

## Final Gate (terminal — do NOT auto-continue)
This is the **last stage**. Do **not** offer "Next" and do **not** start another
cycle automatically. Summarize the regression delta (newly-failing / newly-passing,
device(s) run, reports rebuilt, backup location), then **stop and let the user
decide**. Present the choices in the same tidy layout as every other gate
(blank line, bold question, then the two choices as a numbered list, **one per
line, never inline**) — but with **no "Next"** since this is terminal:

```markdown
**Regression reviewed — what next?**

1. **Change** — re-run / adjust the baseline and I'll re-present this gate
2. **Done** — finish this cycle (I'll stop here)
```

Then add a short **"How to re-test later"** note so the user knows how to start again
on demand:
> To re-test later: run **`rerun`** for a full regression against the latest
> baseline, or **`retest ui` / `retest api` / `retest unit`** to re-run just one
> layer. The previous reports are already backed up under `Backup/<timestamp>/`.

Do not proceed past this point without an explicit user request.

> Full snapshot/diff/parity commands: `../automation-testing/reference/CLI_REFERENCE.md`.
