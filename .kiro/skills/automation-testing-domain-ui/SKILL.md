---
name: automation-testing-domain-ui
description: "UI test DOMAIN (capability, not a lifecycle stage) of the QA Automation Testing skill library. Owns UI-E2E and UI-Functional testing: how UI cases are detected, generated, executed (tool-agnostic driver — reuse Cypress/WebdriverIO/Selenium/Puppeteer, else bundled Playwright default), and evidenced (screenshot per case, per-case video, per-device full-run + full positive/negative process videos). The Analyze/Generate/Validate/Scale stages consult this domain when the UI layer is active; it also runs standalone ('test the UI of this app'). Owns the bundled Playwright capture templates in its own assets/. Node is optional (only when the driver/health-check is Node-based)."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: test-domain
  test_types: [UI-E2E, UI-Functional]
  keywords: [ui, e2e, browser, responsive, playwright, cypress, webdriverio, selenium, puppeteer, screenshot, video]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Test Domain — UI (E2E + Functional)

**Role:** the authority for *what and how* UI testing works in this library. The
lifecycle stages (Analyze / Generate / Validate / Scale) stay generic and **consult
this domain** whenever the UI layer is active. It can also run **standalone**.
**Reply in the user's language.**

> This is a **capability skill, not a gated stage.** It adds no gate of its own; the
> stage that consulted it owns the Approval Gate.

## Environment Detection
Detect the platform and set `SKILL_DIR` so paths resolve on any tool:
- `.kiro/` → Kiro (`SKILL_DIR=.kiro/skills/automation-testing-domain-ui`)
- `.claude/` → Claude Code · `.cursor/` → Cursor · `.windsurf/` → Windsurf
  (`SKILL_DIR={platform}/skills/automation-testing-domain-ui`)
Shared library lives at the sibling hub `../automation-testing/` (scripts + docs).
`ASSETS_DIR={SKILL_DIR}/assets` holds this domain's own capture templates.

## Detects when (→ `../automation-testing/scripts/scope.py` markers)
`index.html` · `package.json` with react/vue/svelte/next/angular/astro/solid/qwik/remix ·
`*.{jsx,tsx,vue}` · `app/routes` · or a **live URL** to explore. Absent an in-project
app, UI is tested against the live URL (External_URL_Mode).

## Two test axes: responsive (devices) × cross-browser
- **Responsive / by-device (default, always on):** every UI spec runs at 3 viewports
  — desktop 1440×900 / tablet 768×1024 / mobile 390×844 — via Playwright projects.
  This is the automated equivalent of DevTools "responsive inspect", but it also
  captures a screenshot + video per device. *(Playwright Inspector / DevTools are
  manual debug tools — they are NOT part of the automated report flow.)*
- **Cross-browser (optional axis):** default is **Chromium only** (fast, no extra
  installs). Opt in with `QA_BROWSERS` (e.g. `"chromium,chrome,msedge"`; also
  `firefox`, `webkit`). Each browser × viewport becomes a project. Enable only when
  compatibility across browsers is a requirement — it multiplies run time.
- **Recorded in the reports:** the actual browser(s) + the device/viewport matrix
  tested are written to the XLSX **Info** sheet and the DOCX **Test Environment**
  section, so the report states exactly what was covered.

## Test types owned (→ `report_model.TEST_TYPES`)
- **UI-E2E** — full user journeys, both positive (happy-path) and negative (error) flows
- **UI-Functional** — single-screen / component behavior checks

## Information Contract
| | |
|---|---|
| **Reads** | `data/analysis.md` (UI coverage plan), the app source or live URL, chosen driver (`report_meta.tool`) |
| **Writes** | UI rows into `data/test_cases.json` (`test_type` = `UI-E2E` / `UI-Functional`); evidence under `results/` |
| **Depends on** | a UI e2e driver (see below); Node **only** if that driver / the JS health check is Node-based |
| **Owns** | the bundled Playwright capture templates at `{ASSETS_DIR}/templates/` |

## Which driver? (tool-agnostic)
Reuse the project's existing e2e driver — **Cypress / WebdriverIO / Selenium /
Puppeteer / an existing Playwright** setup — detected by
`scope.detect_ui_driver(workspace)`. Only when the project has **no** UI tooling, fall
back to the **bundled Playwright default** and copy this domain's templates
(`{ASSETS_DIR}/templates/*.tpl.js`, strip `.tpl`) into the target. Record the driver
that actually ran as `report_meta.tool`. **The evidence contract below is identical
regardless of driver**, so reports never depend on the tool.

## How to generate
- One full **E2E journey** per feature (positive) plus at least one **negative**
  journey (invalid input / error path); functional checks per screen/component.
- Tag each case `category` positive/negative; fill `steps`, `expected`,
  `preconditions`, `test_data`, and traceability (`user_story_id`,
  `source_reference`, `source_origin`).

## How to execute (driver-agnostic)
- Sequential, single worker, no parallelism (Playwright default `workers:1`,
  `fullyParallel:false`; other drivers use their equivalent), in TC-id order.
- **Slow-motion so the video is easy to follow** — Playwright default
  `launchOptions.slowMo = 1500ms` (override via `QA_SLOW_MO`, e.g. 2000–2500 for
  demos). Video on; run at **three responsive viewports** (desktop 1440×900 /
  tablet 768×1024 / mobile 390×844), one device fully before the next.
  Health-check the target first.
- **Tag each spec title with its flow** — `[positive]` for happy-path, `[negative]`
  for error/invalid — so evidence + `category` are grouped correctly.
- **Record the full end-to-end videos as dedicated JOURNEY specs (no ffmpeg).**
  In addition to the per-case specs, write **one continuous `[journey][positive]`
  spec** that walks the whole happy path (e.g. login → add to cart → checkout) and
  **one `[journey][negative]` spec** that walks the whole error path, each in a
  single test. Playwright records each as **one native video**, which the teardown
  saves directly to `full_process__{device}__{positive|negative}.webm` — a real
  full-journey video on every machine, **no ffmpeg required**. (ffmpeg is only a
  fallback for stitching, and only the whole-suite `full_run` video still needs it.)
- Auto-fix a flaky **script** ≤ 3 attempts; a genuine product defect goes to the
  DefectLog (never patch product code).

## Evidence contract (identical for any driver → `paths.py`)
- `results/screenshots/{pass,fail}/{TC}__{device}.png` (one per case per viewport)
- `results/videos/{device}/{pass,fail}/{TC}.webm` (per-case clip, recorded at true
  viewport size)
- `results/videos/{device}/full_run__{device}.webm` (whole run per device)
- `results/videos/{device}/full_process__{device}__positive.webm` and
  `…__negative.webm` — the full positive & negative end-to-end journeys per device,
  produced **natively from the `[journey]` specs (no ffmpeg)**.
- Per-device outcomes recorded under each case's `device_results` (top-level status =
  worst-of roll-up).

> **`ffmpeg` (optional):** the **full_process** videos need NO ffmpeg (they come
> straight from the `[journey]` specs' own recordings). ffmpeg is only used to
> stitch the whole-suite `full_run__{device}.webm` from the many per-case clips;
> without it, the per-case clips + the full_process journey videos are still all
> present (a non-fatal note is printed for full_run only).

## Pass/Fail criteria
A case passes when its actual matches expected across all tested devices (worst-of
roll-up). Visual comparison optional (baselines in `results/visual-baselines/`;
Playwright `toHaveScreenshot()` or the driver's own snapshot assertion).

## Report mapping (→ shared Reporting_Engine, unchanged)
UI cases render into the `UI-E2E` / `UI-Functional` sheets of `test_cases.xlsx`, the
DOCX §5 detailed results, and evidence in the DOCX Appendix. No engine change needed.

## Standalone use
"test the UI of this app / URL" → run generate → execute → evidence for the UI layer
only, then produce reports via the hub's `build_reports.py`.

## Standalone re-test (router command: `retest ui`)
Re-run **only** the UI layer against an existing suite — "Re-run scoped to UI":
1. **Reuse** `data/test_cases.json` (do NOT regenerate); select cases where
   `effective_test_type()` ∈ {`UI-E2E`, `UI-Functional`} (i.e. `qa_layer == UI`).
2. Ask which device(s) to re-run; re-run the health check; execute just those cases
   with the chosen driver, capturing the same evidence contract.
3. **Diff** the new results against the baseline snapshot for the UI cases
   (`regression_diff.py`), then rebuild reports (`build_reports.py`) and snapshot.
4. Requires an existing suite + a prior run; otherwise fall back to the normal flow.
Non-UI cases keep their previous status untouched (scoped, idempotent, no drift).

> Extending to a new test type? See `../automation-testing/reference/DOMAIN_CONTRACT.md`.
