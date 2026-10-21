---
name: automation-testing-validate
description: "Stage 4 (Validate One) of the QA Automation Testing skill library. Activated by the automation-testing router (or the 'test one' command) after Generate. Sets up the chosen UI driver — reusing the project's existing e2e tool (Cypress/WebdriverIO/Selenium/Puppeteer) or the bundled Playwright default config + helpers — runs a health check on the target, then executes EXACTLY ONE test case to prove the capture setup, producing one screenshot + slow-motion video, auto-fixing a flaky script up to 3 attempts, and summarizes the trial. Node is optional (only when the driver/health-check needs it). Hands off to automation-testing-scale. Use to validate a single test and the evidence-capture setup before scaling the whole suite."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 4 of 7
  keywords: [validate, single-case, health-check, driver-setup, screenshot, video]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 4 — Validate One (single-case capture proof)

**Purpose:** a deliberate single-case checkpoint that proves the driver config,
helpers, and evidence capture work before scaling. **Reply in the user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-validate`. Shared engine + docs:
sibling hub `../automation-testing/`; UI driver assets: `../automation-testing-domain-ui/`.
*(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | `data/test_cases.json`; a reachable target; the chosen UI driver (`report_meta.tool`, for UI cases) |
| **Optional inputs** | credentials / test data for the health check |
| **Reads** | `data/test_cases.json`, the active domain skill, driver templates (`../automation-testing-domain-ui/assets/templates/` for the default) |
| **Writes** | driver config + helpers into the target; one case's screenshot + video under `results/`; `report_meta.environment_health` |
| **Depends on** | a UI e2e driver for UI cases; **Node only** if that driver / the JS health check is Node-based |
| **Next** | `automation-testing-scale` (Stage 5) |

## Which UI driver? (tool-agnostic — consult the UI domain)
For UI cases, follow the **`automation-testing-domain-ui`** skill (the capability that
owns UI driver + evidence details). Use the **driver chosen in Analyze §1c**
(`report_meta.tool`): **reuse the project's existing e2e tool** (Cypress / WebdriverIO
/ Selenium / Puppeteer / an existing Playwright setup) if it has one; only when the
project has **no** UI e2e tooling do you fall back to the **bundled Playwright default
reference driver** and its templates below. The setup mechanics differ per tool, but
the **evidence contract is identical** (screenshot + video, and later the
per-device/per-flow full videos) so downstream reports never change.

*(API / Unit-Integration layers have no browser setup — follow
`automation-testing-domain-api` / `-domain-unit` instead; Node is not needed there
unless that domain's client/runner is Node-based.)*

**Node is optional.** It is needed only when the chosen driver (or the JS health
check) is Node-based. A project with **no UI layer** (API/Unit only), or a UI driver
that isn't Node-based, needs no Node — skip the JS templates and use that driver's
native runner instead.

## Setup (bundled Playwright default — first run only)
When using the default driver, copy the bundled capture templates (owned by the UI
domain) into the target, stripping the `.tpl` suffix:
- `../automation-testing-domain-ui/assets/templates/playwright.config.tpl.js` → `<target>/playwright.config.js`
- `_helpers.tpl.js` and `_teardown.tpl.js` (same folder) → `<target>/qa-automation-testing/scripts/`

*(For a reused non-Playwright driver, configure that tool to emit the same screenshot
+ video evidence into the same `qa-automation-testing/results/` paths instead.)*

**ESM compatibility (Playwright default):** the templates use ES Modules. If
`package.json` has `"type": "module"`, do nothing; else add it, or (for CommonJS
projects) rename the copied templates + generated specs to `.mjs`. Inform the user if
`package.json` changed.

## Health check (before running)
Confirm the target is reachable before running. With the Node-based default, copy
`../automation-testing-domain-ui/assets/templates/_health_check.tpl.js` → `<target>/qa-automation-testing/scripts/_health_check.js`
and run `node _health_check.js <base_url> [timeout_ms]` (exit 0 = healthy). For a
non-Node stack, use an equivalent reachability probe (`curl`, the driver's own
runner, or a language-native request) — the checks below are the same regardless:
- **UI (URL):** GET base URL, require 2xx/3xx (not an error page); confirm login
  endpoint if auth is used.
- **API:** GET `/health` `/api/health` `/status` `/ping` (or the API base); require
  a response within 10s.
- **Unit/Integration:** verify the runner is installed and dependencies present.

**On failure:** report the specific error, do NOT proceed, ask the user to fix /
supply a correct URL / type 'skip'; record under `report_meta.environment_health`.
**On success:** log the pass (with response time) and proceed.

## Run exactly ONE case
Write the config + helpers + one spec, run **one** Test_Case at a single viewport
(e.g. Playwright `--project=desktop`) to produce its **screenshot + video**. Auto-fix
a flaky script (selector/timing/assertion) up to **3** attempts, then stop and record
the outcome.

- Sequential (one worker, no parallelism), in slow-motion (Playwright:
  `launchOptions.slowMo`, default 1000ms) with video recording on; capture a
  full-page screenshot after the case (`_helpers.js` `afterEach` for the default
  driver); use a 2× scale + scroll/animation-freeze for crisp, complete shots and
  re-capture once if a shot is blank/distorted, with a warning.
- *(A reused non-Playwright driver uses its own equivalents — single-worker run,
  slow playback, video on, full-page screenshot — writing to the same result paths.)*

## Approval Gate
**Summarize the trial:** what ran, pass/fail, the screenshot + video paths, and any
problems (selector, timing, a re-captured shot). Then present the gate in the
canonical tidy layout (see router "Approval Gate rule" — blank line, bold
question, then the two choices as a numbered list, **one per line, never inline**):

```markdown
**Setup/evidence OK?**

1. **Next** — go to Stage 5 (Scale Suite): run the full set across the chosen devices
2. **Change** — tell me what to fix and I'll re-run one case and re-present
```

- **Change** → fix setup/spec, re-run one case, re-present this gate.
- **Next** → hand off to **`automation-testing-scale`** (Stage 5) via the router.
