# CLI Reference

Command-line invocations for the bundled Reporting_Engine scripts, split out of
`SKILL.md` to keep the orchestrator flow short. Always call these by
**skill-relative path**; never call the standalone `xlsx` / `docx` skills — the
engine in this folder is the only writer, which is what keeps the formats in
agreement (one model → writers: XLSX, DOCX, HTML).

Layout: the model + metadata live in `qa-automation-testing/data/`; all reports
are written into `qa-automation-testing/doc/` (per-device workbooks under
`doc/by-device/`).

Prerequisites: `pip install -r requirements.txt` (openpyxl + python-docx).

## Report writers

```bash
# CANONICAL — one command builds the ENTIRE doc/ tree, identically on ANY tool.
# Reads data/test_cases.json (+ optional data/report_meta.json) and writes the
# single test_cases.xlsx (with per-device sheets), the docx, and the API html —
# all under doc/. Every tool that runs this produces the same output.
python .kiro/skills/automation-testing/scripts/build_reports.py qa-automation-testing
```

The individual writers below give finer control but produce the **same** output
as the canonical command.

```bash
# Write the single XLSX workbook (Info → Summary → TestCases → per-type →
# per-device sheets → Traceability → DefectLog; Status in computed column R; a
# By Device table in Summary). The optional data/report_meta.json fills Info.
python .kiro/skills/automation-testing/scripts/report_xlsx.py \
    qa-automation-testing/data/test_cases.json \
    qa-automation-testing/doc/test_cases.xlsx \
    qa-automation-testing/data/report_meta.json   # optional

# Write the single consolidated DOCX Test Execution Report (summarizes all
# devices; includes a per-device pass/fail table when per-device results exist).
python .kiro/skills/automation-testing/scripts/report_docx.py \
    qa-automation-testing/data/test_cases.json \
    qa-automation-testing/doc/test_cases.docx \
    qa-automation-testing/data/report_meta.json   # optional

# (No HTML report — the deliverables are the XLSX workbook + the DOCX report.
#  Prefer the canonical `build_reports.py` above, which writes system-named files.)
```

## Run snapshots & regression diff

For the **Re-run/Regression** stage, compute the delta versus a **baseline** run.
At the **end of the Report stage**, snapshot the completed run into
`results/runs/<timestamp>/` (the snapshot copies the whole `data/` + `doc/`
trees); at Re-test, pick a baseline (default = the latest snapshot) and diff the
current `data/test_cases.json` against it.

```bash
# At the end of Report: snapshot this run (copies data/ + doc/ and writes a
# manifest with run_id / timestamp / doc version / pass-fail counts).
python .kiro/skills/automation-testing/scripts/runs.py snapshot qa-automation-testing

# At Re-test: list snapshots and resolve the latest baseline's test_cases.json.
python .kiro/skills/automation-testing/scripts/runs.py list qa-automation-testing
python .kiro/skills/automation-testing/scripts/runs.py baseline qa-automation-testing

# Diff current vs the chosen baseline (newly-failing / newly-passing over the
# common Test_Case ids). Baselines keep the model under their own data/ folder.
python .kiro/skills/automation-testing/scripts/regression_diff.py \
    qa-automation-testing/results/runs/<baseline_ts>/data/test_cases.json \
    qa-automation-testing/data/test_cases.json
```

## Per-device results: add-a-device merge + roll-up

```bash
# ADD mode (re-test): merge a separate new-run model into the main test_cases.json
# per Test_Case id — the new run's device(s) are added/updated while every device
# already recorded is preserved (never clobbered).
python .kiro/skills/automation-testing/scripts/device_results.py merge \
    qa-automation-testing <path/to/new_run_test_cases.json>

# ALWAYS finish a run/re-run with the roll-up: re-derive each case's top-level
# status/evidence as the worst-of across ALL devices present
# (Fail > Blocked > Skipped > Not Run > Pass). Idempotent; preserves all devices.
python .kiro/skills/automation-testing/scripts/device_results.py rollup qa-automation-testing
```

## Report parity check

```bash
# Assert the XLSX and DOCX reports agree on id + status for every Test_Case.
python .kiro/skills/automation-testing/scripts/parity.py \
    qa-automation-testing/data/test_cases.json
```
