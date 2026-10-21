"""Single source of truth for the Reporting_Engine.

Defines the :class:`TestCase` dataclass plus ``load``/``dump`` helpers used
identically by the XLSX writer, the DOCX writer, and the regression diff so the
two report formats never drift. The model normalizes records once on load;
execution values (status, actual, executed_at) are stored verbatim and never
recomputed.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field, fields, replace
from typing import Any


# Field order is canonical and matches the SKILL.md column reference (A→AA).
# Both writers iterate in this order for parity. Every field is defaulted so the
# order is unconstrained by Python's "non-default fields precede defaults" rule.
@dataclass
class TestCase:
    # --- Identification (columns A–I) ---
    id: str = ""                 # A — TC_XXX, ordered by E2E journey
    user_story_id: str = ""      # B — originating user story / feature id
    module: str = ""             # C — functional module / area under test
    qa_layer: str = ""           # D — UI / API / Unit-Integration
    # Per-type bucket for the split report sheets (v1): "UI-E2E" /
    # "UI-Functional" / "API" / "Unit-Integration". Model-only (not an XLSX
    # column); set at Generate. When blank it is derived from `qa_layer`.
    test_type: str = ""
    scenario: str = ""           # E — short scenario title
    category: str = ""           # F — Positive / Negative / Performance / …
    priority: str = ""           # G — High / Medium / Low
    severity: str = ""           # H — explicit severity (else derived from priority)
    # Risk level for risk-based execution ordering (Critical/High/Medium/Low).
    # Derived from priority × module-criticality when not explicitly set.
    risk_level: str = ""         # I — risk-based execution level
    # --- Test Definition (columns J–Q) ---
    preconditions: str = ""      # J — preconditions before the steps run
    steps: str = ""              # K — ordered steps
    test_data: str = ""          # L — concrete inputs / data
    # Origin of test data (inline/file/fixture/api). Helps trace data provenance.
    test_data_source: str = ""   # M — origin of the test data
    expected: str = ""           # N — expected result
    rule_id: str = ""            # O — business-rule id
    source_reference: str = ""   # P — origin pointer (file:section or URL#anchor)
    source_origin: str = ""      # Q — Doc / Code / UI / URL / User-Provided / Inferred
    # --- Execution Result (columns R–X; populated at run, blank pre-run) ---
    status: str = ""             # R — Pass / Fail / Blocked / Skipped / Not Run
    actual: str = ""             # S — actual observed result
    executed_by: str = ""        # T — tester / automation agent
    executed_at: str = ""        # U — execution timestamp
    environment: str = ""        # V — e.g. staging / local
    defect_id: str = ""          # W — linked defect id (mirrors DefectLog)
    notes: str = ""              # X — free notes
    # --- Evidence (columns Y–AA) ---
    # Y — the report CELL holds the ACTUAL embedded image, sourced from the file
    # at `screenshot_path`. Its text value stays empty — the image lives in the
    # cell itself.
    screenshot: str = ""
    screenshot_path: str = ""    # Z — textual path note to the screenshot file
    video_path: str = ""         # AA — textual path note to the video file
    # --- Per-device execution results (NOT an XLSX column) ---
    # Populated at run time when the suite is executed across the responsive
    # device projects (desktop / tablet / mobile). Maps a device name to that
    # device's own result, e.g.:
    #   {"desktop": {"status": "Pass", "actual": "...",
    #                "screenshot_path": "...", "video_path": "..."}, ...}
    # The top-level status/actual/screenshot/video remain the ROLL-UP across
    # devices (worst-of status), so existing reports stay backward-compatible.
    device_results: dict = field(default_factory=dict)


# Prevent pytest from mistaking this domain dataclass for a test class
# (its name starts with "Test"); it is data, not a test suite.
TestCase.__test__ = False


# Tuple of field names in declaration order, derived once from the dataclass.
_FIELD_NAMES: tuple[str, ...] = tuple(f.name for f in fields(TestCase))

# Run-stage execution fields stored verbatim (no recomputation on load/dump).
_EXECUTION_FIELDS: tuple[str, ...] = ("status", "actual", "executed_at")


def _normalize(record: dict[str, Any]) -> TestCase:
    """Normalize a single raw mapping into a :class:`TestCase`.

    - Unknown keys are ignored.
    - Missing optional (run-stage) fields default to "".
    - Generate-stage fields with no value normalize to "" so the model is total.
    - Execution values are copied through verbatim (cast to str only, no
      reinterpretation), satisfying the "store verbatim, no recomputation" rule.
    """
    kwargs: dict[str, Any] = {}
    for name in _FIELD_NAMES:
        value = record.get(name, "")
        # Per-device results are a nested mapping, not a scalar — pass through.
        if name == "device_results":
            kwargs[name] = value if isinstance(value, dict) else {}
            continue
        if value is None:
            value = ""
        # Preserve execution values exactly; only coerce type to str.
        kwargs[name] = value if isinstance(value, str) else str(value)
    # A case with NO execution status yet is "Not Run" — make that explicit so it
    # is labelled correctly everywhere (never left blank, and never mis-filed as a
    # "Fail"). Explicit statuses (Pass/Fail/Blocked/Skipped) are kept as-is.
    if not str(kwargs.get("status", "")).strip():
        kwargs["status"] = "Not Run"
    return TestCase(**kwargs)


def load(json_path: str) -> list[TestCase]:
    """Load and normalize Test_Cases from a JSON file.

    The JSON document is expected to be a list of objects (or an object with a
    top-level ``test_cases`` list). Normalization happens exactly once here so
    downstream writers consume a uniform, ordered model.
    """
    with open(json_path, "r", encoding="utf-8") as fh:
        data = json.load(fh)

    if isinstance(data, dict):
        records = data.get("test_cases", [])
    else:
        records = data

    return [_normalize(record) for record in records]


def dump(cases: list[TestCase], json_path: str) -> None:
    """Serialize Test_Cases to a JSON file preserving field and list order.

    Execution values are written exactly as held on each :class:`TestCase`,
    with no recomputation.
    """
    serialized = [asdict(case) for case in cases]
    with open(json_path, "w", encoding="utf-8") as fh:
        json.dump(serialized, fh, indent=2, ensure_ascii=False)


# ---------------------------------------------------------------------------
# Standard QA helpers (pure, stdlib-only) shared by BOTH report writers so the
# DefectLog / Traceability / severity data never drift between formats.
# ---------------------------------------------------------------------------

# A case in one of these execution states is treated as a logged defect.
DEFECT_TRIGGER_STATUSES: tuple[str, ...] = ("Fail", "Blocked")

# Rendered for any defect/traceability field with no value.
DASH = "—"


def derive_severity(case: TestCase) -> str:
    """Return the case severity, deriving it from priority when unset.

    Mapping: High->High, Medium->Medium, Low->Low, anything else->Medium. An
    explicit ``severity`` on the case always wins.
    """
    sev = (case.severity or "").strip()
    if sev:
        return sev
    priority = (case.priority or "").strip().lower()
    return {"high": "High", "medium": "Medium", "low": "Low"}.get(priority, "Medium")


def defect_cases(cases: list[TestCase]) -> list[TestCase]:
    """Return the cases whose status marks them as defects (Fail / Blocked)."""
    return [c for c in cases if c.status in DEFECT_TRIGGER_STATUSES]


def build_defect_log(cases: list[TestCase]) -> list[dict[str, str]]:
    """Build standard defect-log rows from the failed/blocked cases.

    One row per defect case, in model order. ``defect_id`` uses the case's own
    ``defect_id`` when set, otherwise an auto id ``DEF_<n>`` (1-based). The
    lifecycle ``status`` starts at ``New`` and the defect is assigned to the
    Developer (``Assigned To`` defaults to "—"). Suggested root cause comes from
    the case notes when present.
    """
    rows: list[dict[str, str]] = []
    for n, case in enumerate(defect_cases(cases), start=1):
        defect_id = (case.defect_id or "").strip() or f"DEF_{n}"
        root_cause = (case.notes or "").strip() or DASH
        found_date = (case.executed_at or "").strip() or DASH
        rows.append(
            {
                "defect_id": defect_id,
                "linked_tc_id": case.id,
                "title": case.scenario,
                "steps": case.steps,
                "severity": derive_severity(case),
                "priority": (case.priority or "").strip() or DASH,
                "status": "New",
                "assigned_to": DASH,
                "root_cause": root_cause,
                "resolution": DASH,
                "found_date": found_date,
                "fixed_date": DASH,
            }
        )
    return rows


def defect_severity_counts(cases: list[TestCase]) -> dict[str, int]:
    """Count logged defects by the standard severities, defaulting odd ones to Medium."""
    counts = {"Critical": 0, "High": 0, "Medium": 0, "Low": 0}
    for row in build_defect_log(cases):
        sev = row["severity"]
        if sev in counts:
            counts[sev] += 1
        else:
            counts["Medium"] += 1
    return counts


def build_traceability(cases: list[TestCase]) -> list[dict[str, object]]:
    """Build RTM rows mapping each user story to the test cases that cover it.

    Cases are grouped by ``user_story_id`` (blank grouped under "—"), preserving
    first-appearance order. Each row carries the comma-joined covered TC ids,
    the test-case count, pass/fail counts, and a coverage mark ("✓" when at
    least one TC exists for the story).
    """
    order: list[str] = []
    groups: dict[str, list[TestCase]] = {}
    for case in cases:
        key = (case.user_story_id or "").strip() or DASH
        if key not in groups:
            groups[key] = []
            order.append(key)
        groups[key].append(case)

    rows: list[dict[str, object]] = []
    for key in order:
        group = groups[key]
        rows.append(
            {
                "user_story_id": key,
                "tc_ids": ", ".join(c.id for c in group),
                "tc_count": len(group),
                "passed": sum(1 for c in group if c.status == "Pass"),
                "failed": sum(1 for c in group if c.status == "Fail"),
                "coverage": "✓" if len(group) >= 1 else "",
            }
        )
    return rows


# Module names considered "critical" for risk derivation.
CRITICAL_MODULES: tuple[str, ...] = (
    "auth", "authentication", "login", "payment", "checkout",
    "billing", "user", "account", "security", "core",
)


def derive_risk_level(case: TestCase) -> str:
    """Derive risk level from priority + module criticality when not explicit.

    Returns the case's own risk_level if set; otherwise computes it from
    the priority × module-criticality matrix (ISO 29119-4 risk-based approach).
    """
    if (case.risk_level or "").strip():
        return case.risk_level.strip()

    priority = (case.priority or "").strip().lower()
    module = (case.module or "").strip().lower()
    is_critical_module = any(cm in module for cm in CRITICAL_MODULES)

    if priority == "high" and is_critical_module:
        return "Critical"
    elif priority == "high" or (priority == "medium" and is_critical_module):
        return "High"
    elif priority == "medium":
        return "Medium"
    return "Low"


def risk_execution_order(cases: list[TestCase]) -> list[TestCase]:
    """Sort test cases by risk level for risk-based execution.

    Order: Critical → High → Medium → Low. Within same level, original
    order is preserved (stable sort).
    """
    rank = {"Critical": 0, "High": 1, "Medium": 2, "Low": 3}
    return sorted(cases, key=lambda c: rank.get(derive_risk_level(c), 3))


# Per-type buckets for the split report sheets (v1). UI is split into
# end-to-end journeys vs single feature/function checks; API and
# Unit-Integration map 1:1 to their qa_layer.
TEST_TYPES: tuple[str, ...] = ("UI-E2E", "UI-Functional", "API", "Unit-Integration")


def effective_test_type(case: TestCase) -> str:
    """Return the per-type sheet bucket for ``case``.

    Uses the explicit ``test_type`` when set; otherwise derives from
    ``qa_layer``: UI → "UI-Functional" (default for a UI case with no explicit
    E2E flag), API → "API", Unit-Integration → "Unit-Integration". An unknown
    layer falls back to the layer string (or "Other").
    """
    explicit = (case.test_type or "").strip()
    if explicit:
        return explicit
    layer = (case.qa_layer or "").strip()
    if layer == "UI":
        return "UI-Functional"
    if layer in ("API", "Unit-Integration"):
        return layer
    return layer or "Other"


def group_by_test_type(cases: list[TestCase]) -> list[tuple[str, list[TestCase]]]:
    """Group cases by their effective test type, in canonical order.

    Returns ``(type_name, cases)`` pairs for every type present, ordered by
    :data:`TEST_TYPES` first, then any other type alphabetically. Types with no
    cases are omitted (no empty sheets).
    """
    groups: dict[str, list[TestCase]] = {}
    for case in cases:
        groups.setdefault(effective_test_type(case), []).append(case)
    preferred = [t for t in TEST_TYPES if t in groups]
    extras = sorted(t for t in groups if t not in TEST_TYPES)
    return [(name, groups[name]) for name in (*preferred, *extras)]


# ---------------------------------------------------------------------------
# Per-device (responsive) results. The suite runs at three viewport projects;
# each case's per-device outcome is stored under `device_results`. These helpers
# let the report writers render a device-scoped view and a by-device summary
# without duplicating the model.
# ---------------------------------------------------------------------------

# Canonical device/viewport order for the responsive projects.
DEVICES: tuple[str, ...] = ("desktop", "tablet", "mobile")

# Fields a per-device result may override on the rolled-up TestCase.
_DEVICE_OVERRIDE_FIELDS: tuple[str, ...] = (
    "status", "actual", "executed_by", "executed_at", "environment",
    "defect_id", "notes", "screenshot_path", "video_path",
)


def devices_tested(cases: list[TestCase]) -> list[str]:
    """Return the device names present across all cases, in canonical order.

    Canonical devices (:data:`DEVICES`) come first, then any other device key
    seen, alphabetically. Empty when no case carries per-device results.
    """
    seen: set[str] = set()
    for case in cases:
        if isinstance(case.device_results, dict):
            seen.update(k for k in case.device_results if k)
    preferred = [d for d in DEVICES if d in seen]
    extras = sorted(d for d in seen if d not in DEVICES)
    return [*preferred, *extras]


def planned_devices(meta: dict | None) -> list[str]:
    """Return the PLANNED device list from run metadata (pre-run fallback).

    Before the suite runs, no case carries ``device_results`` yet, so
    :func:`devices_tested` is empty and the reports would show no device
    coverage at all — even though the user already chose which devices to run at
    Intake/Scale. This reads the intended device(s) recorded in
    ``report_meta.json`` (key ``devices`` — a list or a comma-separated string;
    also accepts ``device`` / ``device_list``) so the Generate PREVIEW and the
    environment/Info section can state the planned responsive matrix. Names are
    lower-cased and returned in canonical order (:data:`DEVICES` first, then any
    extras in first-seen order). Empty when nothing is configured.
    """
    if not isinstance(meta, dict):
        return []
    raw = meta.get("devices")
    if raw in (None, ""):
        raw = meta.get("device")
    if raw in (None, ""):
        raw = meta.get("device_list")
    if raw in (None, ""):
        return []
    if isinstance(raw, str):
        items = [p.strip() for p in raw.split(",") if p.strip()]
    elif isinstance(raw, (list, tuple)):
        items = [str(p).strip() for p in raw if str(p).strip()]
    else:
        return []
    seen: list[str] = []
    for it in items:
        key = it.lower()
        if key and key not in seen:
            seen.append(key)
    preferred = [d for d in DEVICES if d in seen]
    extras = [d for d in seen if d not in DEVICES]
    return [*preferred, *extras]


def effective_planned_devices(meta: dict | None, cases: "list[TestCase] | None" = None) -> list[str]:
    """Planned devices for the reports — bulletproof, even if none were recorded.

    Order of preference:
      1. Devices explicitly recorded in ``report_meta`` (see :func:`planned_devices`).
      2. Otherwise, if the suite contains any **UI** case, default to the standard
         responsive set (:data:`DEVICES` = desktop / tablet / mobile), so a UI
         report's Scope / Info / environment ALWAYS states the responsive coverage
         even at the Generate preview (before Scale records `device_results` and
         before any device was written to `report_meta`). Tagged "planned" by the
         writers.
      3. Empty for a non-UI (API / Unit-only) suite — those layers are not
         responsive, so no device matrix is implied.
    """
    explicit = planned_devices(meta)
    if explicit:
        return explicit
    has_ui = any((c.qa_layer or "").strip().upper() == "UI" for c in (cases or []))
    return list(DEVICES) if has_ui else []


def device_view(case: TestCase, device: str) -> TestCase:
    """Return a copy of ``case`` with its result fields overridden for ``device``.

    Pulls ``status`` / ``actual`` / ``screenshot_path`` / ``video_path`` (etc.)
    from ``case.device_results[device]`` when present, leaving the case unchanged
    where a device has no value. ``device_results`` itself is dropped from the
    view. When the device is absent the rolled-up case is returned as-is.
    """
    result = (case.device_results or {}).get(device)
    if not isinstance(result, dict):
        return replace(case, device_results={})
    overrides: dict[str, str] = {}
    for name in _DEVICE_OVERRIDE_FIELDS:
        if name in result and result[name] is not None:
            value = result[name]
            overrides[name] = value if isinstance(value, str) else str(value)
    return replace(case, device_results={}, **overrides)


def device_views(cases: list[TestCase], device: str) -> list[TestCase]:
    """Return the device-scoped view of every case (see :func:`device_view`)."""
    return [device_view(c, device) for c in cases]


def device_summary_counts(cases: list[TestCase]) -> dict[str, dict[str, int]]:
    """Return ``{device: {total, passed, failed}}`` from per-device results.

    Only devices present in the model appear (canonical order). ``total`` counts
    the cases that carry a result for that device; ``passed`` / ``failed`` count
    that device's ``status``.
    """
    summary: dict[str, dict[str, int]] = {}
    for device in devices_tested(cases):
        total = passed = failed = 0
        for case in cases:
            result = (case.device_results or {}).get(device)
            if not isinstance(result, dict):
                continue
            total += 1
            status = str(result.get("status", "")).strip()
            if status == "Pass":
                passed += 1
            elif status == "Fail":
                failed += 1
        summary[device] = {"total": total, "passed": passed, "failed": failed}
    return summary
