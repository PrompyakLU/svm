// _teardown.tpl.js
//
// Bundled template. The QA automation skill orchestrator copies this file to
// <target>/qa-automation-testing/scripts/_teardown.js. It is referenced as
// `globalTeardown` in playwright.config.js and runs once after the whole suite.
//
// Contract:
//   - Read data/.video-map.json (written per-test by _helpers.js afterEach).
//   - For each entry, find the raw .webm under its per-test output directory and
//     copy/rename it to results/videos/{device}/{pass|fail}/{TC id}.webm
//     (grouped per device/viewport so each device's recordings stay together).
//   - Then, for each device, join that device's per-Test_Case clips (in run
//     order) into a single FULL end-to-end run video:
//         results/videos/{device}/full_run__{device}.webm
//     and, from the positive-/negative-tagged clips, two FULL end-to-end PROCESS
//     videos per device (the whole happy path and the whole error path):
//         results/videos/{device}/full_process__{device}__positive.webm
//         results/videos/{device}/full_process__{device}__negative.webm
//     Joining uses ffmpeg when available; if ffmpeg is missing the per-TC clips
//     are still kept and a note is printed (non-fatal). The flow (positive/
//     negative) comes from each video-map entry (tagged by _helpers.js from the
//     spec title).
//
// This deferred rename avoids the video.saveAs() deadlock that occurs when trying
// to finalize a video before its page/context has closed.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const RESULTS_DIR = path.resolve(process.cwd(), 'qa-automation-testing', 'results');
const DATA_DIR = path.resolve(process.cwd(), 'qa-automation-testing', 'data');
const VIDEO_MAP_PATH = path.join(DATA_DIR, '.video-map.json');
const VIDEOS_DIR = path.join(RESULTS_DIR, 'videos');

// Recursively locate the first .webm under a per-test output directory.
function findWebm(dir) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return null; // directory missing or unreadable
  }
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) {
      const nested = findWebm(full);
      if (nested) return nested;
    } else if (e.name.endsWith('.webm')) {
      return full;
    }
  }
  return null;
}

// Resolve an ffmpeg binary: PATH first, else the bundled copy under qa-automation-testing/bin.
function resolveFfmpeg() {
  const candidates = ['ffmpeg', path.resolve(process.cwd(), 'qa-automation-testing', 'bin', 'ffmpeg.exe')];
  for (const bin of candidates) {
    try {
      const r = spawnSync(bin, ['-version'], { stdio: 'ignore' });
      if (r.status === 0) return bin;
    } catch {
      // try next
    }
  }
  return null;
}
const FFMPEG_BIN = resolveFfmpeg();

// True when an `ffmpeg` binary is callable on this machine.
function hasFfmpeg() {
  return FFMPEG_BIN != null;
}

// Concatenate `clips` (absolute paths, in order) into `outPath` via ffmpeg's
// concat demuxer with stream copy (all clips share the Playwright webm codec).
// Returns true on success. Non-fatal: any failure just leaves the per-TC clips.
function concatWithFfmpeg(clips, outPath) {
  if (clips.length === 0) return false;
  const listPath = `${outPath}.concat.txt`;
  const listBody = clips.map((p) => `file '${p.replace(/'/g, "'\\''")}'`).join('\n');
  try {
    fs.writeFileSync(listPath, `${listBody}\n`);
    const r = spawnSync(
      FFMPEG_BIN || 'ffmpeg',
      ['-y', '-f', 'concat', '-safe', '0', '-i', listPath, '-c', 'copy', outPath],
      { stdio: 'ignore' },
    );
    fs.rmSync(listPath, { force: true });
    return r.status === 0 && fs.existsSync(outPath);
  } catch {
    try { fs.rmSync(listPath, { force: true }); } catch { /* ignore */ }
    return false;
  }
}

export default async function globalTeardown() {
  let map = [];
  try {
    const parsed = JSON.parse(fs.readFileSync(VIDEO_MAP_PATH, 'utf8'));
    if (Array.isArray(parsed)) map = parsed;
  } catch {
    return; // no map -> nothing to rename
  }

  // Per-device ordered list of clips for the full-run join, plus a per-device,
  // per-flow list (positive / negative) for stitching a full-process video when
  // no dedicated journey spec exists (ffmpeg fallback).
  const clipsByDevice = new Map();
  const clipsByDeviceFlow = new Map(); // key: `${device}::${flow}` -> [clips]
  // A JOURNEY test's OWN recording IS the full-process video (native, no ffmpeg).
  // key: `${device}::${flow}` -> the saved full_process path already written.
  const journeyDone = new Set();
  const devicesSeen = new Set();

  for (const entry of map) {
    if (!entry || !entry.dir || !entry.tc) continue;

    const status = entry.status === 'pass' ? 'pass' : 'fail';
    const device = entry.project || 'default';
    devicesSeen.add(device);
    const flow = entry.flow || 'other';
    const src = findWebm(entry.dir);
    if (!src) continue; // no recording for this TC

    // A journey spec (walks the whole flow in one continuous test) → its single
    // native recording becomes the full-process video directly. NO ffmpeg.
    if (entry.journey && (flow === 'positive' || flow === 'negative')) {
      fs.mkdirSync(path.join(VIDEOS_DIR, device), { recursive: true });
      const outPath = path.join(VIDEOS_DIR, device, `full_process__${device}__${flow}.webm`);
      try {
        fs.renameSync(src, outPath);
      } catch {
        fs.copyFileSync(src, outPath);
      }
      journeyDone.add(`${device}::${flow}`);
      continue; // not a per-case clip
    }

    // results/videos/{device}/{pass|fail}/{TC id}.webm
    const destDir = path.join(VIDEOS_DIR, device, status);
    fs.mkdirSync(destDir, { recursive: true });
    const dest = path.join(destDir, `${entry.tc}.webm`);

    // Prefer rename (same volume); fall back to copy across volumes.
    try {
      fs.renameSync(src, dest);
    } catch {
      fs.copyFileSync(src, dest);
    }

    if (!clipsByDevice.has(device)) clipsByDevice.set(device, []);
    clipsByDevice.get(device).push(dest);

    if (flow === 'positive' || flow === 'negative') {
      const key = `${device}::${flow}`;
      if (!clipsByDeviceFlow.has(key)) clipsByDeviceFlow.set(key, []);
      clipsByDeviceFlow.get(key).push(dest);
    }
  }

  const ffmpeg = hasFfmpeg();

  // Build one full end-to-end RUN video per device (all clips, run order).
  // This is the only video that still needs ffmpeg (many separate test contexts).
  for (const [device, clips] of clipsByDevice) {
    const outPath = path.join(VIDEOS_DIR, device, `full_run__${device}.webm`);
    if (ffmpeg) {
      if (!concatWithFfmpeg(clips, outPath)) {
        console.warn(`[qa-capture] could not build full-run video for ${device}; per-TC clips kept.`);
      }
    } else {
      console.warn(
        `[qa-capture] ffmpeg not found — skipped full_run__${device}.webm; ` +
          `per-TC clips are kept under results/videos/${device}/.`,
      );
    }
  }

  // Full end-to-end PROCESS videos (positive / negative) per device. Preferred
  // source is the native journey recording (already saved above, no ffmpeg). Only
  // when NO journey spec ran for a flow do we fall back to stitching its tagged
  // per-case clips with ffmpeg.
  for (const [key, clips] of clipsByDeviceFlow) {
    if (journeyDone.has(key)) continue; // native full-process already produced
    const [device, flow] = key.split('::');
    const outPath = path.join(VIDEOS_DIR, device, `full_process__${device}__${flow}.webm`);
    if (ffmpeg) {
      if (!concatWithFfmpeg(clips, outPath)) {
        console.warn(`[qa-capture] could not build full_process ${flow} video for ${device}; per-TC clips kept.`);
      }
    } else {
      console.warn(
        `[qa-capture] no [journey] spec for ${device} ${flow} and ffmpeg not found — ` +
          `skipped full_process__${device}__${flow}.webm (per-TC ${flow} clips kept). ` +
          `Add a [journey] spec to get this video without ffmpeg.`,
      );
    }
  }

  // COMPLETE the evidence folder skeleton so EVERY expected folder exists — even
  // when a run has no failing cases (an empty `fail/` folder is still created) so
  // the results tree is uniform and predictable. Screenshots pass/fail at the top;
  // per-device videos pass/fail for each device that ran (default desktop/tablet/
  // mobile when none were recorded).
  const skelDevices = devicesSeen.size ? [...devicesSeen] : ['desktop', 'tablet', 'mobile'];
  for (const sub of ['pass', 'fail']) {
    fs.mkdirSync(path.join(RESULTS_DIR, 'screenshots', sub), { recursive: true });
  }
  for (const dev of skelDevices) {
    for (const sub of ['pass', 'fail']) {
      fs.mkdirSync(path.join(VIDEOS_DIR, dev, sub), { recursive: true });
    }
  }

  // ROBUST FALLBACK for the full-process journey videos: scan the raw Playwright
  // artifact directories directly. A [journey] spec always produces its own video
  // in an artifact dir whose name contains the spec name + flow + project — this
  // does not depend on the afterEach hook having recorded a video-map entry, so
  // the positive/negative full-process videos are produced reliably (no ffmpeg).
  const artifactsRoot = path.join(RESULTS_DIR, '.pw-artifacts');
  try {
    const dirs = fs.readdirSync(artifactsRoot, { withFileTypes: true });
    for (const d of dirs) {
      if (!d.isDirectory()) continue;
      const name = d.name.toLowerCase();
      if (!name.includes('journey')) continue;
      const flow = name.includes('negative') ? 'negative'
        : name.includes('positive') ? 'positive' : null;
      if (!flow) continue;
      const devMatch = name.match(/(desktop|tablet|mobile)/);
      const device = devMatch ? devMatch[1] : 'default';
      const key = `${device}::${flow}`;
      if (journeyDone.has(key)) continue; // already saved from the video-map
      const src = findWebm(path.join(artifactsRoot, d.name));
      if (!src) continue;
      fs.mkdirSync(path.join(VIDEOS_DIR, device), { recursive: true });
      const outPath = path.join(VIDEOS_DIR, device, `full_process__${device}__${flow}.webm`);
      try {
        fs.copyFileSync(src, outPath);
        journeyDone.add(key);
      } catch {
        /* ignore — non-fatal */
      }
    }
  } catch {
    /* no artifacts dir — nothing to scan */
  }

  // Clean up the raw artifacts directory now that the videos have been renamed.
  // Non-critical — leave it in place if removal fails.
  const artifactsDir = path.join(RESULTS_DIR, '.pw-artifacts');
  try {
    fs.rmSync(artifactsDir, { recursive: true, force: true });
  } catch {
    // ignore — a leftover artifacts dir does not affect the report
  }

  // Reset the per-test video map now that this run's clips have been renamed and
  // stitched. This is REQUIRED for the per-device sequential run: Scale runs one
  // `playwright test --project=<device>` invocation per chosen device, and this
  // teardown runs after EACH invocation. Clearing the map here means the next
  // device starts with a fresh map (its _helpers.js appends only that device's
  // entries), so devices never cross-contaminate and each device's full_run /
  // full_process videos are built from that device's clips only. Non-critical.
  try {
    fs.rmSync(VIDEO_MAP_PATH, { force: true });
  } catch {
    // ignore — a stale map is tolerated (entries whose .webm is already moved are skipped)
  }
}
