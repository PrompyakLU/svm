# Test Domain Contract — how to add a new kind of testing

The QA Automation Testing library has **two axes**:

- **Lifecycle stages** (`automation-testing-intake … -report … -rerun`) — the *process*
  ("when / in what order"). Generic; they do not hard-code any test type.
- **Test domains** (`automation-testing-domain-*`) — the *capabilities*
  ("what we test / how, per type"). Each is one small skill.

A domain skill is a **capability, not a gated stage**: it adds no Approval Gate of its
own; the lifecycle stage that consults it owns the gate. This file is the template +
rules for adding a new domain (e.g. Performance, Security, Accessibility, Load,
Contract) **without editing the seven stages**.

## Where things live (do not violate)
- **Shared, must-not-drift code** (the Reporting_Engine: `report_model`, `report_xlsx`,
  `report_docx`, `parity`, `paths`, `scope`, …) stays in the hub
  `automation-testing/scripts/` — **one copy**. Never fork it into a domain.
- **Report layout + fallback prose** are built into the hub's engine (`scripts/`).
- **Domain-specific assets** (a driver config, a runner template, tool binaries config)
  live in **that domain's own** `assets/` — like `automation-testing-domain-ui/assets/`
  owns the Playwright capture templates. Only the domain that uses them holds them.

## Steps to add a domain
1. **Create the folder** `automation-testing-domain-<x>/` with a `SKILL.md`.
2. **Fill the required sections** (copy the skeleton below) — all 8 are mandatory:
   Environment Detection · Detects when · Test types owned · Information Contract ·
   How to generate · How to execute · Evidence contract · Pass/Fail criteria ·
   Report mapping · Standalone use.
3. **Map to the model.** If the new type fits an existing `report_model.TEST_TYPES`
   bucket, reuse it. If it needs a **new** bucket/section (e.g. Performance, Security),
   that is a **Reporting_Engine change** — extend `report_model.TEST_TYPES`,
   the relevant writer(s), and `labels.py` (th/en), then re-run `parity.py`. Note this
   as a follow-on; do not hand-craft reports.
4. **Register it** — add one row to the router's **Capability Registry** table
   (`automation-testing/SKILL.md`) and the **What it tests** matrix in the READMEs.
5. **Add markers** (optional) to `scripts/scope.py` so the layer auto-detects.
6. **Ship** — `install.sh` already copies every `automation-testing*` folder, so a new
   domain travels automatically. No installer edit needed.

## Skeleton (copy into `automation-testing-domain-<x>/SKILL.md`)
```markdown
---
name: automation-testing-domain-<x>
description: "<one-paragraph: what this domain tests, how it's detected/generated/
  executed/evidenced, that it's a capability consulted by the stages + standalone>."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: test-domain
  test_types: [<Type>]
  keywords: [<...>]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Test Domain — <X>

**Role:** authority for what/how <X> testing works; consulted by the stages + runs
standalone. Reply in the user's language. (Capability, not a gated stage.)

## Environment Detection
.kiro/ → Kiro · .claude/ → Claude Code · .cursor/ → Cursor · .windsurf/ → Windsurf.
SKILL_DIR={platform}/skills/automation-testing-domain-<x>. Hub: ../automation-testing/.

## Detects when     (→ scope.py markers)
## Test types owned (→ report_model.TEST_TYPES)
## Information Contract   (Reads / Writes / Depends on / Owns — as a table)
## How to generate
## How to execute
## Evidence contract      (paths from paths.py; identical regardless of tool)
## Pass/Fail criteria
## Report mapping         (which sheet/section; note engine changes if any)
## Standalone use

> Extending? See ../automation-testing/reference/DOMAIN_CONTRACT.md.
```

## Guardrails
- **Output must not change** for existing domains — the engine is shared; only add,
  never fork.
- **Node stays optional** — declare in the Information Contract whether the domain
  needs Node (only Node-based drivers/runners do). Python (the engine) is always
  required.
- **Evidence paths come from `paths.py`** — do not invent ad-hoc paths.
- **Genuine product defects → DefectLog**, never patched in product code (all domains).
