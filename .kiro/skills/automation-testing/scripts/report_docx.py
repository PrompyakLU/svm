"""DOCX writer for the QA automation skill's Reporting_Engine.

Renders the single source of truth (``report_model.TestCase`` list, loaded from
``test_cases.json``) into a **standard, sectioned Test Execution Report** Word
document that follows a company template with bilingual ไทย (English) headings:
cover block, document approval, revision history, a **pre-populated** table of
contents (a real Word ``TOC`` field whose cached result already lists every
section/subsection heading, so it displays a complete contents list the moment
the file opens — no "update fields?" popup and nothing to click), and numbered
sections 1-8. Page numbers are not baked in; the field is marked dirty so Word
fills them in automatically when the document is printed or exported to PDF
(and on a manual F9), without prompting on open.

The report is **data-driven**:

* The Executive Summary's pass/fail-by-test-type table and every count are
  tallied directly from the in-memory model — never re-derived from the XLSX
  report — so the two formats stay in agreement by construction.
* Detailed-result subsections are emitted only for the QA layers (and the
  Performance / Security categories) that actually have Test_Cases; empty
  placeholder sections are never padded in.

The detailed-result tables (``TC ID | user_story_id | Scenario | Status |
Notes``) are the machine-readable surface the relaxed report parity check
relies on: every Test_Case id from the XLSX ``TestCases`` sheet also appears in
these tables with the same status.

Optional run metadata is read from a ``report_meta.json`` file (see
``load_meta`` / ``normalize_meta``). Any field the metadata does not supply
renders as an em dash ("—") — never a raw ``{{placeholder}}``.

Design reference: design.md "DOCX Writer" and "Report Parity".

CLI (skill-relative invocation):
    python .kiro/skills/automation-testing/scripts/report_docx.py \
        <test_cases.json> <out.docx> [report_meta.json]
"""

from __future__ import annotations

import json
import os
import re
import sys
from datetime import datetime

from docx import Document
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT, WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_TAB_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Emu, Inches, Mm, Pt, RGBColor

import labels
import report_model
from report_model import TestCase


def _content_samples(meta: dict | None, cases) -> list[str]:
    """Collect representative text used to auto-detect the report language."""
    samples: list[str] = []
    if isinstance(meta, dict):
        narr = _extract_narrative(meta)
        for v in narr.values():
            if isinstance(v, (list, tuple)):
                samples.extend(str(i) for i in v)
            else:
                samples.append(str(v))
        for key in ("coverage", "verdict"):
            samples.append(str(meta.get(key, "")))
    for c in (cases or [])[:12]:
        samples.append(getattr(c, "scenario", "") or "")
        samples.append(getattr(c, "module", "") or "")
    return samples


def _report_language(meta: dict | None, cases) -> str:
    """Resolve the CONTENT language (used for auto-detect / body context)."""
    return labels.resolve_language(meta, *_content_samples(meta, cases))


def _heading_language(meta: dict | None, cases=None) -> str:
    """Resolve the language of the SECTION/DOCUMENT HEADINGS only — the numbered
    section & subsection titles and the document-structure headings (approval,
    revision history, table of contents, list of tables/figures).

    Defaults to **English**: the headings need not be in Thai even for a Thai
    report. Override only via ``report_meta.heading_language`` ("th" / "en").
    Everything else — body prose, table column headers, captions, cover, footer,
    glossary — follows the CONTENT language (:func:`_report_language`), so a Thai
    report reads as **English headings over a Thai body**. Table column headers
    therefore follow the content language; the parity check resolves the same
    content language so its header row always matches the writer's.
    """
    if isinstance(meta, dict):
        import re as _re
        for k, v in meta.items():
            if _re.sub(r"[^a-z0-9]", "", str(k).lower()) == "headinglanguage":
                lv = str(v).strip().lower()
                if lv in ("th", "en"):
                    return lv
    return "en"


# ---------------------------------------------------------------------------
# Typography configuration (edit these to restyle the whole document).
# ---------------------------------------------------------------------------
# Thai and English share TH Sarabun New so Thai glyphs render in the body font.
# CRITICAL: every run/style sets the complex-script font (w:rFonts w:cs) and the
# complex-script size (w:szCs) too, otherwise Word falls back to a default font
# for the Thai (complex-script) runs and TH Sarabun New is ignored.
# SINGLE SOURCE for every font + size in the .docx — change here to restyle the
# whole document. TH Sarabun New carries both Thai and Latin glyphs (a standard
# Thai official-document font), so English headings + Thai body render consistently.
# Sizes tuned to a professional report standard (this font renders a touch small,
# so body ~14pt / H1 18 / H2 16 / H3 14 reads cleanly and not oversized).
THAI_FONT = "TH Sarabun New"
ENG_FONT = "TH Sarabun New"
BODY_SIZE = 14      # Normal / body text (รายละเอียด)
H1 = 18             # Heading 1 (หัวข้อใหญ่ / section titles)
H2 = 16             # Heading 2 (subsections)
H3 = 16             # Heading 3
TABLE_HEADER = 10   # table header cells (bold) — ตาราง
TABLE_BODY = 10     # table body cells — ตาราง
CAPTION_SIZE = 10   # caption under a table/figure (ข้อความใต้รูป/ใต้ตาราง)
HF_SIZE = 9         # header / footer text
# (Cover-page sizes are set inline in _write_cover and are left unchanged.)


# Rendered for any metadata field that is missing or blank. Never emit a raw
# "{{placeholder}}" token.
DASH = "—"

# All display strings (title, headings, table headers, prose) are language-
# neutral and live in labels.py; the writer selects a set via ``doc._L``.

# Preferred ordering for QA-layer / category groups in the report.
_PREFERRED_GROUP_ORDER: tuple[str, ...] = (
    "UI",
    "API",
    "Unit-Integration",
    "Performance",
    "Security",
)


# ---------------------------------------------------------------------------
# Metadata loading & normalization (report_meta.json -> flat canonical dict)
# ---------------------------------------------------------------------------

# Canonical metadata field -> accepted source-key aliases (compared after
# normalizing keys to lowercase alphanumerics, so "systemName", "system_name"
# and "systemname" all match).
_META_ALIASES: dict[str, tuple[str, ...]] = {
    "company": ("company", "organization", "org"),
    "system_name": ("systemname", "system", "projectname"),
    "project_company": ("projectcompany", "companyth", "companythai"),
    "project_company_en": ("projectcompanyen", "companyen"),
    "version": ("version", "ver", "documentversion", "docversion"),
    "created_date": ("createddate", "created", "createdon", "datecreated", "documentdate"),
    "updated_date": ("updateddate", "updated", "lastupdated", "lastmodified", "modifieddate"),
    "executed_date": ("executeddate", "date", "reportdate", "testdate"),
    "start_date": ("startdate", "teststartdate"),
    "end_date": ("enddate", "testenddate"),
    "environment": ("environment", "env"),
    "os": ("os", "operatingsystem"),
    "node_version": ("nodeversion", "node"),
    "browser_version": ("browserversion", "browser"),
    "memory": ("memory", "ram"),
    "playwright_version": ("playwrightversion", "playwright"),
    "app_url": ("appurl", "baseurl", "url", "applicationurl"),
    "api_base": ("apibase", "apibaseurl", "apiurl"),
    "author": ("author", "preparedby", "testedby"),
    "tool": ("tool", "framework", "testtool", "automationtool"),
    "scope": ("scope", "testscope", "coveragescope"),
    "coverage": ("coverage", "coveragesummary"),
    "verdict": ("verdict", "overallverdict", "conclusion"),
    "test_types": ("testtypes", "testtype", "types"),
}

# Scalar canonical fields (everything except the structured lists below).
_META_SCALAR_FIELDS: tuple[str, ...] = tuple(_META_ALIASES)


def _norm_key(key: object) -> str:
    """Normalize a mapping key to lowercase alphanumerics for alias matching."""
    return re.sub(r"[^a-z0-9]", "", str(key).lower())


def load_meta(path: str | None) -> dict:
    """Load run metadata from a JSON file, tolerating absence/parse errors.

    Returns an empty dict when ``path`` is falsy, missing, or unreadable so the
    report still renders (with "—" for every field).
    """
    if not path:
        return {}
    try:
        with open(path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (OSError, json.JSONDecodeError, ValueError):
        return {}
    return data if isinstance(data, dict) else {}


def _build_scalar_lookup(meta: dict) -> dict[str, str]:
    """Flatten nested dicts into a normalized-key -> value scalar lookup.

    Lists are skipped here (handled separately for test_accounts). The first
    non-empty value found for a normalized key wins.
    """
    lookup: dict[str, str] = {}

    def walk(obj: object) -> None:
        if not isinstance(obj, dict):
            return
        for key, value in obj.items():
            if isinstance(value, dict):
                walk(value)
            elif isinstance(value, list):
                continue
            else:
                nk = _norm_key(key)
                if value not in (None, "") and nk not in lookup:
                    lookup[nk] = value

    walk(meta)
    return lookup


def _find_accounts(meta: dict) -> list[dict]:
    """Locate the test-accounts list anywhere in the (possibly nested) meta."""

    def walk(obj: object):
        if not isinstance(obj, dict):
            return None
        for key, value in obj.items():
            if isinstance(value, list) and _norm_key(key) in ("testaccounts", "accounts"):
                return [item for item in value if isinstance(item, dict)]
            if isinstance(value, dict):
                found = walk(value)
                if found is not None:
                    return found
        return None

    return walk(meta) or []


def normalize_meta(meta: dict | None) -> dict:
    """Normalize flat-or-nested metadata into a flat canonical dict.

    Every scalar field in :data:`_META_ALIASES` is present in the result as a
    string, defaulting to "—" when missing/blank. ``test_types`` is rendered as
    a comma-joined string when supplied as a list. ``test_accounts`` is a list
    of ``{"role", "username", "privileges"}`` dicts (each field defaulting to
    "—"); absent when none are supplied.
    """
    meta = meta or {}
    lookup = _build_scalar_lookup(meta)

    result: dict[str, object] = {}
    for field in _META_SCALAR_FIELDS:
        value: object = DASH
        for alias in _META_ALIASES[field]:
            if alias in lookup:
                raw = lookup[alias]
                if isinstance(raw, list):
                    raw = ", ".join(str(item) for item in raw)
                text = str(raw).strip()
                if text:
                    value = text
                    break
        result[field] = value

    # Created / Updated dates: fall back sensibly so the documents always show
    # a creation date and a last-updated date. "Updated" defaults to the report
    # generation date (today); "Created" defaults to the test start date when
    # given, else today. Author-supplied values in report_meta always win.
    today = datetime.now().strftime("%Y-%m-%d")
    # The report is generated on the run date, so every date field defaults to
    # today when the metadata does not supply one — the document must NEVER show
    # "—" for a date. Author-supplied values always win.
    #   executed_date  → the test/report date (cover + Executive Summary)
    #   start_date / end_date → the test window (Executive Summary 4.1)
    if result.get("executed_date") in (DASH, "", None):
        result["executed_date"] = today
    if result.get("start_date") in (DASH, "", None):
        result["start_date"] = result["executed_date"]
    if result.get("end_date") in (DASH, "", None):
        result["end_date"] = result["executed_date"]
    if result.get("updated_date") in (DASH, "", None):
        result["updated_date"] = today
    if result.get("created_date") in (DASH, "", None):
        start = result.get("start_date")
        result["created_date"] = start if start not in (DASH, "", None) else today

    # Version defaults to the initial version "1.0" when none is supplied, so a
    # fresh report always carries a sensible starting version rather than "—".
    if result.get("version") in (DASH, "", None):
        result["version"] = "1.0"

    accounts = []
    for item in _find_accounts(meta):
        item_lookup = {_norm_key(k): v for k, v in item.items()}
        accounts.append(
            {
                "role": _account_field(item_lookup, ("role", "type")),
                "username": _account_field(item_lookup, ("username", "user", "login", "email")),
                "privileges": _account_field(item_lookup, ("privileges", "permissions", "access", "role")),
            }
        )
    result["test_accounts"] = accounts

    # Carry the PLANNED device list (from report_meta.devices) through the
    # normalized meta so both writers can show the intended responsive matrix in
    # the PREVIEW (before any run populates per-device results). Post-run, the
    # actual devices_tested() take precedence over this planned fallback.
    result["planned_devices"] = report_model.planned_devices(meta)
    return result


def _account_field(item_lookup: dict, aliases: tuple[str, ...]) -> str:
    for alias in aliases:
        if alias in item_lookup:
            text = str(item_lookup[alias]).strip()
            if text:
                return text
    return DASH


def _m(meta: dict, field: str) -> str:
    """Fetch a normalized scalar metadata field, defaulting to '—'."""
    value = meta.get(field, DASH)
    text = str(value).strip()
    return text if text else DASH


# Known tool display names (lowercase key -> nicely-cased label). Anything not
# listed is capitalized generically so "playwright" -> "Playwright".
_TOOL_DISPLAY = {
    "playwright": "Playwright", "cypress": "Cypress", "webdriverio": "WebdriverIO",
    "selenium": "Selenium", "puppeteer": "Puppeteer", "jest": "Jest",
    "vitest": "Vitest", "mocha": "Mocha", "pytest": "pytest", "phpunit": "PHPUnit",
    "junit": "JUnit", "playwright-test": "Playwright Test",
}


def _prettify_tool(value: str) -> str:
    """Return a nicely-cased tool name (e.g. 'playwright' -> 'Playwright')."""
    if not value or value == DASH:
        return value
    key = value.strip().lower()
    return _TOOL_DISPLAY.get(key, value.strip()[:1].upper() + value.strip()[1:])


class _SafeFmt(dict):
    """Mapping for ``str.format_map`` that yields '' for any missing key."""
    def __missing__(self, key):  # noqa: D401
        return ""


def _pfmt(doc, template: str, m: dict, **extra) -> str:
    """Format a prose template with the enterprise context (company/system/…).

    Fills ``{company}`` / ``{org}`` with the organization name (a neutral,
    language-appropriate phrase when none was supplied, so a missing company
    never renders as "—"), ``{system}`` with the system under test, plus
    ``{environment}`` and ``{test_types}``. Unknown placeholders resolve to ''
    (never a KeyError), so any prose string is safe to pass through here.
    """
    lang = getattr(doc, "_lang", "en")
    company = _m(m, "company")
    system = _m(m, "system_name")
    env = _m(m, "environment")
    types = _m(m, "test_types")
    if lang == "th":
        org = company if company != DASH else "หน่วยงานผู้จัดทำ"
        sysname = system if system != DASH else "ระบบที่อยู่ระหว่างการทดสอบ"
        env = env if env != DASH else "สภาพแวดล้อมที่กำหนด"
        types = types if types != DASH else "ตามที่ระบุในเอกสารฉบับนี้"
    else:
        org = company if company != DASH else "the organization"
        sysname = system if system != DASH else "the system under test"
        env = env if env != DASH else "the designated environment"
        types = types if types != DASH else "those defined in this report"
    ctx = {"company": org, "org": org, "system": sysname,
           "environment": env, "test_types": types}
    ctx.update(extra)
    try:
        return template.format_map(_SafeFmt(ctx))
    except Exception:
        return template


# Friendly layer / device names per language, for data-driven scope prose.
_LAYER_NAMES = {
    "en": {"UI": "user-interface (UI) testing", "API": "service-layer (API) testing",
           "Unit-Integration": "unit and integration testing"},
    "th": {"UI": "การทดสอบส่วนติดต่อผู้ใช้ (UI)", "API": "การทดสอบระดับบริการ (API)",
           "Unit-Integration": "การทดสอบระดับหน่วยและการผสานรวม (Unit/Integration)"},
}
_DEVICE_NAMES = {
    "en": {"desktop": "Desktop", "tablet": "Tablet", "mobile": "Mobile"},
    "th": {"desktop": "เดสก์ท็อป (Desktop)", "tablet": "แท็บเล็ต (Tablet)", "mobile": "มือถือ (Mobile)"},
}


def _join_list(items: list[str], lang: str) -> str:
    """Join items with a language-appropriate conjunction (…, … and …)."""
    items = [i for i in items if i]
    if not items:
        return ""
    if len(items) == 1:
        return items[0]
    conj = " และ" if lang == "th" else ", and " if len(items) > 2 else " and "
    if lang == "th":
        return " ".join(items[:-1]) + conj + items[-1]
    return (", ".join(items[:-1]) + conj + items[-1]) if len(items) > 2 else items[0] + conj + items[1]


def _scope_facts(doc, cases, m) -> dict:
    """Compute DATA-DRIVEN scope facts from the actual cases (never hard-coded).

    Returns localized fragments describing exactly what THIS run tested:
    ``layers`` (the QA layers actually present, with the UI sub-types it used),
    ``devices`` (the responsive viewports actually exercised) and
    ``devices_clause`` (a ready-to-embed phrase, empty when no per-device data),
    plus the prettified ``tool``. These feed the Scope / Out-of-Scope /
    Methodology prose so the confirmation document states only what was really
    executed.
    """
    lang = getattr(doc, "_lang", "en")
    cases = cases or []
    present: list[str] = []
    for c in cases:
        lyr = (c.qa_layer or "").strip()
        if lyr and lyr not in present:
            present.append(lyr)
    order = ["UI", "API", "Unit-Integration"]
    present = [l for l in order if l in present] + [l for l in present if l not in order]

    names = _LAYER_NAMES.get(lang, _LAYER_NAMES["en"])
    types = {(c.test_type or "").strip() for c in cases}
    layer_phrases: list[str] = []
    for lyr in present:
        phrase = names.get(lyr, lyr)
        if lyr == "UI":
            e2e, func = "UI-E2E" in types, "UI-Functional" in types
            if e2e and func:
                phrase += (" (ทั้งแบบครบวงจร End-to-End และเชิงฟังก์ชัน)" if lang == "th"
                           else " (both end-to-end journeys and functional checks)")
            elif e2e:
                phrase += (" (แบบครบวงจร End-to-End)" if lang == "th" else " (end-to-end journeys)")
            elif func:
                phrase += (" (เชิงฟังก์ชัน)" if lang == "th" else " (functional checks)")
        layer_phrases.append(phrase)

    if layer_phrases:
        layers = _join_list(layer_phrases, lang)
    else:
        layers = ("ประเภทการทดสอบที่กำหนดไว้สำหรับงานนี้" if lang == "th"
                  else "the test types defined for this engagement")

    dev_names = _DEVICE_NAMES.get(lang, _DEVICE_NAMES["en"])
    # Actual devices when a run happened; otherwise the PLANNED devices from
    # report_meta so the Scope prose still states the responsive coverage.
    device_keys = report_model.devices_tested(cases) or list((m or {}).get("planned_devices") or [])
    is_planned = not report_model.devices_tested(cases) and bool(device_keys)
    devs = [dev_names.get(d, d.capitalize()) for d in device_keys]
    devices = _join_list(devs, lang)
    if devs:
        _planned_th = " (ตามแผน)" if is_planned else ""
        _planned_en = " (planned)" if is_planned else ""
        devices_clause = (f" โดยดำเนินการซ้ำข้ามอุปกรณ์หลายขนาดหน้าจอ ได้แก่ {devices}{_planned_th}"
                          if lang == "th" else f", across the {devices} device viewports{_planned_en}")
    else:
        devices_clause = ""

    return {"layers": layers, "devices": devices, "devices_clause": devices_clause,
            "tool": _prettify_tool(_m(m, "tool"))}


# ---------------------------------------------------------------------------
# Authored narrative extraction (report_meta.json -> per-section prose)
# ---------------------------------------------------------------------------
# The orchestrator may author real, project-specific prose per report section
# and pass it in the meta under a nested object keyed (case-insensitively) by
# `narrative` / `report` / `sections`. Each value is either free-text (str) or
# a bullet list (array of strings). When a section field is unauthored we fall
# back to today's computed/default sentence — never a raw "{{placeholder}}".

# Accepted top-level keys (normalized) that may hold the narrative object.
_NARRATIVE_CONTAINER_KEYS: tuple[str, ...] = ("narrative", "report", "sections")


def _extract_narrative(meta: dict) -> dict:
    """Find the authored-narrative object in ``meta`` and normalize its keys.

    Searches the (possibly nested) meta for a dict held under any of
    :data:`_NARRATIVE_CONTAINER_KEYS` (compared via :func:`_norm_key`). Returns
    a dict mapping normalized key -> value (str or list). Tolerates absence by
    returning an empty dict.
    """
    if not isinstance(meta, dict):
        return {}

    def walk(obj: object) -> dict | None:
        if not isinstance(obj, dict):
            return None
        for key, value in obj.items():
            if _norm_key(key) in _NARRATIVE_CONTAINER_KEYS and isinstance(value, dict):
                return value
        for value in obj.values():
            if isinstance(value, dict):
                found = walk(value)
                if found is not None:
                    return found
        return None

    found = walk(meta)
    if not isinstance(found, dict):
        return {}
    return {_norm_key(k): v for k, v in found.items()}


def _say(narr: dict, key: str, default: str) -> str:
    """Return the authored string for ``key`` (normalized) if non-empty, else ``default``.

    A list value is joined into a single sentence so a stray array still renders
    cleanly. Anything blank falls back to ``default``.
    """
    value = narr.get(_norm_key(key))
    if isinstance(value, (list, tuple)):
        text = " ".join(str(item).strip() for item in value if str(item).strip())
    elif value is None:
        text = ""
    else:
        text = str(value).strip()
    return text if text else default


def _say_list(narr: dict, key: str) -> list[str]:
    """Return the authored list for ``key`` (normalized), else an empty list.

    A single non-empty string is wrapped into a one-item list; an array is
    filtered to its non-empty string items. Absence yields ``[]``.
    """
    value = narr.get(_norm_key(key))
    if isinstance(value, (list, tuple)):
        return [str(item).strip() for item in value if str(item).strip()]
    if value is None:
        return []
    text = str(value).strip()
    return [text] if text else []


def _add_bullets(doc, items: list[str]) -> None:
    """Render each item as a 'List Bullet' paragraph."""
    for item in items:
        doc.add_paragraph(item, style="List Bullet")


# ---------------------------------------------------------------------------
# Model tallies (computed once, from the model only)
# ---------------------------------------------------------------------------

def _group_key(case: TestCase) -> str:
    """Map a Test_Case to its detailed-results group.

    Performance / Security categories form their own groups; otherwise the
    case's ``qa_layer`` is the group. This keeps every case in exactly one
    detailed table (no double-listing) which keeps id+status parity clean.
    """
    category = (case.category or "").lower()
    if "performance" in category:
        return "Performance"
    if "security" in category:
        return "Security"
    return case.qa_layer or "Unspecified"


def _ordered_groups(keys) -> list[str]:
    """Order group keys: preferred order first, then any others sorted."""
    present = list(dict.fromkeys(keys))
    preferred = [k for k in _PREFERRED_GROUP_ORDER if k in present]
    others = sorted(k for k in present if k not in _PREFERRED_GROUP_ORDER)
    return preferred + others


def layer_summary_rows(cases: list[TestCase]) -> list[dict[str, object]]:
    """Compute the Executive Summary pass/fail rows, one per qa_layer present.

    Each row has ``layer``, ``total``, ``passed`` (status == "Pass"), ``failed``
    (status == "Fail") and ``pass_rate`` (passed / total). A final ``TOTAL`` row
    aggregates every case. All values are tallied from the model only.
    """
    layers = _ordered_groups(c.qa_layer or "Unspecified" for c in cases)
    rows: list[dict[str, object]] = []
    for layer in layers:
        group = [c for c in cases if (c.qa_layer or "Unspecified") == layer]
        rows.append(_tally_row(layer, group))
    if cases:
        rows.append(_tally_row("TOTAL", cases))
    return rows


def _tally_row(label: str, group: list[TestCase]) -> dict[str, object]:
    total = len(group)
    passed = sum(1 for c in group if c.status == "Pass")
    failed = sum(1 for c in group if c.status == "Fail")
    pass_rate = (passed / total) if total else 0.0
    return {
        "layer": label,
        "total": total,
        "passed": passed,
        "failed": failed,
        "pass_rate": pass_rate,
    }


def _severity_counts(cases: list[TestCase]) -> dict[str, int]:
    """Count defect severities from the shared DefectLog (failed/blocked cases).

    Delegates to :func:`report_model.defect_severity_counts` so the section 6
    severity summary, the defect table, and the XLSX DefectLog never drift.
    """
    return report_model.defect_severity_counts(cases)


def _pct(value: float) -> str:
    return f"{value:.1%}"


# ---------------------------------------------------------------------------
# Low-level docx helpers
# ---------------------------------------------------------------------------

# Built-in table style: a clean, minimal single-border grid. The header row is
# given a light-blue fill and bold BLACK text in :func:`_add_table`, matching
# the professional look of the companion XLSX workbook header band.
_PREFERRED_TABLE_STYLE = "Table Grid"
_FALLBACK_TABLE_STYLE = "Table Grid"

# Light-blue fill for the header row of every table (professional look). Because
# the fill is light, header text is BLACK + bold so it stays readable.
_TABLE_HEADER_FILL = "EBF4FF"

# Dark black color for the 1/2 pt (0.5pt) table grid lines.
_TABLE_BORDER_COLOR = "000000"

# Black text everywhere (overrides Word's default heading blue).
_BLACK = RGBColor(0x00, 0x00, 0x00)

# ---------------------------------------------------------------------------
# Color palette (mirrors the organization report template
# Templete/รายงานผลการทดสอบระบบ.docx).
# ---------------------------------------------------------------------------
# Dark charcoal — all section headings (professional, near-black but softer).
_HEADING_COLOR = RGBColor(0x1F, 0x29, 0x37)
# Medium grey — secondary / subtitle / meta text (cover meta, header, footer).
_SUBTITLE_COLOR = RGBColor(0x6B, 0x72, 0x80)
# Accent blue — organization name on the cover.
_ACCENT_COLOR = RGBColor(0x1F, 0x4E, 0x79)
# Danger red — the CONFIDENTIAL marking.
_DANGER_COLOR = RGBColor(0xDC, 0x26, 0x26)
# Hex forms for OOXML-level writes.
_HEADING_HEX = "1F2937"
_SUBTITLE_HEX = "6B7280"


def set_run_font(run, name: str, size: int, bold: bool = False) -> None:
    """Set a run's font for BOTH latin and complex-script (Thai) text.

    Writes ``w:rFonts`` ascii/hAnsi **and cs** (complex script) to ``name`` and
    both ``w:sz`` and ``w:szCs`` to ``size`` so Thai glyphs render in ``name``
    at the right size instead of falling back to a default font.
    """
    run.font.bold = bold
    rpr = run._element.get_or_add_rPr()

    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    for attr in ("w:ascii", "w:hAnsi", "w:cs"):
        rfonts.set(qn(attr), name)

    half = str(int(round(size * 2)))
    for tag in ("w:sz", "w:szCs"):
        node = rpr.find(qn(tag))
        if node is None:
            node = OxmlElement(tag)
            rpr.append(node)
        node.set(qn("w:val"), half)


def _set_style_font(style, name: str, size: int, bold: bool) -> None:
    """Apply the same latin+complex-script font/size to a paragraph style.

    Sets the style's ``w:rFonts`` ascii/hAnsi/**cs** and ``w:sz``/``w:szCs`` so
    every paragraph using the style renders Thai in ``name`` by default.
    """
    style.font.name = name
    style.font.size = Pt(size)
    style.font.bold = bold

    rpr = style.element.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = OxmlElement("w:rFonts")
        rpr.insert(0, rfonts)
    for attr in ("w:ascii", "w:hAnsi", "w:cs"):
        rfonts.set(qn(attr), name)

    half = str(int(round(size * 2)))
    for tag in ("w:sz", "w:szCs"):
        node = rpr.find(qn(tag))
        if node is None:
            node = OxmlElement(tag)
            rpr.append(node)
        node.set(qn("w:val"), half)


# Heading level -> (point size). All headings are bold in THAI_FONT.
_HEADING_SIZES: dict[int, int] = {1: H1, 2: H2, 3: H3}


def _configure_styles(doc) -> None:
    """Configure Normal + Heading 1/2/3 styles in TH Sarabun New (latin + cs).

    Every style's font color is forced to BLACK so Word's default heading blue
    is overridden; headings stay bold (black bold).
    """
    _set_style_font(doc.styles["Normal"], THAI_FONT, BODY_SIZE, bold=False)
    _set_style_font(doc.styles["Heading 1"], THAI_FONT, H1, bold=True)
    _set_style_font(doc.styles["Heading 2"], THAI_FONT, H2, bold=True)
    _set_style_font(doc.styles["Heading 3"], THAI_FONT, H3, bold=True)
    # Body text is black; headings take the template's dark-charcoal color.
    doc.styles["Normal"].font.color.rgb = _BLACK
    for style_name in ("Heading 1", "Heading 2", "Heading 3"):
        doc.styles[style_name].font.color.rgb = _HEADING_COLOR


def _configure_page(doc) -> None:
    """Set A4 page size with narrow left/right margins so tables get more width.

    Top/bottom stay 2.5cm; left/right are reduced to 1.5cm so every full-width
    table (especially the wide detailed-result tables) has noticeably roomier
    cells — the usable content width grows from 16cm to 18cm.
    """
    section = doc.sections[0]
    section.page_width = Mm(210)
    section.page_height = Mm(297)
    section.top_margin = Cm(2.5)
    section.bottom_margin = Cm(2.5)
    section.left_margin = Cm(1.5)
    section.right_margin = Cm(1.5)


def _disable_proofing(doc) -> None:
    """Hide Word's spelling/grammar proofing marks (red/green wavy underlines).

    Writes ``w:hideSpellingErrors`` and ``w:hideGrammaticalErrors`` into the
    document settings so no proofing squiggle is drawn under any text (Thai or
    English), regardless of the reader's proofing language.
    """
    settings = doc.settings.element
    for tag in ("w:hideSpellingErrors", "w:hideGrammaticalErrors"):
        if settings.find(qn(tag)) is None:
            node = OxmlElement(tag)
            node.set(qn("w:val"), "true")
            settings.insert(0, node)


def _add_heading(doc, text: str, level: int):
    """Add a heading and force TH Sarabun New (latin + complex script) on it.

    Every heading is marked ``keep_with_next`` so Word never leaves a heading
    stranded at the bottom of a page, separated from the content that follows
    it (this single helper is the choke point for all headings).
    """
    heading = doc.add_heading(text, level=level)
    heading.paragraph_format.keep_with_next = True
    size = _HEADING_SIZES.get(level, H3)
    bold = level >= 1
    for run in heading.runs:
        set_run_font(run, THAI_FONT, size, bold=bold)
        run.font.color.rgb = _HEADING_COLOR
    return heading


def _apply_paragraph_formatting(doc) -> None:
    """Give body prose an official-document look: first-line indent + justify.

    Walks body-level paragraphs and, for genuine prose (skipping headings,
    captions, list bullets, centered/right cover and caption lines, and empty
    spacers), applies a first-line indent, justified alignment, comfortable line
    spacing, and consistent paragraph spacing. Table-cell paragraphs are not in
    ``doc.paragraphs`` so tables are left compact and untouched. Must be called
    BEFORE the TOC / List-of fields are inserted so their entries keep their own
    tabbed layout.
    """
    for paragraph in doc.paragraphs:
        style_name = (getattr(paragraph.style, "name", "") or "")
        if style_name.startswith("Heading"):
            continue
        if style_name == "Caption" or "List" in style_name or "TOC" in style_name.upper():
            continue
        text = paragraph.text.strip()
        if not text:
            continue
        # Skip numbered captions ("ตารางที่ N : ...", "รูปที่ N : ...") which fall
        # back to the Normal style when the template lacks a Caption style.
        if text.startswith("ตารางที่") or text.startswith("รูปที่"):
            continue
        if paragraph.alignment in (WD_ALIGN_PARAGRAPH.CENTER, WD_ALIGN_PARAGRAPH.RIGHT):
            continue
        # Thai body is LEFT-aligned (see below), so we KEEP the author's natural
        # spaces (Thai uses spaces as phrase separators) — do NOT tighten them,
        # which previously collapsed phrase gaps to help justify. Natural spacing
        # + Thai dictionary line-breaking gives the correct, readable Thai look.
        pf = paragraph.paragraph_format
        # Alignment is language-aware to avoid ugly inter-word gaps:
        #   • Thai body → LEFT. Word justifies Thai by stretching the few ASCII
        #     spaces (around English terms / codes), producing large uneven gaps
        #     ("ช่องไฟ" that aren't normal word spaces). LEFT keeps every space at
        #     its natural width — the clean look for Thai; a slightly ragged right
        #     edge is normal and expected for Thai text.
        #   • English body → JUSTIFY. English justifies cleanly at its natural
        #     word spaces, giving a formal flush-both-edges look with no gaps.
        pf.alignment = (
            WD_ALIGN_PARAGRAPH.JUSTIFY
            if getattr(doc, "_lang", "en") == "en"
            else WD_ALIGN_PARAGRAPH.LEFT
        )
        pf.first_line_indent = Cm(1.25)
        pf.space_before = Pt(0)
        pf.space_after = Pt(6)
        pf.line_spacing = 1.15


def _apply_complex_script_fonts(doc) -> None:
    """Final pass: ensure EVERY run sets the complex-script font (w:rFonts cs).

    Walks body paragraphs, table cells, and header/footer paragraphs. For each
    run it sets ``w:rFonts`` ascii/hAnsi/cs to THAI_FONT (without disturbing an
    already-set explicit size) and mirrors any explicit ``w:sz`` into ``w:szCs``
    so Thai never falls back to a substitute font. This is the safety net that
    guarantees Thai rendering across runs that inherited their font from a style.
    """

    def fix_run(run) -> None:
        rpr = run._element.get_or_add_rPr()
        rfonts = rpr.find(qn("w:rFonts"))
        if rfonts is None:
            rfonts = OxmlElement("w:rFonts")
            rpr.insert(0, rfonts)
        for attr in ("w:ascii", "w:hAnsi", "w:cs"):
            rfonts.set(qn(attr), THAI_FONT)
        sz = rpr.find(qn("w:sz"))
        if sz is not None:
            szcs = rpr.find(qn("w:szCs"))
            if szcs is None:
                szcs = OxmlElement("w:szCs")
                rpr.append(szcs)
            szcs.set(qn("w:val"), sz.get(qn("w:val")))
        # Default any run WITHOUT an explicit color to black so body text is
        # solid black; runs that already carry a color (headings in charcoal,
        # cover accent/danger, grey meta) are preserved as authored.
        color = rpr.find(qn("w:color"))
        if color is None:
            color = OxmlElement("w:color")
            rpr.append(color)
            color.set(qn("w:val"), "000000")
        # Tag the complex-script (Thai) language as th-TH so Word applies Thai
        # dictionary line-breaking: lines fill to the right margin (breaking
        # between Thai characters) instead of only at ASCII spaces, which was
        # leaving large ragged gaps on the right and looking unprofessional.
        lang = rpr.find(qn("w:lang"))
        if lang is None:
            lang = OxmlElement("w:lang")
            rpr.append(lang)
        lang.set(qn("w:bidi"), "th-TH")

    def fix_paragraphs(paragraphs) -> None:
        for paragraph in paragraphs:
            for run in paragraph.runs:
                fix_run(run)

    def fix_tables(tables) -> None:
        for table in tables:
            for row in table.rows:
                for cell in row.cells:
                    fix_paragraphs(cell.paragraphs)
                    fix_tables(cell.tables)

    fix_paragraphs(doc.paragraphs)
    fix_tables(doc.tables)
    for section in doc.sections:
        fix_paragraphs(section.header.paragraphs)
        fix_paragraphs(section.footer.paragraphs)


def _verify_docx_readable(path: str) -> None:
    """Integrity guard: reopen the saved .docx and touch its body.

    Re-parsing with python-docx exercises the same OOXML load Word performs, so
    a structurally broken document would raise here rather than corrupting on
    open. Returns normally when the file is valid and editable.
    """
    check = Document(path)
    _ = len(check.paragraphs)
    _ = len(check.tables)


def _apply_table_style(table) -> None:
    """Apply the clean single-border grid style ('Table Grid')."""
    for style_name in (_PREFERRED_TABLE_STYLE, _FALLBACK_TABLE_STYLE):
        try:
            table.style = style_name
            return
        except (KeyError, ValueError):
            continue


def _shade_cell(cell, fill: str) -> None:
    """Apply a solid background fill (hex like 'D9D9D9') to a table cell."""
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), fill)
    tc_pr.append(shd)


def _repeat_header_row(row) -> None:
    """Mark a table row to repeat as a header across page breaks (tblHeader)."""
    tr_pr = row._tr.get_or_add_trPr()
    tbl_header = OxmlElement("w:tblHeader")
    tbl_header.set(qn("w:val"), "true")
    tr_pr.append(tbl_header)


def _no_split_row(row) -> None:
    """Mark a table row so it does not split across a page boundary (cantSplit)."""
    tr_pr = row._tr.get_or_add_trPr()
    cant_split = OxmlElement("w:cantSplit")
    cant_split.set(qn("w:val"), "true")
    tr_pr.append(cant_split)


def _apply_thin_borders(table) -> None:
    """Write explicit thin, dark-black grid borders on ``table``.

    Overrides the heavier default 0.5pt "Table Grid" lines with single 0.5pt
    (``w:sz="4"``) borders in dark black (``000000``) on all six edges
    (top/left/bottom/right/insideH/insideV). Call after the table style is
    applied so it wins.
    """
    tbl_pr = table._tbl.tblPr
    existing = tbl_pr.find(qn("w:tblBorders"))
    if existing is not None:
        tbl_pr.remove(existing)
    borders = OxmlElement("w:tblBorders")
    for edge in ("top", "left", "bottom", "right", "insideH", "insideV"):
        element = OxmlElement(f"w:{edge}")
        element.set(qn("w:val"), "single")
        element.set(qn("w:sz"), "4")
        element.set(qn("w:space"), "0")
        element.set(qn("w:color"), _TABLE_BORDER_COLOR)
        borders.append(element)
    tbl_pr.append(borders)


def _add_table(doc, headers: tuple[str, ...] | list[str], rows: list[list[str]],
               caption: str | None = None):
    """Add a styled table with a bold, repeating header row followed by ``rows``.

    Header cells are TABLE_HEADER pt bold BLACK on a light-blue fill; body cells
    TABLE_BODY pt normal. Cells are top-aligned and wrap. The table uses a FIXED
    layout that spans the full page width with columns divided evenly (matching
    the SRS document skill), plus comfortable cell padding so text never touches
    the grid lines. Grid lines are 1/2 pt (0.5pt), the header row repeats across
    page breaks, and when ``caption`` is given a numbered ``Caption`` paragraph
    is added ABOVE the table so it appears in the List of Tables.
    """
    if caption and hasattr(doc, "_table_caps"):
        _add_caption(doc, "table", caption)
    table = doc.add_table(rows=1, cols=len(headers))
    _apply_table_style(table)
    _apply_thin_borders(table)
    try:
        table.alignment = WD_TABLE_ALIGNMENT.CENTER
    except (KeyError, ValueError):
        pass

    # Lay the table out with a FIXED grid that spans the full page width and
    # divides it evenly across the columns (same approach as the SRS document
    # skill). Fixed layout keeps columns tidy and uniform and prevents Word from
    # squeezing a long header into a sliver that wraps letter-by-letter. Every
    # cell width is set too (Word requires this for a fixed layout to hold).
    section = doc.sections[0]
    usable_emu = int(section.page_width - section.left_margin - section.right_margin)
    col_emu = usable_emu // len(headers)
    table.autofit = False
    table.allow_autofit = False
    tbl_pr = table._tbl.tblPr
    for tag in ("w:tblW", "w:tblLayout", "w:tblCellMar"):
        existing = tbl_pr.find(qn(tag))
        if existing is not None:
            tbl_pr.remove(existing)
    tbl_w = OxmlElement("w:tblW")
    tbl_w.set(qn("w:type"), "dxa")
    tbl_w.set(qn("w:w"), str(int(usable_emu / 635)))  # EMU -> twips
    tbl_pr.append(tbl_w)
    tbl_layout = OxmlElement("w:tblLayout")
    tbl_layout.set(qn("w:type"), "fixed")
    tbl_pr.append(tbl_layout)
    # Comfortable cell padding so text never touches the grid lines. Generous
    # top/bottom padding gives every row extra vertical breathing room.
    cell_mar = OxmlElement("w:tblCellMar")
    for edge, twips in (("top", "120"), ("bottom", "120"), ("left", "108"), ("right", "108")):
        node = OxmlElement(f"w:{edge}")
        node.set(qn("w:w"), twips)
        node.set(qn("w:type"), "dxa")
        cell_mar.append(node)
    tbl_pr.append(cell_mar)
    for column in table.columns:
        column.width = Emu(col_emu)

    header_cells = table.rows[0].cells
    for idx, label in enumerate(headers):
        header_cells[idx].text = str(label)
        header_cells[idx].width = Emu(col_emu)
        header_cells[idx].vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.TOP
        _shade_cell(header_cells[idx], _TABLE_HEADER_FILL)
        for paragraph in header_cells[idx].paragraphs:
            for run in paragraph.runs:
                set_run_font(run, ENG_FONT, TABLE_HEADER, bold=True)
                run.font.color.rgb = _BLACK
    _repeat_header_row(table.rows[0])
    for row in rows:
        cells = table.add_row().cells
        for idx, value in enumerate(row):
            cells[idx].text = str(value)
            cells[idx].width = Emu(col_emu)
            cells[idx].vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.TOP
            for paragraph in cells[idx].paragraphs:
                for run in paragraph.runs:
                    set_run_font(run, THAI_FONT, TABLE_BODY, bold=False)
    return table


def _section_heading(doc, text: str):
    """Add a top-level (level 1) section heading that starts on a new page."""
    heading = _add_heading(doc, text, level=1)
    heading.paragraph_format.page_break_before = True
    return heading


def _add_page_number_field(paragraph) -> None:
    """Append a Word PAGE field (live page number) to ``paragraph``."""
    run = paragraph.add_run()

    begin = OxmlElement("w:fldChar")
    begin.set(qn("w:fldCharType"), "begin")
    instr = OxmlElement("w:instrText")
    instr.set(qn("xml:space"), "preserve")
    instr.text = "PAGE"
    end = OxmlElement("w:fldChar")
    end.set(qn("w:fldCharType"), "end")

    run._r.append(begin)
    run._r.append(instr)
    run._r.append(end)


def _add_footer(doc) -> None:
    """Add a footer showing ONLY the page number, aligned bottom-right.

    No "Page"/"หน้า" word and no "Confidential" marking — just the bare page
    number field, right-aligned, grey, 9pt (a clean, standard look).
    """
    section = doc.sections[0]
    footer = section.footer
    paragraph = footer.paragraphs[0] if footer.paragraphs else footer.add_paragraph()
    paragraph.text = ""
    paragraph.alignment = WD_ALIGN_PARAGRAPH.RIGHT

    _add_page_number_field(paragraph)
    for run in paragraph.runs:
        set_run_font(run, ENG_FONT, HF_SIZE, bold=False)
        run.font.color.rgb = _SUBTITLE_COLOR


def _add_header(doc, m: dict) -> None:
    """Add a page header with the organization name (top-left), grey, on every page.

    The company name comes from the normalized meta ("—" when missing).
    """
    section = doc.sections[0]
    header = section.header
    paragraph = header.paragraphs[0] if header.paragraphs else header.add_paragraph()
    paragraph.text = ""
    paragraph.alignment = WD_ALIGN_PARAGRAPH.LEFT
    run = paragraph.add_run(_m(m, "company"))
    set_run_font(run, ENG_FONT, HF_SIZE, bold=False)
    run.font.color.rgb = _SUBTITLE_COLOR


# A numbered heading text starts with a dotted section number, e.g. "1. ",
# "1.1 ", "2.3.1 ". The depth is the count of dotted parts (1 -> 1, 1.1 -> 2).
_HEADING_NUMBER_RE = re.compile(r"^(\d+(?:\.\d+)*)[.\s]")

# A space sitting BETWEEN two Thai characters. Thai is written without spaces, so
# such spaces are optional; keeping them makes Word break the line at the space
# (leaving the right margin ragged/half-empty) instead of breaking within the
# Thai run to fill the line. Removing only Thai↔Thai spaces (never spaces next to
# Latin letters, digits, parentheses, or punctuation) lets Word's Thai
# line-breaking fill each line to the right margin while English terms, codes and
# parenthetical glosses keep their normal spacing.
_THAI = r"\u0E00-\u0E7F"
_THAI_THAI_SPACE_RE = re.compile(rf"(?<=[{_THAI}])[ \u00A0]+(?=[{_THAI}])")


def _tighten_thai_spaces(text: str) -> str:
    """Drop spaces that sit strictly between two Thai characters (see regex)."""
    return _THAI_THAI_SPACE_RE.sub("", text)


def _collect_toc_entries(doc) -> list[tuple[str, int]]:
    """Collect numbered headings in document order for the cached TOC result.

    Scans ``doc.paragraphs`` and keeps every paragraph whose style name starts
    with "Heading" and whose text begins with a dotted section number (matched
    by :data:`_HEADING_NUMBER_RE`). Returns a list of ``(text, depth)`` where
    ``depth`` is the number of dotted parts in the leading number (1 -> 1,
    1.1 -> 2, 1.1.1 -> 3).
    """
    entries: list[tuple[str, int]] = []
    for paragraph in doc.paragraphs:
        style = paragraph.style
        style_name = getattr(style, "name", "") or ""
        if not style_name.startswith("Heading"):
            continue
        text = (paragraph.text or "").strip()
        match = _HEADING_NUMBER_RE.match(text)
        if not match:
            continue
        depth = len(match.group(1).split("."))
        entries.append((text, depth))
    return entries


def _build_toc_field(doc, anchor):
    """Insert a REAL, PRE-POPULATED Word Table-of-Contents field after ``anchor``.

    Builds a ``TOC \\o "1-3" \\h \\z \\u`` field (headings 1-3, hyperlinked,
    page numbers shown, suppressed where no page applies) wrapped in the
    standard ``begin / instrText / separate / cached-result / end`` field-char
    sequence. The cached result is fully populated: one paragraph per numbered
    heading (collected by :func:`_collect_toc_entries`), so Word shows a
    complete contents list the instant the file opens — no "update fields?"
    popup and nothing to click.

    Page numbers are NOT baked into the cached entries. The ``begin`` fldChar is
    marked ``w:dirty="true"`` so Word refreshes the field — adding real page
    numbers — automatically when the document is printed or exported to PDF, and
    on a manual F9, without any open-time prompt.
    """
    entries = _collect_toc_entries(doc)

    # First paragraph: begin (dirty) + instruction text + separate.
    first = doc.add_paragraph()

    run_begin = first.add_run()
    fld_begin = OxmlElement("w:fldChar")
    fld_begin.set(qn("w:fldCharType"), "begin")
    fld_begin.set(qn("w:dirty"), "true")
    run_begin._r.append(fld_begin)

    run_instr = first.add_run()
    instr = OxmlElement("w:instrText")
    instr.set(qn("xml:space"), "preserve")
    instr.text = 'TOC \\o "1-3" \\h \\z \\u'
    run_instr._r.append(instr)

    run_sep = first.add_run()
    fld_sep = OxmlElement("w:fldChar")
    fld_sep.set(qn("w:fldCharType"), "separate")
    run_sep._r.append(fld_sep)

    # One cached-result paragraph per collected heading, in document order.
    # Bold for top-level entries, indented for deeper levels. No page numbers.
    paragraphs = [first]
    result_paras = []
    for text, depth in entries:
        result_para = doc.add_paragraph()
        result_para.paragraph_format.left_indent = Inches(0.3 * (depth - 1))
        run = result_para.add_run(text)
        set_run_font(run, THAI_FONT, BODY_SIZE, bold=(depth == 1))
        run.font.color.rgb = _BLACK
        result_paras.append(result_para)
        paragraphs.append(result_para)

    # Ensure the field is well-formed: the final cached paragraph ends with the
    # field-end char. When there are no entries the begin paragraph carries it.
    last = result_paras[-1] if result_paras else first
    run_end = last.add_run()
    fld_end = OxmlElement("w:fldChar")
    fld_end.set(qn("w:fldCharType"), "end")
    run_end._r.append(fld_end)

    # Move the freshly-appended paragraphs up to sit just after the anchor,
    # preserving their order (addnext after the previously-inserted paragraph).
    cursor = anchor
    for para in paragraphs:
        cursor._p.addnext(para._p)
        cursor = para
    return first


def _add_seq_field(paragraph, seq_id: str, cached: int) -> None:
    """Append a Word ``SEQ`` auto-number field (e.g. ``SEQ ตาราง``) to a caption.

    The field renders a running number that Word recomputes on F9 / print, so
    captions stay correctly numbered even after edits. ``cached`` is the number
    shown before the first field refresh.
    """
    run_b = paragraph.add_run()
    b = OxmlElement("w:fldChar")
    b.set(qn("w:fldCharType"), "begin")
    b.set(qn("w:dirty"), "true")
    run_b._r.append(b)

    run_i = paragraph.add_run()
    instr = OxmlElement("w:instrText")
    instr.set(qn("xml:space"), "preserve")
    instr.text = f' SEQ {seq_id} \\* ARABIC '
    run_i._r.append(instr)

    run_s = paragraph.add_run()
    s = OxmlElement("w:fldChar")
    s.set(qn("w:fldCharType"), "separate")
    run_s._r.append(s)

    paragraph.add_run(str(cached))

    run_e = paragraph.add_run()
    e = OxmlElement("w:fldChar")
    e.set(qn("w:fldCharType"), "end")
    run_e._r.append(e)


# Seq-id + Thai label per caption kind. The seq-id must match the TOC \c switch.
# Internal SEQ identifiers per caption kind (must match the TOC \c switch and
# stay constant regardless of the display language). The DISPLAYED label comes
# from the resolved language label set.
_CAPTION_SEQ = {"table": "ตาราง", "figure": "รูป"}


def _cap_label(doc, kind: str) -> str:
    key = "cap_label_table" if kind == "table" else "cap_label_figure"
    return doc._L["doc"][key]


def _add_caption(doc, kind: str, text: str, center: bool = False) -> None:
    """Add a numbered, language-aware caption paragraph followed by a SEQ number.

    ``kind`` is "table" or "figure". The caption uses the ``Caption`` style so
    Word's *Insert → Table of Figures* (and our pre-built list fields) collect
    it, and carries a ``SEQ`` field so numbering auto-updates. The caption text
    (without the number) is recorded on the document for the cached list field.
    """
    seq_id = _CAPTION_SEQ[kind]
    label = _cap_label(doc, kind)
    d = doc._L["doc"]
    store = doc._table_caps if kind == "table" else doc._figure_caps
    store.append(text)

    try:
        para = doc.add_paragraph(style="Caption")
    except KeyError:
        para = doc.add_paragraph()
    if center:
        para.alignment = WD_ALIGN_PARAGRAPH.CENTER
    para.add_run(d["cap_prefix"].format(label=label))
    _add_seq_field(para, seq_id, len(store))
    para.add_run(f"{d['cap_sep']}{text}")
    for run in para.runs:
        set_run_font(run, THAI_FONT, CAPTION_SIZE, bold=False)
        run.font.color.rgb = _SUBTITLE_COLOR
    return para


def _build_captions_list(doc, anchor, kind: str, entries: list[str]):
    """Insert a REAL, pre-populated *Table of Figures* field (``TOC \\c``).

    Builds ``TOC \\c "<seq>" \\h \\z`` — the field Word uses for a List of Tables
    / List of Figures — wrapped in the begin/instr/separate/cached-result/end
    field-char sequence, mirroring :func:`_build_toc_field`. The cached result
    lists each caption in document order so the list shows on open; the field is
    marked dirty so Word refreshes real page numbers on F9 / print / PDF export.
    """
    seq_id = _CAPTION_SEQ[kind]
    label = _cap_label(doc, kind)
    d = doc._L["doc"]

    first = doc.add_paragraph()
    run_begin = first.add_run()
    fld_begin = OxmlElement("w:fldChar")
    fld_begin.set(qn("w:fldCharType"), "begin")
    fld_begin.set(qn("w:dirty"), "true")
    run_begin._r.append(fld_begin)

    run_instr = first.add_run()
    instr = OxmlElement("w:instrText")
    instr.set(qn("xml:space"), "preserve")
    instr.text = f'TOC \\c "{seq_id}" \\h \\z'
    run_instr._r.append(instr)

    run_sep = first.add_run()
    fld_sep = OxmlElement("w:fldChar")
    fld_sep.set(qn("w:fldCharType"), "separate")
    run_sep._r.append(fld_sep)

    paragraphs = [first]
    result_paras = []
    if entries:
        for i, text in enumerate(entries, start=1):
            rp = doc.add_paragraph()
            run = rp.add_run(f"{d['cap_prefix'].format(label=label)}{i}{d['cap_sep']}{text}")
            set_run_font(run, THAI_FONT, CAPTION_SIZE, bold=False)
            run.font.color.rgb = _BLACK
            result_paras.append(rp)
            paragraphs.append(rp)
    else:
        rp = doc.add_paragraph()
        run = rp.add_run(d["no_entries"])
        set_run_font(run, THAI_FONT, CAPTION_SIZE, bold=False)
        run.font.color.rgb = _BLACK
        result_paras.append(rp)
        paragraphs.append(rp)

    last = result_paras[-1] if result_paras else first
    run_end = last.add_run()
    fld_end = OxmlElement("w:fldChar")
    fld_end.set(qn("w:fldCharType"), "end")
    run_end._r.append(fld_end)

    cursor = anchor
    for para in paragraphs:
        cursor._p.addnext(para._p)
        cursor = para
    return first


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

def write_docx(cases: list[TestCase], out_path: str, meta: dict | None = None,
               assets_root: str | None = None) -> None:
    """Write the Test_Cases to a standard Test Execution Report ``.docx``.

    Args:
        cases: Ordered list of Test_Cases (the single source of truth).
        out_path: Destination ``.docx`` path.
        meta: Optional run metadata (flat or nested); missing fields render "—".
        assets_root: Optional run directory (``qa_dir``) used to resolve and
            embed real screenshot evidence. Defaults to the parent of the
            document's folder (``.../doc`` -> ``qa_dir``) so images embed with no
            extra wiring when the report is written into the standard tree.
    """
    if assets_root is None:
        doc_dir = os.path.dirname(os.path.abspath(out_path))
        assets_root = os.path.dirname(doc_dir)
    m = normalize_meta(meta)
    # Cases-aware planned devices: if the run recorded none but there ARE UI
    # cases, default to the standard responsive set so the preview still states
    # the device coverage (see report_model.effective_planned_devices).
    m["planned_devices"] = report_model.effective_planned_devices(meta, cases)
    narr = _extract_narrative(meta or {})
    doc = Document()
    # Resolve the report language (explicit meta.language, else auto-detect from
    # content) and attach the matching label set — the code carries NO fixed
    # language; every visible string comes from ``doc._L``.
    doc._lang = _report_language(meta, cases)          # content language (body)
    # Split the label set on two axes so a Thai report reads as English HEADINGS
    # over a Thai BODY (the user's explicit preference — headings need not be
    # Thai even for a Thai report):
    #   • CONTENT language (doc._lang) drives everything that is body/content —
    #     prose, table column headers, captions, cover, footer, glossary, etc.
    #   • HEADING language (default English; override via
    #     report_meta.heading_language) drives ONLY the numbered section /
    #     subsection headings and the document-structure headings (approval,
    #     revision history, table of contents, list of tables/figures).
    # We start from the content set (Thai) and override just the heading groups
    # with the structural set (English), so nothing else changes language.
    _content = labels.get(doc._lang)
    _struct = labels.get(_heading_language(meta, cases))
    _L = dict(_content)
    _L["sections"] = _struct["sections"]
    _L["subs"] = _struct["subs"]
    _doc_labels = dict(_content["doc"])
    for _k in ("approval", "revision", "toc", "lot", "lof", "no_entries"):
        _doc_labels[_k] = _struct["doc"][_k]
    _L["doc"] = _doc_labels
    doc._L = _L
    # Caption stores for the List of Tables / List of Figures fields.
    doc._table_caps = []
    doc._figure_caps = []
    _configure_styles(doc)
    _configure_page(doc)
    _disable_proofing(doc)
    _add_header(doc, m)
    _add_footer(doc)

    _write_cover(doc, m)
    _write_approval(doc, m)
    _write_revision_history(doc, m)
    toc_anchor, lot_anchor, lof_anchor = _write_toc(doc)
    _write_introduction(doc, m, cases, narr)
    _write_strategy(doc, m, cases, narr)
    _write_environment(doc, m, cases)
    _write_executive_summary(doc, m, cases, narr)
    _write_detailed_results(doc, cases, m, assets_root)
    _write_issues(doc, cases)
    _write_conclusion(doc, m, cases, narr)
    _write_appendix(doc, m, cases, assets_root)

    # Give body prose an official-document look (first-line indent + justify)
    # BEFORE inserting the TOC / List-of fields so their tabbed entries are
    # left untouched.
    _apply_paragraph_formatting(doc)

    # Insert a REAL, PRE-POPULATED Word TOC field from the numbered headings
    # just produced — its cached result already lists every section heading so
    # Word shows a complete contents list on open with no "update fields?"
    # popup. The field is marked dirty so page numbers fill in on print / PDF
    # export / F9. Then make one final pass so every run carries the
    # complex-script (Thai) font and renders black (correct for the light-blue
    # table headers too).
    _build_toc_field(doc, toc_anchor)
    # Real List of Tables / List of Figures fields (TOC \c), pre-populated from
    # the captions collected while writing the body; page numbers fill on F9.
    _build_captions_list(doc, lot_anchor, "table", doc._table_caps)
    _build_captions_list(doc, lof_anchor, "figure", doc._figure_caps)
    _apply_complex_script_fonts(doc)

    doc.save(out_path)
    # Integrity: reopen the saved file to assert it is valid and editable.
    _verify_docx_readable(out_path)


def _add_bottom_border(paragraph, color: str = "1F4E79", size: int = 8) -> None:
    """Draw a horizontal rule under ``paragraph`` (like the template Title style)."""
    p_pr = paragraph._p.get_or_add_pPr()
    borders = OxmlElement("w:pBdr")
    bottom = OxmlElement("w:bottom")
    bottom.set(qn("w:val"), "single")
    bottom.set(qn("w:sz"), str(size))
    bottom.set(qn("w:space"), "6")
    bottom.set(qn("w:color"), color)
    borders.append(bottom)
    p_pr.append(borders)


def _write_cover(doc, m: dict) -> None:
    """Render a clean, balanced one-page cover styled after the org template.

    Palette (from Templete/รายงานผลการทดสอบระบบ.docx): the report title in dark
    charcoal with an accent-blue underline rule, the organization in accent
    blue, all run/date/author meta in grey, and CONFIDENTIAL in red.
    """

    def _centered(text: str, *, bold: bool = False, size: int | None = None,
                  color: RGBColor = _BLACK, underline: bool = False):
        para = doc.add_paragraph()
        para.alignment = WD_ALIGN_PARAGRAPH.CENTER
        run = para.add_run(text)
        run.bold = bold
        run.font.color.rgb = color
        if size is not None:
            run.font.size = Pt(size)
        if underline:
            _add_bottom_border(para)
        return para

    # Balanced top spacing so the title block sits in the upper third (not the
    # very top) — professional, without a large empty gap.
    for _ in range(4):
        doc.add_paragraph()

    L = doc._L
    cov = L["cover"]

    # Main report title — dark charcoal, bold (no underline rule).
    _centered(L["report_title"], bold=True, size=28, color=_HEADING_COLOR)

    # System under test — directly below the title at 18pt (the name of the
    # application/system being tested), when supplied. A small gap keeps the
    # spacing balanced. Skipped only when no system name is provided.
    system = _m(m, "system_name")
    if system != DASH:
        para = doc.add_paragraph()
        para.paragraph_format.space_before = Pt(6)
        para.alignment = WD_ALIGN_PARAGRAPH.CENTER
        run = para.add_run(system)
        run.bold = True
        run.font.size = Pt(18)
        run.font.color.rgb = _HEADING_COLOR

    doc.add_paragraph()

    # Organization (single company line) in accent blue.
    _centered(_m(m, "company"), bold=True, size=18, color=_ACCENT_COLOR)

    doc.add_paragraph()

    # Version / date / author / created-updated — grey meta block.
    _centered(cov["version"].format(v=_m(m, "version")), size=13, color=_SUBTITLE_COLOR)
    _centered(cov["date"].format(d=_m(m, "executed_date")), size=12, color=_SUBTITLE_COLOR)
    _centered(cov["prepared_by"].format(a=_m(m, "author")), size=12, color=_SUBTITLE_COLOR)
    _centered(
        cov["created_updated"].format(c=_m(m, "created_date"), u=_m(m, "updated_date")),
        size=12,
        color=_SUBTITLE_COLOR,
    )

    # No confidentiality marking — keep the cover clean (the CONFIDENTIAL label
    # is intentionally omitted so a Thai report never shows "ลับ").
    doc.add_page_break()


def _write_approval(doc, m: dict) -> None:
    L = doc._L
    _add_heading(doc, L["doc"]["approval"], level=1)
    author = _m(m, "author")
    roles = L["approval_rows"]
    # Signature column is left BLANK (not "—") so it can be signed by hand or
    # with a digital signature after the document is printed or shared.
    _add_table(
        doc,
        L["approval_headers"],
        [
            [roles[0], author, "", _m(m, "executed_date")],
            [roles[1], DASH, "", DASH],
            [roles[2], DASH, "", DASH],
        ],
    )


def _write_revision_history(doc, m: dict) -> None:
    L = doc._L
    _add_heading(doc, L["doc"]["revision"], level=1)
    _add_table(
        doc,
        L["revision_headers"],
        [[_m(m, "version"), _m(m, "executed_date"), _m(m, "author"), L["initial_release"]]],
    )


def _write_toc(doc):
    """Add the Table of Contents heading and return it as the insertion anchor.

    A REAL, pre-populated Word TOC field is inserted after this heading once the
    body has been built — see :func:`_build_toc_field`. Its cached result already
    lists every numbered heading, so Word displays a complete contents list the
    moment the file opens with no "update fields?" popup and nothing to click.
    Page numbers are filled in by Word automatically on print / PDF export (and
    on a manual F9), not on open. The TOC heading carries a page-break-before so
    the Table of Contents sits cleanly on its own page.
    """
    d = doc._L["doc"]
    heading = _add_heading(doc, d["toc"], level=1)
    heading.paragraph_format.page_break_before = True
    lot = _add_heading(doc, d["lot"], level=1)
    lot.paragraph_format.page_break_before = True
    lof = _add_heading(doc, d["lof"], level=1)
    lof.paragraph_format.page_break_before = True
    return heading, lot, lof


def _write_introduction(doc, m: dict, cases=None, narr: dict | None = None) -> None:
    narr = narr or {}
    cases = cases or []
    L = doc._L
    S, P = L["subs"], L["prose"]
    _section_heading(doc, L["sections"][1])

    _add_heading(doc, S["1.1"], level=2)
    doc.add_paragraph(
        _say(narr, "introduction",
             _say(narr, "purpose", _pfmt(doc, P["purpose"], m)))
    )

    _add_heading(doc, S["1.2"], level=2)
    facts = _scope_facts(doc, cases, m)
    doc.add_paragraph(
        _say(narr, "scope", _pfmt(doc, P["scope"], m, **facts))
    )

    _add_heading(doc, S["1.3"], level=2)
    doc.add_paragraph(_say(narr, "audience", _pfmt(doc, P["audience"], m)))

    _add_heading(doc, S["1.4"], level=2)
    _add_caption(doc, "table", L["caps"]["glossary"])
    _add_table(doc, L["headers"]["term_definition"],
               [list(row) for row in L["rows"]["glossary"]])

    _add_heading(doc, S["1.5"], level=2)
    references = _say_list(narr, "references")
    if references:
        _add_bullets(doc, references)
    else:
        doc.add_paragraph(f"{L['rows']['app_url']}: {_m(m, 'app_url')}")
        doc.add_paragraph(f"{L['rows']['api_base']}: {_m(m, 'api_base')}")


def _write_strategy(doc, m: dict, cases=None, narr: dict | None = None) -> None:
    narr = narr or {}
    cases = cases or []
    L = doc._L
    facts = _scope_facts(doc, cases, m)
    S, P, R = L["subs"], L["prose"], L["rows"]
    _section_heading(doc, L["sections"][2])

    _add_heading(doc, S["2.1"], level=2)
    doc.add_paragraph(_say(narr, "objectives", _pfmt(doc, P["objectives"], m)))

    _add_heading(doc, S["2.2"], level=2)
    _add_caption(doc, "table", L["caps"]["test_types"])
    # Only list the test types actually covered in THIS run (derived from the
    # cases' qa_layer), and show the REAL tool used — never the "detected
    # framework" label as a value. A UI-only run therefore lists only the UI
    # row, not empty API / Unit rows.
    tool = _prettify_tool(_m(m, "tool"))
    ui_tool = tool if tool != DASH else "Playwright"
    _layer_specs = {
        "UI": ("UI E2E", ui_tool, R["test_type_ui"]),
        "API": ("API", tool, R["test_type_api"]),
        "Unit-Integration": ("Unit-Integration", tool, R["test_type_unit"]),
    }
    present: list[str] = []
    for c in cases:
        lyr = (c.qa_layer or "").strip()
        if lyr and lyr not in present:
            present.append(lyr)
    type_rows = []
    for lyr in present:
        name, tool_val, desc = _layer_specs.get(lyr, (lyr, tool, "—"))
        type_rows.append([name, tool_val, desc])
    if not type_rows:
        type_rows = [["UI E2E", ui_tool, R["test_type_ui"]]]
    _add_table(doc, L["headers"]["test_types"], type_rows)
    test_types_note = _say(narr, "test_types", "")
    if test_types_note:
        doc.add_paragraph(test_types_note)

    _add_heading(doc, S["2.3"], level=2)
    doc.add_paragraph(_say(narr, "out_of_scope", _pfmt(doc, P["out_of_scope"], m, **facts)))

    _add_heading(doc, S["2.4"], level=2)
    doc.add_paragraph(
        _say(narr, "risks_assumptions", _pfmt(doc, P["risks"], m))
    )

    _add_heading(doc, S["2.5"], level=2)
    doc.add_paragraph(_say(narr, "pass_fail_criteria", _pfmt(doc, P["pass_fail"], m)))


# Known viewport pixel sizes per device (mirrors the bundled Playwright config).
_DEVICE_VIEWPORTS: dict[str, str] = {
    "desktop": "1440×900", "tablet": "768×1024", "mobile": "390×844",
}


def _tested_matrix(cases, m: dict | None = None, lang: str = "en") -> str:
    """Describe the responsive / cross-browser matrix (actual, else planned).

    Uses the devices actually exercised (``device_results``) when present. Before
    a run those are empty, so it falls back to the PLANNED devices recorded in
    report_meta (``m['planned_devices']``) and tags them as planned, so the
    environment section always states the responsive coverage instead of "—".
    """
    devices = report_model.devices_tested(cases or [])
    planned = False
    if not devices:
        devices = list((m or {}).get("planned_devices") or [])
        planned = bool(devices)
    if not devices:
        return DASH
    parts = []
    for d in devices:
        vp = _DEVICE_VIEWPORTS.get(d.split("-")[-1])
        parts.append(f"{d} ({vp})" if vp else d)
    matrix = ", ".join(parts)
    if planned:
        matrix += " (วางแผนทดสอบ)" if lang == "th" else " (planned)"
    return matrix


def _write_environment(doc, m: dict, cases=None) -> None:
    L = doc._L
    S, R = L["subs"], L["rows"]
    _section_heading(doc, L["sections"][3])

    _add_heading(doc, S["3.1"], level=2)
    _add_caption(doc, "table", L["caps"]["hardware"])
    _add_table(
        doc,
        L["headers"]["kv_item_value"],
        [
            [R["os"], _m(m, "os")],
            [R["node"], _m(m, "node_version")],
            [R["browser"], _m(m, "browser_version")],
            [R["memory"], _m(m, "memory")],
            ["Devices / Viewports Tested", _tested_matrix(cases, m, getattr(doc, "_lang", "en"))],
        ],
    )

    _add_heading(doc, S["3.2"], level=2)
    _add_caption(doc, "table", L["caps"]["tools"])
    _tool = _prettify_tool(_m(m, "tool"))
    _tool_name = _tool if _tool != DASH else "Playwright"
    _add_table(doc, L["headers"]["tool_version"], [[_tool_name, _m(m, "playwright_version")]])

    _add_heading(doc, S["3.3"], level=2)
    doc.add_paragraph(_pfmt(doc, L["prose"]["test_data"], m))

    _add_heading(doc, S["3.4"], level=2)
    accounts = m.get("test_accounts") or []
    if accounts:
        _add_caption(doc, "table", L["caps"]["accounts"])
        _add_table(
            doc,
            L["headers"]["account"],
            [[a.get("role", DASH), a.get("username", DASH), a.get("privileges", DASH)] for a in accounts],
        )
    else:
        doc.add_paragraph(L["prose"]["no_accounts"])

    _add_heading(doc, S["3.5"], level=2)
    _add_caption(doc, "table", L["caps"]["endpoints"])
    _add_table(
        doc,
        L["headers"]["endpoint_value"],
        [
            [R["app_url"], _m(m, "app_url")],
            [R["api_base"], _m(m, "api_base")],
        ],
    )


def _write_executive_summary(doc, m: dict, cases: list[TestCase], narr: dict | None = None) -> None:
    narr = narr or {}
    L = doc._L
    S, P = L["subs"], L["prose"]
    _section_heading(doc, L["sections"][4])

    # Result counts — every fallback sentence below is computed from these so the
    # prose can never contradict the actual outcome (e.g. never "fix the failures"
    # when nothing failed).
    _total = len(cases)
    _passed = sum(1 for c in cases if c.status == "Pass")
    _failed = sum(1 for c in cases if c.status in ("Fail", "Blocked"))
    _notrun = _total - _passed - _failed
    _executed = _passed + _failed
    _rate = _pct((_passed / _executed) if _executed else 0.0)
    _cfmt = dict(total=_total, passed=_passed, failed=_failed, notrun=_notrun,
                 executed=_executed, rate=_rate)

    _add_heading(doc, f"4.1 {S['4.1']}", level=2)
    # Overview: authored narrative if present, else a computed, result-consistent
    # sentence (never blank).
    doc.add_paragraph(_say(narr, "executive_overview", _pfmt(doc, P["exec_overview"], m, **_cfmt)))
    doc.add_paragraph(L["cover_date_start"].format(d=_m(m, "start_date")))
    doc.add_paragraph(L["cover_date_end"].format(d=_m(m, "end_date")))
    doc.add_paragraph(L["cover_total"].format(n=len(cases)))

    _add_heading(doc, f"4.2 {S['4.2']}", level=2)
    rows = layer_summary_rows(cases)
    table_rows = [
        [r["layer"], str(r["total"]), str(r["passed"]), str(r["failed"]), _pct(r["pass_rate"])]
        for r in rows
    ]
    _add_table(doc, L["headers"]["exec_summary"], table_rows, caption=L["caps"]["exec_by_type"])

    # Running subsection number after 4.2 (advances only for sections rendered).
    n = 3

    device_counts = report_model.device_summary_counts(cases)
    if device_counts:
        _add_heading(doc, f"4.{n} {S['4.device']}", level=2)
        doc.add_paragraph(P["device_note"])
        device_rows = []
        for dev, dc in device_counts.items():
            total = dc["total"]
            rate = (dc["passed"] / total) if total else 0.0
            device_rows.append(
                [dev, str(total), str(dc["passed"]), str(dc["failed"]), _pct(rate)]
            )
        _add_table(doc, L["headers"]["device_summary"], device_rows, caption=L["caps"]["exec_by_device"])
        n += 1

    _add_heading(doc, f"4.{n} {S['4.coverage']}", level=2)
    # Coverage: explicit report_meta.coverage, else a computed sentence from the
    # layers + devices actually covered (never a bare "—").
    _coverage = _m(m, "coverage")
    if _coverage == DASH:
        _cf = _scope_facts(doc, cases, m)
        _coverage = _pfmt(
            doc,
            ("การทดสอบครอบคลุม {layers}{devices_clause}" if getattr(doc, "_lang", "en") == "th"
             else "Testing covered {layers}{devices_clause}."),
            m, **_cf,
        )
    doc.add_paragraph(_coverage)
    doc.add_paragraph(P["rtm_intro"])
    rtm_rows = report_model.build_traceability(cases)
    if rtm_rows:
        _add_caption(doc, "table", L["caps"]["rtm"])
        _add_table(
            doc,
            L["headers"]["rtm"],
            [
                [r["user_story_id"], r["tc_ids"], str(r["tc_count"]),
                 str(r["passed"]), str(r["failed"]), r["coverage"]]
                for r in rtm_rows
            ],
        )
    else:
        doc.add_paragraph(P["rtm_none"])
    n += 1

    _add_heading(doc, f"4.{n} {S['4.dist']}", level=2)
    doc.add_paragraph(P["result_dist"])
    n += 1

    _add_heading(doc, f"4.{n} {S['4.findings']}", level=2)
    key_findings = _say_list(narr, "key_findings")
    if key_findings:
        _add_bullets(doc, key_findings)
    else:
        failed = sum(1 for c in cases if c.status == "Fail")
        doc.add_paragraph(
            P["findings_fail"].format(n=failed) if failed else P["findings_none"]
        )
    n += 1

    _add_heading(doc, f"4.{n} {S['4.verdict']}", level=2)
    # Verdict priority: authored narrative → explicit report_meta.verdict → a
    # computed verdict that MATCHES the results (pass / fail / not-run). Never "—".
    _meta_verdict = _m(m, "verdict")
    if _executed == 0:
        _verdict_default = _pfmt(doc, P["verdict_notrun"], m, **_cfmt)
    elif _failed:
        _verdict_default = _pfmt(doc, P["verdict_fail"], m, **_cfmt)
    else:
        _verdict_default = _pfmt(doc, P["verdict_pass"], m, **_cfmt)
    if _meta_verdict != DASH:
        _verdict_default = _meta_verdict
    doc.add_paragraph(_say(narr, "verdict", _verdict_default))


def _resolve_asset(assets_root: str | None, rel_path: str) -> str | None:
    """Resolve a skill-relative evidence path to an existing file, else None.

    Screenshot paths are stored rooted at ``qa-automation-testing/...``. This
    strips that leading segment and joins the remainder under ``assets_root``
    (the run's ``qa_dir``). Returns the absolute path only when the file exists
    on disk, so the writer embeds real images and never a broken reference.
    """
    if not assets_root or not rel_path:
        return None
    rel = rel_path.replace("\\", "/").strip()
    parts = rel.split("/", 1)
    tail = parts[1] if len(parts) == 2 and parts[0] == "qa-automation-testing" else rel
    candidate = os.path.join(assets_root, tail)
    return candidate if os.path.isfile(candidate) else None


# Embedded-screenshot display width per device (mobile is portrait, so narrower).
_EVIDENCE_WIDTH = {"desktop": Inches(5.6), "tablet": Inches(4.2), "mobile": Inches(2.6)}
_DEFAULT_EVIDENCE_WIDTH = Inches(5.2)


def _add_figure(doc, image_path: str, caption: str, width) -> None:
    """Embed a centered screenshot followed by a numbered figure caption below it.

    Mirrors the organization template: the image sits centered on the page and a
    ``รูปที่ N : <caption>`` line appears directly beneath it (feeding the List of
    Figures). The caption records the figure so the pre-built List of Figures
    field lists it.
    """
    para = doc.add_paragraph()
    para.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = para.add_run()
    run.add_picture(image_path, width=width)
    _add_caption(doc, "figure", caption, center=True)


def _case_evidence_shots(case: TestCase) -> list[tuple[str, str, str]]:
    """Collect a case's screenshots as ``(rel_path, device_label, status)`` tuples.

    Prefers the per-device results (one screenshot per responsive viewport) so
    the evidence shows every device that was executed; falls back to the rolled-
    up ``screenshot_path`` when a case carries no per-device screenshots.
    """
    shots: list[tuple[str, str, str]] = []
    results = case.device_results if isinstance(case.device_results, dict) else {}
    for device in report_model.DEVICES:
        result = results.get(device)
        if isinstance(result, dict) and result.get("screenshot_path"):
            status = str(result.get("status") or case.status or "").strip() or DASH
            shots.append((result["screenshot_path"], device, status))
    # Any extra (non-standard) devices, in a stable order.
    for device in sorted(d for d in results if d not in report_model.DEVICES):
        result = results.get(device)
        if isinstance(result, dict) and result.get("screenshot_path"):
            status = str(result.get("status") or case.status or "").strip() or DASH
            shots.append((result["screenshot_path"], device, status))
    if not shots and case.screenshot_path:
        shots.append((case.screenshot_path, "", (case.status or DASH)))
    return shots


def _write_evidence_reference(doc, group: str, group_cases: list[TestCase]) -> None:
    """Render the section-5 Evidence subsection as TEXT references only.

    The actual screenshots are embedded once, completely, in the Appendix
    ("หลักฐานการทดสอบทุก testcase"). Here we describe the evidence and reference
    the exact file path of each capture as text, so section 5 stays concise and
    the images are not duplicated.
    """
    P = doc._L["prose"]
    if group == "UI":
        doc.add_paragraph(P["evidence_ui"])
        for case in group_cases:
            shots = _case_evidence_shots(case)
            if not shots:
                doc.add_paragraph(
                    P["evidence_ui_none"].format(id=case.id, scenario=case.scenario),
                    style="List Bullet",
                )
                continue
            for rel_path, device, status in shots:
                device_label = device or P["device_default"]
                ref = doc.add_paragraph(
                    P["evidence_ref"].format(id=case.id, device=device_label,
                                             status=status, path=rel_path),
                    style="List Bullet",
                )
                for run in ref.runs:
                    run.font.color.rgb = _SUBTITLE_COLOR
    else:
        doc.add_paragraph(P["evidence_api"])


def _write_appendix_evidence(doc, cases: list[TestCase], assets_root: str | None) -> None:
    """Embed the complete screenshot evidence for EVERY test case (Appendix).

    Iterates all cases in order and, for each, embeds every per-device
    screenshot as a centered image with a ``รูปที่ N`` caption beneath. Cases
    with no screenshot (e.g. API) are still listed with a formal text note so
    the evidence set is complete and covers every test case without exception.
    """
    P = doc._L["prose"]
    doc.add_paragraph(P["appendix_evidence_intro"])
    for case in cases:
        _add_heading(doc, f"{case.id} — {case.scenario}", level=3)
        shots = _case_evidence_shots(case)
        if not shots:
            doc.add_paragraph(
                P["appendix_evidence_none"].format(status=case.status or DASH)
            )
            continue
        for rel_path, device, status in shots:
            resolved = _resolve_asset(assets_root, rel_path)
            device_label = device or P["device_default"]
            caption = f"{case.id} — {case.scenario} [{device_label} · {status}]"
            if resolved:
                width = _EVIDENCE_WIDTH.get(device, _DEFAULT_EVIDENCE_WIDTH)
                _add_figure(doc, resolved, caption, width)
            else:
                note = doc.add_paragraph(
                    P["appendix_evidence_missing"].format(
                        id=case.id, device=device_label, path=rel_path)
                )
                for run in note.runs:
                    run.font.color.rgb = _SUBTITLE_COLOR


def _write_detailed_results(doc, cases: list[TestCase], m: dict,
                            assets_root: str | None = None) -> None:
    L = doc._L
    S, P = L["subs"], L["prose"]
    _section_heading(doc, L["sections"][5])

    if not cases:
        doc.add_paragraph(P["detail_none"])
        return

    facts = _scope_facts(doc, cases, m)
    layer_title = {
        "UI": S["5.1.title"], "API": S["5.api.title"],
        "Unit-Integration": S["5.unit.title"],
    }
    layer_method = {"UI": P["method_ui"], "API": P["method_api"],
                    "Unit-Integration": P["method_unit"]}

    groups = _ordered_groups(_group_key(c) for c in cases)
    for index, group in enumerate(groups, start=1):
        group_cases = [c for c in cases if _group_key(c) == group]
        title = layer_title.get(group, S["5.generic.title"].format(group=group))
        _add_heading(doc, f"5.{index} {title}", level=2)

        _add_heading(doc, f"5.{index}.1 {S['5.methodology']}", level=3)
        method_tpl = layer_method.get(group, P["method_generic"])
        doc.add_paragraph(_pfmt(doc, method_tpl, m, group=group, **facts))

        # THREE-WAY split so a case is never mis-filed: Passed → Successful;
        # Fail/Blocked → Failed; anything else (Not Run / Skipped) → Not Executed.
        # A blank/not-run status is NEVER reported as a failure.
        successful = [c for c in group_cases if c.status == "Pass"]
        failed = [c for c in group_cases if c.status in ("Fail", "Blocked")]
        notrun = [c for c in group_cases if c not in successful and c not in failed]

        # Subsection numbers are dynamic so the "Not Executed" block only appears
        # (and only consumes a number) when there are such cases.
        sub = 2
        _add_heading(doc, f"5.{index}.{sub} {S['5.success']}", level=3); sub += 1
        if successful:
            _add_table(doc, L["headers"]["detail"], [_detail_row(c) for c in successful],
                       caption=L["caps"]["success"].format(group=group))
        else:
            doc.add_paragraph(P["success_none"])

        _add_heading(doc, f"5.{index}.{sub} {S['5.failed']}", level=3); sub += 1
        if failed:
            _add_table(doc, L["headers"]["detail"], [_detail_row(c) for c in failed],
                       caption=L["caps"]["failed"].format(group=group))
        else:
            doc.add_paragraph(P["failed_none"])

        if notrun:
            _add_heading(doc, f"5.{index}.{sub} {S['5.notrun']}", level=3); sub += 1
            _add_table(doc, L["headers"]["detail"], [_detail_row(c) for c in notrun],
                       caption=L["caps"]["notrun"].format(group=group))

        _add_heading(doc, f"5.{index}.{sub} {S['5.evidence']}", level=3)
        _write_evidence_reference(doc, group, group_cases)


def _detail_row(case: TestCase) -> list[str]:
    return [
        case.id,
        case.user_story_id,
        case.module,
        case.scenario,
        report_model.derive_severity(case),
        report_model.derive_risk_level(case),
        case.status,
        case.defect_id,
        case.notes,
    ]


def _write_issues(doc, cases: list[TestCase]) -> None:
    L = doc._L
    S, P = L["subs"], L["prose"]
    _section_heading(doc, L["sections"][6])

    counts = _severity_counts(cases)
    _add_heading(doc, S["6.1"], level=2)
    _add_caption(doc, "table", L["caps"]["severity"])
    _add_table(
        doc,
        L["headers"]["severity_count"],
        [
            ["Critical", str(counts["Critical"])],
            ["High", str(counts["High"])],
            ["Medium", str(counts["Medium"])],
            ["Low", str(counts["Low"])],
        ],
    )

    _add_heading(doc, S["6.2"], level=2)
    doc.add_paragraph(P["severity_class"])
    # Detailed classification RESULT (mirrors the XLSX note, but fuller): the
    # actual counts, how many are release-blocking, and a written verdict.
    total_def = counts["Critical"] + counts["High"] + counts["Medium"] + counts["Low"]
    executed = sum(1 for c in cases if (c.status or "").strip() not in ("", "Not Run", "Skipped"))
    if total_def == 0:
        doc.add_paragraph(P["severity_summary_none"])
    else:
        blocking = counts["Critical"] + counts["High"]
        nonblocking = counts["Medium"] + counts["Low"]
        verdict = P["severity_verdict_block"] if blocking else P["severity_verdict_ok"]
        doc.add_paragraph(P["severity_summary"].format(
            total=total_def, executed=executed, crit=counts["Critical"],
            high=counts["High"], med=counts["Medium"], low=counts["Low"],
            blocking=blocking, nonblocking=nonblocking, verdict=verdict))

    _add_heading(doc, S["6.3"], level=2)
    defect_rows = report_model.build_defect_log(cases)
    if defect_rows:
        _add_caption(doc, "table", L["caps"]["defect_log"])
        _add_table(
            doc,
            L["headers"]["defect"],
            [
                [
                    r["defect_id"],
                    r["linked_tc_id"],
                    r["severity"],
                    r["status"],
                    r["assigned_to"],
                    r["root_cause"],
                    r["resolution"],
                ]
                for r in defect_rows
            ],
        )
    else:
        doc.add_paragraph(P["defect_none"])

    _add_heading(doc, S["6.4"], level=2)
    failures = report_model.defect_cases(cases)
    if failures:
        for case in failures:
            doc.add_paragraph(
                f"{case.id} — {case.scenario}: {case.notes or DASH}",
                style="List Bullet",
            )
    else:
        doc.add_paragraph(P["defect_none"])


def _write_conclusion(doc, m: dict, cases: list[TestCase], narr: dict | None = None) -> None:
    narr = narr or {}
    L = doc._L
    S, P, QL = L["subs"], L["prose"], L["quality_levels"]
    _section_heading(doc, L["sections"][7])

    total = len(cases)
    passed = sum(1 for c in cases if c.status == "Pass")
    failed = sum(1 for c in cases if c.status == "Fail")
    executed = passed + failed
    overall_rate = (passed / total) if total else 0.0
    exec_rate = (passed / executed) if executed else 0.0

    # Derive a quality level from the executed pass rate (Pass+Fail).
    if executed == 0:
        quality_level = QL["not_assessed"]
    elif exec_rate >= 0.95:
        quality_level = QL["excellent"]
    elif exec_rate >= 0.85:
        quality_level = QL["good"]
    elif exec_rate >= 0.70:
        quality_level = QL["acceptable"]
    else:
        quality_level = QL["needs"]

    _add_heading(doc, S["7.1"], level=2)
    _cov = _m(m, "coverage")
    if _cov == DASH:
        _cf = _scope_facts(doc, cases, m)
        _cov = _pfmt(
            doc,
            ("การทดสอบครอบคลุม {layers}{devices_clause}" if getattr(doc, "_lang", "en") == "th"
             else "Testing covered {layers}{devices_clause}."),
            m, **_cf,
        )
    doc.add_paragraph(_say(narr, "coverage", _cov))

    _add_heading(doc, S["7.2"], level=2)
    counts = _severity_counts(cases)
    fmt = dict(overall=_pct(overall_rate), passed=passed, total=total, executed=executed,
               exec_rate=_pct(exec_rate), level=quality_level,
               crit=counts["Critical"], high=counts["High"],
               med=counts["Medium"], low=counts["Low"])
    assessment = _pfmt(doc, P["quality_exec"] if executed else P["quality_noexec"], m, **fmt)
    doc.add_paragraph(assessment)
    authored_quality = _say(narr, "quality_assessment", "")
    if authored_quality:
        doc.add_paragraph(authored_quality)

    _add_heading(doc, S["7.3"], level=2)
    recommendations = _say_list(narr, "recommendations")
    if recommendations:
        _add_bullets(doc, recommendations)
    else:
        # The default recommendation MUST match the result: recommending "fix the
        # failed cases" when nothing failed is wrong. Pick the pass-variant when
        # there are no failures (and none were executed uses the pass wording too).
        rec_key = "recommend_default" if failed else "recommend_pass"
        doc.add_paragraph(_pfmt(doc, P[rec_key], m))

    _add_heading(doc, S["7.4"], level=2)
    next_steps = _say_list(narr, "next_steps")
    if next_steps:
        _add_bullets(doc, next_steps)
    else:
        # "After defects are fixed…" only makes sense when there WERE failures.
        next_key = "next_default" if failed else "next_pass"
        doc.add_paragraph(_pfmt(doc, P[next_key], m))

    conclusion = _say(narr, "conclusion", "")
    if conclusion:
        doc.add_paragraph(conclusion)


def _write_appendix(doc, m: dict, cases: list[TestCase],
                    assets_root: str | None = None) -> None:
    L = doc._L
    S, P, R = L["subs"], L["prose"], L["rows"]
    _section_heading(doc, L["sections"][8])

    _add_heading(doc, S["8.1"], level=2)
    if cases:
        _add_caption(doc, "table", L["caps"]["tc_index"])
        _add_table(doc, L["headers"]["tc_index"], [[c.id, c.scenario] for c in cases])
    else:
        doc.add_paragraph(P["no_test_cases"])

    _add_heading(doc, S["8.2"], level=2)
    doc.add_paragraph(P["appendix_glossary"])

    _add_heading(doc, S["8.3"], level=2)
    _add_caption(doc, "table", L["caps"]["tools_config"])
    _add_table(
        doc,
        L["headers"]["tool_version"],
        [
            ["Playwright", _m(m, "playwright_version")],
            ["Node.js", _m(m, "node_version")],
        ],
    )

    _add_heading(doc, S["8.4"], level=2)
    doc.add_paragraph(f"{R['app_url']}: {_m(m, 'app_url')}")
    doc.add_paragraph(f"{R['api_base']}: {_m(m, 'api_base')}")

    _add_heading(doc, S["8.5"], level=2)
    doc.add_paragraph(P["appendix_workbook"])

    _add_heading(doc, S["8.6"], level=2)
    if cases:
        _write_appendix_evidence(doc, cases, assets_root)
    else:
        doc.add_paragraph(P["no_test_cases"])


def main(argv: list[str]) -> int:
    if len(argv) not in (3, 4):
        print(
            "usage: report_docx.py <test_cases.json> <out.docx> [report_meta.json]",
            file=sys.stderr,
        )
        return 2

    cases = report_model.load(argv[1])
    meta = load_meta(argv[3]) if len(argv) == 4 else {}
    write_docx(cases, argv[2], meta)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
