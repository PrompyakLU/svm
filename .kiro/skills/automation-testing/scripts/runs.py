"""Run snapshots & baseline selection for Re-test / Regression.

Each completed run is snapshotted into ``results/runs/<timestamp>/`` so a later
Re-test can diff against a chosen **baseline** run. A snapshot copies the run's
``test_cases.json`` (the single source of truth) plus the generated reports it
finds, and writes a small ``manifest.json`` recording the ``run_id``, timestamp,
document version (from ``report_meta.json`` when present), and the pass/fail
counts.

Versioning policy (mirrored in SKILL.md):
  * Every execution produces a NEW run snapshot, keyed by its timestamp/run_id —
    so re-running unchanged code still yields a distinct, comparable run.
  * The **document version** (``report_meta.version``) is a *semantic* version
    that changes only when the code or test scope meaningfully changes — it is
    NOT bumped automatically on every re-test. Use the run timestamp/run_id to
    distinguish executions; use the document version to mark a new test cycle.

CLI:
    python runs.py snapshot <qa_dir> [run_id]   # snapshot the current run
    python runs.py list <qa_dir>                # list available run snapshots
    python runs.py baseline <qa_dir>            # print latest baseline test_cases.json path
"""

from __future__ import annotations

import json
import os
import shutil
import sys
from datetime import datetime, timezone

import report_model

# Data files copied into a run snapshot when present (data/test_cases.json is
# required). Report files under doc/ are copied wholesale (their names are now
# system-specific, e.g. "Application_Testing_Report - <system>.docx").
_SNAPSHOT_DATA_FILES: tuple[str, ...] = (
    "data/test_cases.json",
    "data/report_meta.json",
)

_RUNS_SUBPATH = os.path.join("results", "runs")
_BACKUP_SUBPATH = "Backup"
_MANIFEST_NAME = "manifest.json"


def _runs_dir(qa_dir: str) -> str:
    """Absolute path to the ``results/runs`` directory under ``qa_dir``."""
    return os.path.join(qa_dir, _RUNS_SUBPATH)


def _snapshot_test_cases_json(run_path: str) -> str:
    """Path to a snapshot's canonical test_cases.json (under its data/ folder)."""
    return os.path.join(run_path, "data", "test_cases.json")


def _counts(test_cases_json: str) -> dict[str, int]:
    """Return {total,passed,failed,blocked,skipped,not_run} from a test_cases.json."""
    keys = {"Pass": "passed", "Fail": "failed", "Blocked": "blocked",
            "Skipped": "skipped", "Not Run": "not_run"}
    counts = {v: 0 for v in keys.values()}
    try:
        cases = report_model.load(test_cases_json)
    except (OSError, ValueError):
        cases = []
    for c in cases:
        name = keys.get((c.status or "").strip())
        if name:
            counts[name] += 1
    counts["total"] = len(cases)
    return counts


def _doc_version(qa_dir: str) -> str:
    """Best-effort document version from data/report_meta.json (else '—')."""
    path = os.path.join(qa_dir, "data", "report_meta.json")
    try:
        with open(path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (OSError, ValueError):
        return "—"
    if isinstance(data, dict):
        for key in ("version", "documentVersion", "docVersion", "ver"):
            val = data.get(key)
            if val:
                return str(val)
    return "—"


def snapshot_run(qa_dir: str, run_id: str | None = None) -> str:
    """Snapshot the current run into ``results/runs/<timestamp>/``.

    Copies the present snapshot files (preserving their ``data/`` / ``doc/``
    subfolders) plus any per-device workbooks, and writes a ``manifest.json``.
    Returns the snapshot directory. Raises ``FileNotFoundError`` when
    ``data/test_cases.json`` is missing (nothing to snapshot).
    """
    src_json = os.path.join(qa_dir, "data", "test_cases.json")
    if not os.path.exists(src_json):
        raise FileNotFoundError(f"no data/test_cases.json in {qa_dir}")

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    dest = os.path.join(_runs_dir(qa_dir), timestamp)
    os.makedirs(dest, exist_ok=True)

    copied: list[str] = []
    # Data files (fixed names).
    for rel in _SNAPSHOT_DATA_FILES:
        src = os.path.join(qa_dir, rel)
        if os.path.exists(src):
            dst = os.path.join(dest, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)
            copied.append(rel)
    # Every report in doc/ (system-specific filenames → copy the whole folder).
    doc_src = os.path.join(qa_dir, "doc")
    if os.path.isdir(doc_src):
        for name in sorted(os.listdir(doc_src)):
            src = os.path.join(doc_src, name)
            if os.path.isfile(src):
                dst = os.path.join(dest, "doc", name)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(src, dst)
                copied.append(f"doc/{name}")

    manifest = {
        "run_id": run_id or timestamp,
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "doc_version": _doc_version(qa_dir),
        "counts": _counts(src_json),
        "files": copied,
    }
    with open(os.path.join(dest, _MANIFEST_NAME), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False)
    return dest


def backup_docs(qa_dir: str) -> str | None:
    """Back up the CURRENT ``doc/`` set before a Re-test overwrites it.

    Copies every file in ``doc/`` (the latest .docx / .xlsx) into
    ``Backup/<timestamp>/`` so the previous cycle's reports are preserved. Call
    this at the start of a Re-test, before the reports are rebuilt. Returns the
    backup directory, or ``None`` when there is nothing to back up.
    """
    doc_src = os.path.join(qa_dir, "doc")
    if not os.path.isdir(doc_src):
        return None
    files = [f for f in sorted(os.listdir(doc_src))
             if os.path.isfile(os.path.join(doc_src, f))]
    if not files:
        return None
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    dest = os.path.join(qa_dir, _BACKUP_SUBPATH, timestamp)
    os.makedirs(dest, exist_ok=True)
    for name in files:
        shutil.copy2(os.path.join(doc_src, name), os.path.join(dest, name))
    return dest


def list_runs(qa_dir: str) -> list[dict]:
    """Return run snapshots (oldest→newest) as manifest dicts + their dir/path."""
    runs_dir = _runs_dir(qa_dir)
    if not os.path.isdir(runs_dir):
        return []
    out: list[dict] = []
    for name in sorted(os.listdir(runs_dir)):
        run_path = os.path.join(runs_dir, name)
        if not os.path.isdir(run_path):
            continue
        manifest_path = os.path.join(run_path, _MANIFEST_NAME)
        info: dict = {"run_id": name, "dir": run_path}
        try:
            with open(manifest_path, "r", encoding="utf-8") as fh:
                info.update(json.load(fh))
        except (OSError, ValueError):
            pass
        info["test_cases_json"] = _snapshot_test_cases_json(run_path)
        out.append(info)
    return out


def latest_baseline(qa_dir: str) -> str | None:
    """Path to the most recent snapshot's ``test_cases.json`` (else None)."""
    runs = [r for r in list_runs(qa_dir)
            if os.path.exists(r.get("test_cases_json", ""))]
    return runs[-1]["test_cases_json"] if runs else None


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    if len(args) < 2 or args[0] not in ("snapshot", "list", "baseline", "backup"):
        print(
            "Usage: python runs.py <snapshot|list|baseline|backup> <qa_dir> [run_id]",
            file=sys.stderr,
        )
        return 2

    command, qa_dir = args[0], args[1]
    if command == "snapshot":
        run_id = args[2] if len(args) > 2 else None
        dest = snapshot_run(qa_dir, run_id)
        print(f"Snapshotted run to {dest}")
        return 0
    if command == "backup":
        dest = backup_docs(qa_dir)
        print(f"Backed up doc/ to {dest}" if dest else "(no doc/ to back up)")
        return 0
    if command == "list":
        runs = list_runs(qa_dir)
        if not runs:
            print("(no run snapshots)")
        for r in runs:
            c = r.get("counts", {})
            print(f"{r.get('run_id')} · v{r.get('doc_version', '—')} · "
                  f"total={c.get('total', '?')} pass={c.get('passed', '?')} "
                  f"fail={c.get('failed', '?')}")
        return 0
    # baseline
    base = latest_baseline(qa_dir)
    print(base or "(no baseline)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
