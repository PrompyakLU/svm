---
name: automation-testing-scale
description: "Stage 5 (Scale Suite) of the QA Automation Testing skill library. Activated by the automation-testing router (or the 'scale' command) after Validate One. FIRST asks which responsive device(s) to run (desktop/tablet/mobile), then executes the full set of test cases sequentially in TC-id order (workers:1), one device fully before the next, announcing per-layer/per-case progress, auto-fixing flaky scripts up to 3 attempts each, capturing a screenshot + slow-motion video per case per device plus one full end-to-end video per device and full positive/negative end-to-end process videos per device, and recording per-device results. Hands off to automation-testing-report. Use to run the entire suite and collect evidence across devices."
license: Proprietary. LICENSE.txt has complete terms
metadata:
  version: 1.0.0
  type: lifecycle-stage
  stage: 5 of 7
  keywords: [scale, full-suite, responsive, devices, evidence, videos, auto-fix]
  supported_platforms: [kiro-ide, kiro-cli, claude-code, cursor, windsurf]
---

# Stage 5 — Scale Suite (run the full set)

**Purpose:** execute all Test_Cases and capture evidence across devices.
**Reply in the user's language.**

## Environment Detection
`.kiro/`→Kiro · `.claude/`→Claude Code · `.cursor/`→Cursor · `.windsurf/`→Windsurf →
`SKILL_DIR={platform}/skills/automation-testing-scale`. Shared engine + docs: sibling
hub `../automation-testing/`. *(Runs standalone; the router may also dispatch it.)*

## Information Contract
| | |
|---|---|
| **Required inputs** | `data/test_cases.json` + runnable specs; the device(s) to run (asked first) |
| **Optional inputs** | test data / credentials; visual baselines under `results/visual-baselines/` |
| **Reads** | `data/test_cases.json`, the specs, the active `automation-testing-domain-*` skill(s) |
| **Writes** | per-device results into `data/test_cases.json` (`device_results`) + evidence under `results/` (screenshots, per-case / full-run / per-flow videos); DefectLog entries |
| **Depends on** | the domain's runner/driver; **Node only** for Node-based UI drivers |
| **Next** | `automation-testing-report` (Stage 6) |

> **Per-layer execution + evidence specifics come from the active TEST DOMAIN(s)**
> recorded in `data/analysis.md`: `automation-testing-domain-ui` (browser driver,
> viewports, per-flow videos), `automation-testing-domain-api` (HTTP client, request/
> response capture — no browser), `automation-testing-domain-unit` (native runner
> output). The device/responsive rules below apply to the **UI** domain; API/Unit run
> once (not responsive).

## First: ASK which device(s) — never assume
Ask which responsive device(s) to run: **desktop (1440×900) / tablet (768×1024) /
mobile (390×844)**. The user may pick one, two, or all three. Then run the suite
**one device fully (all cases) before the next** — never all devices at once.

**Create the evidence folders for the CHOSEN device(s) only** (not a fixed set) —
right after the user picks them, so the tree matches what's actually run:
```bash
python .kiro/skills/automation-testing/scripts/paths.py qa-automation-testing devices desktop tablet mobile
```
(replace the trailing list with the selected device(s)). This makes
`results/videos/<device>/{pass,fail}` for each chosen device. The driver's teardown
also creates them at run time, so this is just to have them ready up-front.

**Record the chosen device(s) into `data/report_meta.json`** as `"devices": [ ... ]`
(exactly the selected set). Both reports read this so the Info sheet, the §3 /
scope prose, and the per-device tables reflect the real device matrix — even before
per-device results exist.

### Run ONLY the chosen device(s) — set QA_DEVICES (deterministic)
Device selection is enforced by the config: it builds a Playwright **project only
for each device in `QA_DEVICES`** (default `desktop`). So set `QA_DEVICES` to
exactly what the user picked — then even a bare run can never execute an unchosen
device (no stray tablet/mobile **portrait** videos that look "distorted" in a
landscape player). Each project records its video at exactly its own viewport, so
captures are never stretched or letter-boxed.
```bash
# only the devices the user picked (comma-separated); runs sequentially (workers:1)
QA_DEVICES="desktop" npx playwright test
# e.g. all three, one device fully before the next:
QA_DEVICES="desktop,tablet,mobile" npx playwright test
```
You may still add `--project=<device>` to run one at a time explicitly, but it is
no longer required to avoid unwanted devices — `QA_DEVICES` already scopes the run.

### Run ONE device per invocation, in order (mandatory)
Do **NOT** launch a single run covering every device — that does not guarantee
"one device fully before the next" and lets each device's `.video-map.json` /
per-device videos cross-contaminate. Instead, run the driver **once per selected
device**, filtered to that device's project, and let the run finish (its
globalTeardown builds that device's `full_run` + `full_process` videos and resets
the video map) **before** starting the next device.

For the bundled Playwright default, that is one invocation per chosen device, in
order — e.g. when the user picked all three:
```bash
npx playwright test --project=desktop   # run ALL cases on desktop, wait for it to finish
npx playwright test --project=tablet    # then ALL cases on tablet
npx playwright test --project=mobile    # then ALL cases on mobile
```
Only include the device(s) the user actually selected (one `--project=<device>`
run each). Each viewport records at its exact size (see the config: per-project
`video: { size: <viewport> }`), so recordings are true-to-device with **no grey
letterbox bars**. Announce the start/end of each device before moving to the next.
*(A reused non-Playwright driver uses its own per-device/tag filter to achieve the
same "one device fully before the next" ordering, writing to the same paths.)*

## Execution rules
- **Sequential, single worker, no parallelism** (Playwright default: `workers:1`,
  `fullyParallel:false`; other drivers use their equivalent), in **TC-id order**
  (and, for smoke/focused, in risk order Critical → High → Medium → Low) so each
  screenshot maps to one case in a stable, announceable order.
- Re-run the **health check** first (see Validate One).
- **Announce progress** per QA layer + per case in id order
  (e.g. "UI 3/12 · running TC_003 — Login"); for suites > 20 cases, surface an ETA
  every 5 cases.
- **Auto-fix flaky scripts ≤ 3 attempts** per case; record the outcome, don't loop.
- **Smoke early-termination:** if any Critical case fails, stop after the Critical
  batch, report at the gate, and ask "Continue with remaining cases or stop here?"

## Evidence & responsive capture (driver-neutral — same paths for any tool)
Whatever UI driver ran (`report_meta.tool` — a reused Cypress/WebdriverIO/Selenium/
Puppeteer setup, or the Playwright default), the **evidence written is identical**:
- Artifacts namespaced per viewport:
  `results/screenshots/{pass,fail}/{TC}__{device}.png`,
  `results/videos/{device}/{pass,fail}/{TC}.webm`.
- **Per-device full run video** — one continuous recording of that device's whole
  run: `results/videos/{device}/full_run__{device}.webm`.
- **Full end-to-end PROCESS videos (positive AND negative), per device** — in
  addition to the full run above, record one uninterrupted video of the complete
  **positive** (happy-path) journey and one of the complete **negative** (error /
  invalid-input) journey, so a reviewer can watch each path end-to-end:
  `results/videos/{device}/full_process__{device}__positive.webm` and
  `results/videos/{device}/full_process__{device}__negative.webm`
  (paths from `paths.full_process_video_path(flow, device)`; `flow` ∈
  `{positive, negative}`). Sequence the positive-tagged cases into the positive video
  and the negative-tagged cases into the negative video, in TC-id order.
- **True-to-device video:** record at exactly each viewport size (never one fixed
  size — avoids distortion).
- Record **each device's** status/actual/screenshot/video under that case's
  **`device_results`** map (top-level status = worst-of roll-up).
- Visual comparison: baselines in `results/visual-baselines/` (Playwright default:
  `toHaveScreenshot()`; other drivers use their own snapshot assertion).

## Persist results back to the model (MANDATORY — do this before Report)
After running, **write every case's outcome back into
`qa-automation-testing/data/test_cases.json`** — set each case's per-device
`device_results` (`status` + `actual` + `screenshot_path` + `video_path` per
device). A case that genuinely did not run stays `Not Run`. **Do not hand off to
Report while every case is still `Not Run`** — that means execution/persistence did
not happen, and the report would carry no real results. Verify the JSON reflects the
run (at least the executed cases are no longer `Not Run`) before the gate.

- **Additive across devices — never clobber a device you didn't run.** When this
  run covers only SOME of the devices already present (e.g. you ran tablet on a
  suite already run on desktop), update ONLY the executed device keys under
  `device_results` and preserve the rest. Do not overwrite the whole map.
- **Always finish with the roll-up** so each case's top-level `status` / evidence
  is the correct worst-of across ALL devices present (Fail > Blocked > Skipped >
  Not Run > Pass) rather than hand-computed:
  ```bash
  python .kiro/skills/automation-testing/scripts/device_results.py rollup qa-automation-testing
  ```
  This keeps the per-device sheets / "By Device" summary complete and the headline
  status consistent with the per-device results.

## Test data & cleanup (ISTQB TAE)
Create state in `beforeAll`/`beforeEach`, use `test_data`, clean up in `afterAll`;
isolate cases (unique `test_<TC>_<timestamp>` prefixes; idempotent). At the end of
Scale Suite (before Report), run generated teardown and report cleanup status
(a cleanup failure is a non-blocking warning).

## Defect duty
Auto-fix only *test scripts*. A genuine **product defect** is **not** fixed in
product code — log it to the DefectLog (assigned to the developer) and re-test after
the dev fixes it.

## Approval Gate
Summarize results (counts, pass/fail per layer/device, evidence produced, any flaky
retries or defects), then present the gate in the canonical tidy layout (see
router "Approval Gate rule" — blank line, bold question, then the two choices as
a numbered list, **one per line, never inline**):

```markdown
**Results/evidence OK?**

1. **Next** — go to Stage 6 (Report): build the XLSX + DOCX from these results
2. **Change** — tell me what to re-run or fix and I'll re-present
```

- **Change** → re-run/fix, re-present this gate.
- **Next** → hand off to **`automation-testing-report`** (Stage 6) via the router.
