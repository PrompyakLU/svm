"""Safe per-device result merge + roll-up for re-tests.

When a re-test runs only SOME devices (e.g. adding tablet/mobile to a suite
already run on desktop), the per-device results must be **merged additively** —
never clobbering the devices that were not re-run — and each case's top-level
status must be re-derived as the worst-of across ALL devices present. Doing this
by hand is error-prone, so this module provides one deterministic implementation
the re-test stage calls instead of hand-editing.

Two operations:

* ``merge`` — merge a "new run" ``test_cases.json`` into the main model per
  Test_Case id: for every case, ``device_results`` is combined key-by-key so the
  new run's device(s) are added/updated while every previously-recorded device is
  preserved. The top-level roll-up is then recomputed (see below).
* ``rollup`` — (re)compute each case's top-level ``status`` / ``actual`` /
  ``screenshot_path`` / ``video_path`` as the worst-of across its
  ``device_results``, WITHOUT dropping any device. Idempotent; safe to run after
  any manual edit as a final consistency pass.

Worst-of status precedence (worst → best):
    Fail > Blocked > Skipped > Not Run > Pass
so a case is only "Pass" at the top level when every device that ran passed, and
any failing device surfaces immediately. The top-level evidence pointers copy the
worst device's screenshot/video so the report's headline row points at the
problem.

CLI:
    python device_results.py rollup <qa_dir>
    python device_results.py merge <qa_dir> <new_run_test_cases.json>
"""

from __future__ import annotations

import sys

import paths
import report_model
from report_model import TestCase

# Worst → best. Lower index = worse. Used for the top-level roll-up.
_STATUS_RANK: dict[str, int] = {
    "Fail": 0,
    "Blocked": 1,
    "Skipped": 2,
    "Not Run": 3,
    "Pass": 4,
}
_DEFAULT_RANK = _STATUS_RANK["Not Run"]


def _rank(status: str) -> int:
    return _STATUS_RANK.get((status or "").strip(), _DEFAULT_RANK)


def worst_device(device_results: dict) -> tuple[str, dict]:
    """Return ``(device_name, result)`` of the worst-status device, or ("", {}).

    The worst device is the one whose status ranks worst (Fail first). Ties keep
    the canonical device order (desktop → tablet → mobile → extras).
    """
    if not isinstance(device_results, dict) or not device_results:
        return "", {}
    ordered = [d for d in report_model.DEVICES if d in device_results]
    ordered += sorted(d for d in device_results if d not in report_model.DEVICES)
    best_dev, best_res, best_rank = "", {}, 99
    for dev in ordered:
        res = device_results.get(dev)
        if not isinstance(res, dict):
            continue
        r = _rank(str(res.get("status", "")))
        if r < best_rank:
            best_dev, best_res, best_rank = dev, res, r
    return best_dev, best_res


def rollup_case(case: TestCase) -> TestCase:
    """Re-derive a case's top-level result fields from its ``device_results``.

    No-op when the case carries no per-device results (a single-device / non-
    responsive layer keeps its own top-level values). All device keys are
    preserved untouched.
    """
    dr = case.device_results if isinstance(case.device_results, dict) else {}
    if not dr:
        return case
    _dev, res = worst_device(dr)
    if not res:
        return case
    if str(res.get("status", "")).strip():
        case.status = str(res.get("status")).strip()
    if str(res.get("actual", "")).strip():
        case.actual = str(res.get("actual")).strip()
    if str(res.get("screenshot_path", "")).strip():
        case.screenshot_path = str(res.get("screenshot_path")).strip()
    if str(res.get("video_path", "")).strip():
        case.video_path = str(res.get("video_path")).strip()
    return case


def merge_device_results(base: dict, incoming: dict) -> dict:
    """Merge two ``device_results`` maps: ``incoming`` device keys win, others kept.

    Neither input is mutated. A device present only in ``base`` is preserved; a
    device present in ``incoming`` (a freshly re-run device) replaces that
    device's entry only.
    """
    merged = dict(base or {})
    for dev, res in (incoming or {}).items():
        if dev:
            merged[dev] = res
    return merged


def rollup(qa_dir: str) -> int:
    """Recompute the top-level roll-up for every case in ``qa_dir`` in place.

    Returns the number of cases updated (touched). Preserves every device key.
    """
    data_json = _data_json(qa_dir)
    cases = report_model.load(data_json)
    for case in cases:
        rollup_case(case)
    report_model.dump(cases, data_json)
    return len(cases)


def merge(qa_dir: str, new_run_json: str) -> int:
    """Merge a new-run model into the main model per Test_Case id, additively.

    For each id present in BOTH files, the new run's ``device_results`` are merged
    over the existing ones (new device(s) added/updated, prior devices preserved),
    then the top-level roll-up is recomputed. New ids present only in the new run
    are appended. Returns the number of cases merged/updated.
    """
    data_json = _data_json(qa_dir)
    base_cases = report_model.load(data_json)
    new_cases = report_model.load(new_run_json)
    by_id = {c.id: c for c in base_cases}
    touched = 0
    for nc in new_cases:
        bc = by_id.get(nc.id)
        if bc is None:
            base_cases.append(nc)
            rollup_case(nc)
            touched += 1
            continue
        bc.device_results = merge_device_results(bc.device_results, nc.device_results)
        rollup_case(bc)
        touched += 1
    report_model.dump(base_cases, data_json)
    return touched


def _data_json(qa_dir: str) -> str:
    """Resolve ``<qa_dir>/data/test_cases.json`` regardless of qa_dir name."""
    import os

    prefix = paths.QA_ROOT + "/"
    rel = paths.test_cases_json()
    tail = rel[len(prefix):] if rel.startswith(prefix) else rel
    return os.path.join(qa_dir, tail)


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    if len(args) >= 2 and args[0] == "rollup":
        n = rollup(args[1])
        print(f"Rolled up {n} case(s) in {args[1]!r} (all device results preserved).")
        return 0
    if len(args) >= 3 and args[0] == "merge":
        n = merge(args[1], args[2])
        print(f"Merged {n} case(s) from {args[2]!r} into {args[1]!r} (additive).")
        return 0
    print(
        "Usage:\n"
        "  python device_results.py rollup <qa_dir>\n"
        "  python device_results.py merge <qa_dir> <new_run_test_cases.json>",
        file=sys.stderr,
    )
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
