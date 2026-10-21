"""Pre-flight validation for the Reporting_Engine inputs.

Guards ``build_reports.py`` against empty / malformed / incomplete inputs so it
fails with a clear, actionable message instead of an opaque stack trace.

Two checks:

* :func:`validate_test_cases` — ``test_cases.json`` must exist, be valid JSON, be
  a non-empty array of objects, and every case must carry a non-empty ``id`` and
  ``scenario`` (the identity + reporting essentials; ids must be unique). Other
  model fields (``module`` / ``qa_layer`` / ``steps`` / ``expected``) are reported
  as **warnings** — the ``TestCase`` model supplies safe defaults for them, so
  they are recommended rather than fatal.

* :func:`validate_report_meta` — ``report_meta.json`` is OPTIONAL and, by design,
  every field is optional (missing values render as "—"), with aliased and nested
  keys accepted by ``report_docx.normalize_meta``. Therefore this check only
  verifies the file (when present) is valid JSON whose root is an object. It does
  NOT enforce required fields or reject "unknown" keys — doing so would falsely
  flag legitimate aliases (``systemName``), nested blocks (``project: {...}``), and
  the many optional keys the report supports.
"""

from __future__ import annotations

import json
from pathlib import Path

# Hard-required per case: without a non-empty id + scenario a report row is
# meaningless and id-based report parity breaks.
REQUIRED_FIELDS: frozenset[str] = frozenset({"id", "scenario"})

# Recommended (warned, not fatal — TestCase supplies defaults).
RECOMMENDED_FIELDS: frozenset[str] = frozenset({"module", "qa_layer", "steps", "expected"})


def validate_test_cases(path: str) -> tuple[list[str], list[str]]:
    """Validate ``test_cases.json``. Returns ``(errors, warnings)`` (errors empty = OK)."""
    errors: list[str] = []
    warnings: list[str] = []
    p = Path(path)

    if not p.exists():
        return [f"File not found: {path}"], warnings

    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, ValueError) as exc:
        return [f"Invalid JSON in {p.name}: {exc}"], warnings

    if not isinstance(data, list):
        return ["Root of test_cases.json must be a JSON array"], warnings
    if len(data) == 0:
        return ["test_cases.json is empty (no test cases)"], warnings

    seen_ids: set[str] = set()
    for i, case in enumerate(data):
        if not isinstance(case, dict):
            errors.append(f"Case index {i}: must be an object, got {type(case).__name__}")
            continue
        cid = str(case.get("id", "")).strip()
        for fieldname in REQUIRED_FIELDS:
            if not str(case.get(fieldname, "")).strip():
                errors.append(f"Case index {i} (id={cid or '?'}): missing/empty required field '{fieldname}'")
        if cid:
            if cid in seen_ids:
                errors.append(f"Case index {i}: duplicate id '{cid}'")
            seen_ids.add(cid)
        missing_reco = [f for f in RECOMMENDED_FIELDS if not str(case.get(f, "")).strip()]
        if missing_reco:
            warnings.append(f"Case id={cid or i}: missing recommended field(s) {sorted(missing_reco)}")

    return errors, warnings


def validate_report_meta(path: str) -> list[str]:
    """Validate ``report_meta.json`` when present. Returns a list of errors (empty = OK).

    report_meta is optional and all its fields are optional (aliased / nestable),
    so this only checks the file parses as a JSON object.
    """
    p = Path(path)
    if not p.exists():
        return []  # optional
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, ValueError) as exc:
        return [f"Invalid JSON in {p.name}: {exc}"]
    if not isinstance(data, dict):
        return ["Root of report_meta.json must be a JSON object"]
    return []
