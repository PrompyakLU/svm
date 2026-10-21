"""Artifact & document path derivation for the QA automation skill.

Central, deterministic paths for every file the skill reads or writes, so the
orchestrator, the reporters, and the run snapshotter all agree on the layout:

    qa-automation-testing/
    ├── data/     # the .json model + metadata + runtime maps
    ├── doc/      # human-facing reports (a SINGLE test_cases.xlsx, docx, html)
    ├── scripts/  # generated specs + copied helpers
    └── results/  # raw evidence (screenshots / videos / baselines / runs)

Reports are a single workbook (`Test_Case_Summary - <system>.xlsx`, with a
per-device sheet inside it — no master-vs-by-device split, no by-device/ folder)
plus one Word volume (`Application_Testing_Report - <system>.docx`). No HTML report
is produced. Evidence is namespaced per device: screenshots by an `__{device}`
filename suffix; videos in a per-device subfolder that also holds the full
end-to-end run video for that device.
"""

# Root location (relative to the target project) for the skill's output tree.
QA_ROOT = "qa-automation-testing"

# Organized sub-roots.
DATA_ROOT = f"{QA_ROOT}/data"        # all .json (model, metadata, runtime maps)
DOC_ROOT = f"{QA_ROOT}/doc"          # human-facing reports
RESULTS_ROOT = f"{QA_ROOT}/results"  # raw evidence


def _status_folder(status: str) -> str:
    """Map an execution status to its artifact subfolder (``pass`` / ``fail``)."""
    return "pass" if status == "Pass" else "fail"


def _suffix(device: str | None) -> str:
    """Return the ``__{device}`` filename suffix, or '' when no device given."""
    return f"__{device}" if device else ""


# --- Data files (.json) ----------------------------------------------------

def test_cases_json() -> str:
    """Path to the single-source-of-truth model: ``data/test_cases.json``."""
    return f"{DATA_ROOT}/test_cases.json"


def report_meta_json() -> str:
    """Path to the optional run/report metadata: ``data/report_meta.json``."""
    return f"{DATA_ROOT}/report_meta.json"


def video_map_json() -> str:
    """Path to the runtime video map written per-test: ``data/.video-map.json``."""
    return f"{DATA_ROOT}/.video-map.json"


# --- Documents (reports) ---------------------------------------------------

import re

# Backup of the previous doc/ set, taken before a Re-test overwrites it.
BACKUP_ROOT = f"{QA_ROOT}/Backup"


def _sanitize_label(label: str | None) -> str:
    """Filesystem-safe form of a system label (illegal chars → space)."""
    if not label:
        return ""
    cleaned = re.sub(r'[\\/:*?"<>|\n\r\t]+', " ", str(label)).strip()
    return re.sub(r"\s{2,}", " ", cleaned)


def _labelled(base: str, ext: str, label: str | None) -> str:
    """``doc/<base> - <label><ext>`` (or ``doc/<base><ext>`` when no label)."""
    l = _sanitize_label(label)
    name = f"{base} - {l}{ext}" if l else f"{base}{ext}"
    return f"{DOC_ROOT}/{name}"


def docx_report(label: str | None = None) -> str:
    """Path to the consolidated Word report.

    Professional name ``doc/Application_Testing_Report - <system>.docx`` when a
    system label is given; else ``doc/Application_Testing_Report.docx``.
    """
    return _labelled("Application_Testing_Report", ".docx", label)


def xlsx_report(label: str | None = None) -> str:
    """Path to the XLSX workbook.

    ``doc/Test_Case_Summary - <system>.xlsx`` when a label is given; else
    ``doc/Test_Case_Summary.xlsx``. There is exactly one workbook (per-device
    results live in per-device sheets inside it).
    """
    return _labelled("Test_Case_Summary", ".xlsx", label)


# --- Evidence (per Test_Case, per device) ----------------------------------

def screenshot_path(tc_id: str, status: str, device: str | None = None) -> str:
    """Derive the screenshot path for a Test_Case (optionally per device).

    Returns
    ``qa-automation-testing/results/screenshots/{pass|fail}/{tc_id}[__device].png``.
    """
    folder = _status_folder(status)
    return f"{RESULTS_ROOT}/screenshots/{folder}/{tc_id}{_suffix(device)}.png"


def video_path(tc_id: str, status: str, device: str | None = None) -> str:
    """Derive the per-Test_Case video path.

    Videos are grouped in a per-device subfolder so a device's recordings stay
    together:
    ``results/videos/{device}/{pass|fail}/{tc_id}.webm`` (device given), or
    ``results/videos/{pass|fail}/{tc_id}.webm`` when no device is in play.
    """
    folder = _status_folder(status)
    base = f"{RESULTS_ROOT}/videos"
    if device:
        return f"{base}/{device}/{folder}/{tc_id}.webm"
    return f"{base}/{folder}/{tc_id}.webm"


def full_run_video_path(device: str | None = None) -> str:
    """Path to the full end-to-end run video for a device.

    ``results/videos/{device}/full_run__{device}.webm`` — a single continuous
    recording of that device's whole test run (its per-Test_Case clips joined in
    run order). Without a device: ``results/videos/full_run.webm``.
    """
    base = f"{RESULTS_ROOT}/videos"
    if device:
        return f"{base}/{device}/full_run__{device}.webm"
    return f"{base}/full_run.webm"


# Flow types for the dedicated full end-to-end process videos.
FLOW_POSITIVE = "positive"
FLOW_NEGATIVE = "negative"
FLOWS = (FLOW_POSITIVE, FLOW_NEGATIVE)


def full_process_video_path(flow: str, device: str | None = None) -> str:
    """Path to a full END-TO-END PROCESS video for a flow type (positive/negative).

    Beyond the overall ``full_run`` video, the suite records one continuous
    end-to-end journey per flow so reviewers can watch the whole happy path and
    the whole error/negative path uninterrupted:
    ``results/videos/{device}/full_process__{device}__{flow}.webm`` (device given),
    or ``results/videos/full_process__{flow}.webm`` without a device.

    ``flow`` must be one of :data:`FLOWS` ("positive" / "negative").
    """
    if flow not in FLOWS:
        raise ValueError(f"unknown flow: {flow!r} (expected one of {FLOWS})")
    base = f"{RESULTS_ROOT}/videos"
    if device:
        return f"{base}/{device}/full_process__{device}__{flow}.webm"
    return f"{base}/full_process__{flow}.webm"


# --- Output-tree scaffolding ------------------------------------------------

import os

# The standard sub-tree created up-front so every later stage has a place to
# write, and so the empty evidence folders exist even before anything runs.
# The base output tree. Per-DEVICE evidence folders are intentionally NOT created
# here — they are created on demand for exactly the device(s) the user chooses at
# Scale (see :func:`ensure_device_dirs`), so the tree never hard-codes devices that
# were never selected.
_SKELETON_DIRS: tuple[str, ...] = (
    "data",
    "doc",
    "scripts",
    "results",
    "results/screenshots/pass",
    "results/screenshots/fail",
    "results/videos",
    "results/visual-baselines",
    "results/runs",
)


def ensure_skeleton(base_dir: str = QA_ROOT) -> list[str]:
    """Create the standard ``qa-automation-testing/`` output tree under ``base_dir``.

    Idempotent (``exist_ok=True``). Intended to run ONCE at the first stage
    (Intake) so `data/`, `doc/`, `results/…` (incl. empty `pass/`/`fail/`
    screenshot folders) and `scripts/` all exist before any stage writes. Returns
    the absolute paths created (sorted).
    """
    created: list[str] = []
    for rel in _SKELETON_DIRS:
        target = os.path.join(base_dir, rel)
        os.makedirs(target, exist_ok=True)
        created.append(target)
    return sorted(created)


def ensure_device_dirs(base_dir: str, devices) -> list[str]:
    """Create the per-device evidence folders for the CHOSEN device(s) only.

    Call this at Scale, AFTER the user has picked which responsive device(s) to
    run, so only the selected devices get folders (never a fixed desktop/tablet/
    mobile set). For each device it creates
    ``results/videos/{device}/pass`` and ``.../fail``. Idempotent. Returns the
    absolute paths created (sorted).
    """
    created: list[str] = []
    for device in devices:
        device = str(device).strip()
        if not device:
            continue
        for status in ("pass", "fail"):
            target = os.path.join(base_dir, "results", "videos", device, status)
            os.makedirs(target, exist_ok=True)
            created.append(target)
    return sorted(created)


if __name__ == "__main__":
    # CLI:
    #   python paths.py [qa_dir]                     → scaffold the base tree (Intake)
    #   python paths.py [qa_dir] devices d1 d2 ...   → add per-device folders (Scale)
    import sys as _sys
    _args = _sys.argv[1:]
    _base = _args[0] if _args else QA_ROOT
    if len(_args) >= 2 and _args[1] == "devices":
        _made = ensure_device_dirs(_base, _args[2:])
        print(f"Created/verified {len(_made)} device folder(s) under {_base!r}:")
    else:
        _made = ensure_skeleton(_base)
        print(f"Created/verified {len(_made)} folder(s) under {_base!r}:")
    for _d in _made:
        print(f"  {_d}")
