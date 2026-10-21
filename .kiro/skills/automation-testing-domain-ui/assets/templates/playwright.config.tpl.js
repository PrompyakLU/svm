// playwright.config.tpl.js
//
// Bundled template. The QA automation skill orchestrator copies this file to
// <target>/playwright.config.js during Validate One / Scale Suite.
//
// Capture contract:
//   - workers: 1 + fullyParallel: false  -> one Test_Case runs at a time, so each
//     screenshot/video maps 1:1 to a single TC (no overwrites). Specs are run in
//     TC-id order; the orchestrator announces per-layer progress.
//   - launchOptions.slowMo                -> observable, slow-motion actions in the video
//                                            (default 1000ms so the recording is easy to follow).
//   - video (size = viewport, per project)-> records BOTH passing and failing runs at the
//                                            EXACT viewport size, so each recording is true-to-
//                                            device screen size with no scaling/letterboxing
//                                            (no distortion / stretching / squashing).
//   - projects (desktop/tablet/mobile)    -> RESPONSIVE: every UI spec runs at three
//                                            viewport sizes; screenshots/videos are
//                                            namespaced per project so they never collide.
//   - deviceScaleFactor: 2                -> crisp, true-to-system captures (not blurry/distorted).
//   - expect.toHaveScreenshot             -> PIXEL-VARIANCE visual comparison defaults
//                                            (small rendering noise tolerated, real changes fail).
//   - globalTeardown                      -> renames raw .webm files to TC ids after the run.
import { defineConfig } from '@playwright/test';

// slowMo (ms) is overridable via env; defaults to 1500ms so the recorded video is
// slow and easy to follow (feedback was the video ran too fast to watch). Raise
// QA_SLOW_MO for an even slower walkthrough (e.g. 2000–2500) on demos/handovers.
const SLOW_MO = Number(process.env.QA_SLOW_MO ?? 1500);

// All known responsive viewports.
const ALL_VIEWPORTS = {
  desktop: { width: 1440, height: 900 },
  tablet: { width: 768, height: 1024 },
  mobile: { width: 390, height: 844 },
};

// DEVICE SELECTION is DETERMINISTIC and SAFE-BY-DEFAULT.
// Only the device(s) listed in QA_DEVICES become Playwright projects, so even a
// bare `npx playwright test` (no --project) runs ONLY the chosen device(s) — it
// can never silently run tablet/mobile and produce unwanted portrait videos.
// Default = "desktop" (the most common single-device run). To run more, set e.g.
//   QA_DEVICES="desktop,tablet,mobile"   (the skill sets this from the user's pick).
const SELECTED_DEVICES = (process.env.QA_DEVICES ?? 'desktop')
  .split(',').map((s) => s.trim().toLowerCase()).filter(Boolean);
const VIEWPORTS = Object.fromEntries(
  Object.entries(ALL_VIEWPORTS).filter(([name]) => SELECTED_DEVICES.includes(name)),
);
// Guard: if QA_DEVICES was set to something unrecognised, fall back to desktop so
// there is always at least one valid project (never an empty run).
if (Object.keys(VIEWPORTS).length === 0) {
  VIEWPORTS.desktop = ALL_VIEWPORTS.desktop;
}

// CROSS-BROWSER (optional axis). Default = chromium only (fast, no extra installs).
// Opt in to more browsers via QA_BROWSERS, e.g. QA_BROWSERS="chromium,chrome,msedge".
// Supported: chromium (bundled) · chrome / msedge (real channels, must be installed)
// · firefox · webkit (Safari engine). Each browser × each viewport becomes a
// project, so the report records the full browser × device test matrix.
const BROWSERS = (process.env.QA_BROWSERS ?? 'chromium')
  .split(',').map((s) => s.trim().toLowerCase()).filter(Boolean);

// Map a friendly browser name to Playwright launch settings.
function browserUse(browser) {
  if (browser === 'chrome') return { browserName: 'chromium', channel: 'chrome' };
  if (browser === 'msedge' || browser === 'edge') return { browserName: 'chromium', channel: 'msedge' };
  if (browser === 'firefox') return { browserName: 'firefox' };
  if (browser === 'webkit' || browser === 'safari') return { browserName: 'webkit' };
  return { browserName: 'chromium' }; // chromium (bundled) — the default
}

// Build the project matrix: browser × viewport. With a single browser (default)
// the project names stay desktop/tablet/mobile so evidence paths + report device
// sheets are unchanged. With multiple browsers they become "{browser}-{device}"
// so screenshots/videos never collide and the report shows each combination.
const SINGLE_BROWSER = BROWSERS.length <= 1;
const PROJECTS = [];
for (const b of BROWSERS) {
  for (const [device, vp] of Object.entries(VIEWPORTS)) {
    PROJECTS.push({
      name: SINGLE_BROWSER ? device : `${b}-${device}`,
      use: { ...browserUse(b), viewport: vp, video: { mode: 'on', size: vp } },
    });
  }
}

export default defineConfig({
  // Generated specs are written here by the orchestrator.
  testDir: './qa-automation-testing/scripts',

  // Timeouts guard against tests that hang and never finish. UI tests with
  // slowMo + navigation can exceed Playwright's 30s default, so a per-test
  // budget of 60s is a safe starting point; raise via config if a suite needs it.
  timeout: 60_000,         // 60s per test case
  globalTimeout: 600_000,  // 10 min for the whole suite

  // Raw Playwright artifacts (including the per-test .webm) land here; _teardown.js
  // reads the per-test directories from .video-map.json and renames the videos.
  outputDir: './qa-automation-testing/results/.pw-artifacts',

  // Stored visual baselines for pixel-variance comparison (toHaveScreenshot).
  snapshotDir: './qa-automation-testing/results/visual-baselines',

  // Sequential execution guarantees a 1:1 TC -> artifact mapping and a stable,
  // announceable run order (by TC id).
  fullyParallel: false,
  workers: 1,

  // Runs once after the whole suite: copy/rename raw .webm -> videos/{pass|fail}/{TC id}.webm.
  // The orchestrator additionally assembles the per-device full-run video and the
  // full end-to-end PROCESS videos (positive & negative) by sequencing the per-case
  // clips in TC-id order (see automation-testing-scale "Evidence & responsive capture").
  globalTeardown: './qa-automation-testing/scripts/_teardown.js',

  // Pixel-variance visual-comparison defaults. Generated UI specs may assert
  // `await expect(page).toHaveScreenshot()`; these thresholds decide pass/fail:
  // up to 2% of pixels may differ, with a small per-pixel color tolerance, so
  // anti-aliasing noise passes while genuine visual regressions fail.
  expect: {
    timeout: 10_000,       // 10s for assertions (e.g. expect(...).toBeVisible())
    toHaveScreenshot: {
      maxDiffPixelRatio: 0.02,
      threshold: 0.2,
      animations: 'disabled',
    },
  },

  reporter: [
    ['list'],
    ['html', { outputFolder: './qa-automation-testing/results/html-report', open: 'never' }],
  ],

  use: {
    // Slow-motion makes interactions visible in the recorded video.
    launchOptions: { slowMo: SLOW_MO },

    // Video recording is deliberately NOT configured at this global level.
    // Reason: the string shorthand `video: 'on'` makes Playwright pick a DEFAULT
    // size ("viewport scaled down to fit 800×800"), whose aspect ratio can differ
    // from the real viewport and produce grey letterbox/pillarbox bars in the
    // recording. Instead, EACH project (below) sets the OBJECT form
    // `video: { mode: 'on', size: <that project's viewport> }`, so every recording
    // is captured at the EXACT device viewport with no scaling/letterboxing.
    // (Do NOT re-add `video: 'on'` here — it re-introduces the grey-bar bug.)

    // Screenshots are taken explicitly per-TC in _helpers.js afterEach,
    // so disable Playwright's automatic screenshot capture to avoid duplicates.
    screenshot: 'off',

    // Capture at 2x so screenshots are crisp and match the real system (avoids the
    // blurry/distorted look reported on 1x captures).
    deviceScaleFactor: 2,
  },

  // RESPONSIVE × CROSS-BROWSER: one project per (browser × viewport). Each project
  // records its video at EXACTLY its own viewport size (true-to-device, never
  // stretched). The capture fixture namespaces screenshots/videos by project so
  // nothing overwrites. Default is chromium × 3 viewports (names: desktop/tablet/
  // mobile); adding browsers via QA_BROWSERS expands the matrix.
  projects: PROJECTS,
});
