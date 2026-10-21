# Skill Development Standard — automation-testing

The house standard for developing and reviewing skills in this library. It captures
the principles the current design follows so future work stays consistent. Pair it
with `DOMAIN_CONTRACT.md` (the concrete template for adding a test type).

## Architecture at a glance

Two axes + one shared library:

- **Lifecycle stages** (`-intake … -report … -rerun` + `-evaluate` helper) — the
  *process*. Generic, no hard-coded test type. Internal to one pipeline.
- **Test domains** (`-domain-ui` / `-domain-api` / `-domain-unit`) — the
  *capabilities*. Each owns one test type and is **addressable** (standalone +
  `retest <domain>`).
- **Shared Reporting_Engine** (`scripts/`) — one copy in the hub, never forked
  (guarantees report parity / no drift); the report layout + fallback prose are built
  into the engine.

Modularity follows **reuse boundaries**: split a thing into its own skill only when
it is independently reusable (domains) or an independently dispatchable stage.
Do **not** split shared executable code — centralize it.

## The 10 principles (review checklist)

1. **Single responsibility** — one skill/module does one coherent thing.
2. **Progressive disclosure** — the entry (`SKILL.md`) stays short; heavy detail is
   in referenced files loaded only when needed (keeps agent context lean).
3. **Rich, honest front-matter** — `name`, `description` (with trigger keywords),
   `license`, and `metadata` (`version`, `type`, `stage`/`activated_by`, `keywords`,
   `supported_platforms`).
4. **Information Contract** — every skill states Required/Optional **inputs**,
   **Reads**, **Writes**, **Depends on**, and **Next** in a table near the top.
5. **Deterministic + tool-agnostic execution** — heavy lifting lives in `scripts/`
   (reproducible); instructions orchestrate, never hand-craft outputs.
6. **Standalone + composable** — each skill works alone (via a direct command) and
   composes through the router. Domains expose a "Standalone re-test".
7. **Idempotent / re-runnable** — safe to re-run; state lives in files
   (`analysis.md`, `test_cases.json`, `results/runs/<ts>/` snapshots), not memory.
8. **Testable** — every capability is verifiable from fixtures: build reports from a
   sample `test_cases.json`, assert with `parity.py`.
9. **Portable** — relative paths + an Environment Detection block
   (`.kiro`/`.claude`/`.cursor`/`.windsurf`); Python always, **Node optional** (only
   Node-based UI drivers/health checks).
10. **Versioned + documented** — semver in front-matter; align QA content with the
    referenced standards (ISO/IEC/IEEE 29119, ISTQB, IEEE 829).

## SKILL.md required sections (every stage/domain/helper)

```
front-matter (name, description, license, metadata)
# Title + one-line Purpose (+ "Reply in the user's language")
## Environment Detection            # platform → SKILL_DIR; hub at ../automation-testing/
## Information Contract              # Required/Optional inputs · Reads · Writes · Depends · Next
## <the work>                       # steps / rules specific to this unit
## Approval Gate                    # stages only — summarize → Change / Next (helper/domain: none)
## Skill Handoff / Standalone use    # where control goes next (+ fallback if next skill missing)
```

## How to add a new TEST TYPE (domain)
Follow `DOMAIN_CONTRACT.md`: create `automation-testing-domain-<x>/SKILL.md`, fill the
8 required sections, map to `report_model.TEST_TYPES` (extend the engine + `labels.py`
only if a new report section is genuinely needed), add a row to the router Capability
Registry + the READMEs' "What it tests" matrix, and add a `retest <x>` command. The
`automation-testing*` glob in `install.sh` ships it automatically.

## How to add a new STAGE (rare)
Only if a genuinely new pipeline step is needed. Add
`automation-testing-<stage>/SKILL.md`, insert it in the canonical order + the router
routing table + gate chain, and update `scripts/gate.py` STAGES. Keep the seven-stage
default unless there is a strong reason.

## Verification gate (run before every handoff / release)
```bash
# 1. engine compiles
python -m py_compile .kiro/skills/automation-testing/scripts/*.py
# 2. reports build from a sample and agree (parity) — use a temp qa dir with data/test_cases.json
python .kiro/skills/automation-testing/scripts/build_reports.py <qa_dir>
python .kiro/skills/automation-testing/scripts/parity.py <qa_dir>/data/test_cases.json
# 3. front-matter of every SKILL.md parses as YAML
# 4. no stale cross-references; clean up __pycache__ and temp fixtures
```
Output (xlsx/docx/html) must be **unchanged** by any refactor that is not explicitly a
report change. If it changes a report, update `FIELD_REFERENCE.md` too.

## Anti-patterns (do not do)
- Forking `scripts/` (or any shared code) into a domain/stage → drift, breaks parity.
- Putting a domain's private asset in the hub (e.g. Playwright templates belong to
  `-domain-ui`).
- Nesting a `SKILL.md` inside another skill's folder (it won't register as a skill).
- Hand-crafting reports or calling external `xlsx`/`docx` skills.
- Restructuring a working, tested layout for cosmetic reasons.
