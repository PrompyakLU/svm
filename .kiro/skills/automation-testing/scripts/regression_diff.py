"""Regression diff for the QA automation skill's Reporting_Engine.

Pure comparison of two status maps (TC id -> status string) from a previous
and a current run. Produces the set of newly-failing and newly-passing test
case ids over the ids common to both runs.

Design reference: design.md "Regression Diff" and Property 17.

CLI (skill-relative invocation):
    python .kiro/skills/automation-testing/scripts/regression_diff.py <prev.json> <curr.json>
"""

from __future__ import annotations

import json
import sys


def diff(prev: dict[str, str], curr: dict[str, str]) -> tuple[set[str], set[str]]:
    """Compute newly-failing and newly-passing test cases between two runs.

    Args:
        prev: Mapping of Test_Case id -> status for the previous run.
        curr: Mapping of Test_Case id -> status for the current run.

    Returns:
        A tuple ``(newly_failing, newly_passing)`` where, over the ids common
        to both maps:
          - ``newly_failing`` = ids whose status was not "Fail" before and is
            "Fail" now.
          - ``newly_passing`` = ids whose status was not "Pass" before and is
            "Pass" now.
        Both sets are subsets of the common ids and are mutually disjoint.
    """
    common = set(prev) & set(curr)
    newly_failing = {
        tc_id
        for tc_id in common
        if prev[tc_id] != "Fail" and curr[tc_id] == "Fail"
    }
    newly_passing = {
        tc_id
        for tc_id in common
        if prev[tc_id] != "Pass" and curr[tc_id] == "Pass"
    }
    return (newly_failing, newly_passing)


def _load_status_map(path: str) -> dict[str, str]:
    """Load a status map {id: status} from either a flat dict or a TestCase list.

    Accepts two formats:
      1. Flat dict: {"TC_001": "Pass", "TC_002": "Fail", ...}
      2. TestCase list (or {"test_cases": [...]}) as produced by report_model.
         Each entry must have "id" and "status" keys.
    """
    with open(path, "r", encoding="utf-8") as fh:
        data = json.load(fh)

    # Format 1: already a flat {id: status} dict.
    if isinstance(data, dict) and "test_cases" not in data:
        return {str(k): str(v) for k, v in data.items()}

    # Format 2: list of TestCase objects (or wrapped in {"test_cases": [...]}).
    records = data.get("test_cases", data) if isinstance(data, dict) else data
    if not isinstance(records, list):
        raise ValueError(f"Unexpected JSON structure in {path}")

    return {str(r["id"]): str(r.get("status", "")) for r in records}


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        prog = "regression_diff.py"
        print(f"usage: {prog} <prev.json> <curr.json>", file=sys.stderr)
        return 2

    prev = _load_status_map(argv[1])
    curr = _load_status_map(argv[2])
    newly_failing, newly_passing = diff(prev, curr)

    result = {
        "newly_failing": sorted(newly_failing),
        "newly_passing": sorted(newly_passing),
    }
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
