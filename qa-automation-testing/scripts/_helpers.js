// _helpers.tpl.js
//
// Bundled template. The QA automation skill orchestrator copies this file to
// <target>/qa-automation-testing/scripts/_helpers.js. Generated specs import
// { test, expect } from this module instead of '@playwright/test'.
//
// afterEach contract:
//   1. Settle the page (best-effort networkidle wait).
//   2. Take ONE full-page screenshot, namespaced by viewport/project so the three
//      responsive runs never overwrite each other:
//        results/screenshots/{pass|fail}/{TC id}__{project}.png
//   3. VALIDATE the screenshot (exists + not blank/too-small). If it looks
//      distorted/empty, re-capture ONCE before giving up.
//   4. Append a video-map entry {tc, project, status, dir} so globalTeardown can
//      rename the raw .webm afterwards.
//
// IMPORTANT: never call video.saveAs() here. The video file is only finalized once
// the page/context closes, so calling saveAs() inside afterEach deadlocks. The raw
// .webm is renamed later by _teardown.js (globalTeardown).
import { test as base, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const RESULTS_DIR = path.resolve(process.cwd(), 'qa-automation-testing', 'results');
const DATA_DIR = path.resolve(process.cwd(), 'qa-automation-testing', 'data');
const SCREENSHOTS_DIR = path.join(RESULTS_DIR, 'screenshots');
const VIDEO_MAP_PATH = path.join(DATA_DIR, '.video-map.json');

// A screenshot smaller than this is treated as blank/distorted and re-captured.
const MIN_SCREENSHOT_BYTES = 2048;

// Derive a TC id from the test title (e.g. "TC_007 user can log in" -> "TC_007").
// Falls back to the raw title when no TC_<n> token is present.
function deriveTcId(title) {
  const match = title.match(/TC[_-][A-Za-z0-9]+/);
  return match ? match[0] : title;
}

// Derive the FLOW (positive / negative) from the test title so the teardown can
// assemble a full end-to-end POSITIVE journey video and a full NEGATIVE journey
// video per device. Tag generated spec titles with "[positive]" / "[negative]"
// (or the words positive/negative, or a @positive/@negative tag). Anything else
// is "other" and is still included in the per-device full_run video only.
function deriveFlow(title) {
  const t = String(title || '').toLowerCase();
  if (/\bneg(ative)?\b|\[negative\]|@negative/.test(t)) return 'negative';
  if (/\bpos(itive)?\b|\[positive\]|@positive/.test(t)) return 'positive';
  return 'other';
}

// A JOURNEY spec walks a whole flow end-to-end in ONE continuous test, so
// Playwright records it as a single native video — the full-process video, with
// NO ffmpeg needed. Tag its title with "[journey]" (plus [positive]/[negative]).
// The teardown saves this test's own recording straight to
//   results/videos/{device}/full_process__{device}__{flow}.webm
function isJourney(title) {
  return /\[journey\]|@journey/i.test(String(title || ''));
}

// Sanitize a project/viewport name for use in a filename.
function safeName(name) {
  return String(name || 'default').replace(/[^A-Za-z0-9_-]+/g, '_');
}

// Append an entry to the shared video map, tolerating a missing/corrupt file.
function appendVideoMapEntry(entry) {
  let map = [];
  try {
    const parsed = JSON.parse(fs.readFileSync(VIDEO_MAP_PATH, 'utf8'));
    if (Array.isArray(parsed)) map = parsed;
  } catch {
    map = [];
  }
  map.push(entry);
  fs.mkdirSync(path.dirname(VIDEO_MAP_PATH), { recursive: true });
  fs.writeFileSync(VIDEO_MAP_PATH, JSON.stringify(map, null, 2));
}

// Prepare the page so a full-page screenshot is COMPLETE and stable:
//   1. Scroll through the whole page to trigger lazy-loaded images/content, then
//      return to the top.
//   2. Wait for web fonts to finish loading so text is not missing/re-rendered.
//   3. Wait for any images in the DOM to finish decoding.
// All steps are best-effort and never throw.
async function settleForCapture(page) {
  await page
    .evaluate(async () => {
      // Force INSTANT scrolling for the whole capture routine. If the site uses
      // CSS `scroll-behavior: smooth`, window.scrollTo animates and a screenshot
      // taken right after can land mid-scroll → content offset/overlap ("distorted"
      // shots). Pinning scroll-behavior to 'auto' guarantees scrollTo(0,0) truly
      // reaches the top before we capture. The style is restored afterwards.
      const rootEl = document.documentElement;
      const bodyEl = document.body;
      const prevRoot = rootEl && rootEl.style.scrollBehavior;
      const prevBody = bodyEl && bodyEl.style.scrollBehavior;
      if (rootEl) rootEl.style.scrollBehavior = 'auto';
      if (bodyEl) bodyEl.style.scrollBehavior = 'auto';

      // Scroll through the whole page to trigger lazy-loaded images/content.
      await new Promise((resolve) => {
        const step = Math.max(window.innerHeight, 200);
        let y = 0;
        const timer = setInterval(() => {
          window.scrollBy(0, step);
          y += step;
          if (y >= document.body.scrollHeight) {
            clearInterval(timer);
            resolve();
          }
        }, 80);
      });

      // Fonts ready so glyphs are not blank / swapped mid-capture.
      if (document.fonts && document.fonts.ready) {
        await document.fonts.ready;
      }
      // Let in-view images finish decoding.
      await Promise.all(
        Array.from(document.images)
          .filter((img) => !img.complete)
          .map((img) => img.decode().catch(() => {})),
      );

      // Return to the very top and confirm we actually landed there (instant).
      window.scrollTo(0, 0);
      await new Promise((r) => requestAnimationFrame(() => r()));
      window.scrollTo(0, 0);

      // Restore the site's original scroll-behavior so we don't alter its runtime.
      if (rootEl) rootEl.style.scrollBehavior = prevRoot || '';
      if (bodyEl) bodyEl.style.scrollBehavior = prevBody || '';
    })
    .catch(() => {});
  await page.waitForTimeout(200).catch(() => {});
}

// Take a full-page screenshot and verify it is non-blank; re-capture once if the
// file is missing or suspiciously small (a sign of a distorted/empty capture).
// `animations: 'disabled'` freezes CSS animations/transitions so nothing is
// captured mid-frame (a common cause of distorted-looking shots).
async function captureAndValidate(page, filePath) {
  for (let attempt = 1; attempt <= 2; attempt += 1) {
    await settleForCapture(page);
    await page
      .screenshot({ path: filePath, fullPage: true, animations: 'disabled' })
      .catch(() => {});
    try {
      const { size } = fs.statSync(filePath);
      if (size >= MIN_SCREENSHOT_BYTES) return { ok: true, size };
    } catch {
      // file missing — fall through to retry
    }
    // Settle briefly and retry once.
    await page.waitForTimeout(300).catch(() => {});
  }
  let size = 0;
  try {
    size = fs.statSync(filePath).size;
  } catch {
    size = 0;
  }
  return { ok: size >= MIN_SCREENSHOT_BYTES, size };
}

// Extend the Playwright base test with an AUTOMATIC fixture that captures the
// evidence after every test. CRITICAL: this must be an `{ auto: true }` fixture,
// NOT a module-level `test.afterEach(...)`. A `test.afterEach` registered inside a
// shared imported module attaches only to the FIRST spec file that imports the
// module (Playwright scopes the hook to the file loading at import time), so every
// other spec file would silently capture NO screenshot/video. An auto fixture runs
// for every test in every file that uses this `test`.
export const test = base.extend({
  _qaCapture: [
    async ({ page }, use) => {
      // Run the test body first.
      await use();

      // --- Teardown: capture evidence for the test that just ran ---
      const testInfo = test.info();
      // Best-effort: let in-flight requests settle so the screenshot is representative.
      await page.waitForLoadState('networkidle').catch(() => {});
      await _captureEvidence(page, testInfo);
    },
    { auto: true },
  ],
});

async function _captureEvidence(page, testInfo) {
  const tc = deriveTcId(testInfo.title);
  const project = safeName(testInfo.project && testInfo.project.name);
  const status = testInfo.status === 'passed' ? 'pass' : 'fail';
  const flow = deriveFlow(testInfo.title);
  const journey = isJourney(testInfo.title);

  // One full-page screenshot per executed Test_Case, namespaced by viewport so the
  // desktop/tablet/mobile runs do not overwrite each other.
  const shotDir = path.join(SCREENSHOTS_DIR, status);
  fs.mkdirSync(shotDir, { recursive: true });
  const shotPath = path.join(shotDir, `${tc}__${project}.png`);
  const result = await captureAndValidate(page, shotPath);

  // Surface a validation problem in the test output so the orchestrator can flag /
  // re-test it; never throw (the run must finish and rename its video).
  if (!result.ok) {
    // eslint-disable-next-line no-console
    console.warn(
      `[qa-capture] screenshot for ${tc} (${project}) looks blank/too small ` +
        `(${result.size} bytes) at ${shotPath} — consider a re-test.`,
    );
  }

  // Record only — the actual .webm rename happens in globalTeardown (_teardown.js).
  appendVideoMapEntry({ tc, project, status, flow, journey, dir: testInfo.outputDir });
}

export { expect };
