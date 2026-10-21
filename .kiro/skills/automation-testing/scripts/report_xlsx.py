"""XLSX writer for the Reporting_Engine.

Renders the single-source-of-truth :class:`report_model.TestCase` model into a
standard, industry-style QA workbook (per ISTQB / IEEE-829 + common templates).
The workbook carries, in tab order: an ``Info`` sheet, a ``Summary`` sheet, the
detailed ``TestCases`` table, a ``Traceability`` (RTM) sheet, a ``DefectLog``
sheet, and any ``History_*`` backup sheets created on a re-run.

Columns are grouped professionally (Identification, Test Definition, Execution
Result, Evidence). The status column letter is **computed** from the column
order (never hardcoded) and every ``Summary`` formula is built from it.

CLI:
    python report_xlsx.py <test_cases.json> <out.xlsx> [report_meta.json]
"""

from __future__ import annotations

import os
import re
from datetime import datetime

from openpyxl import Workbook, load_workbook
from openpyxl.drawing.image import Image as XLImage
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.hyperlink import Hyperlink
from openpyxl.worksheet.worksheet import Worksheet

import labels
import report_model
from report_docx import DASH
from report_docx import load_meta as report_docx_load_meta
from report_docx import normalize_meta
from report_model import TestCase

# Canonical column order for the standard QA workbook, grouped professionally:
#   Identification, Test Definition, Execution Result, Evidence.
# `user_story_id` sits right after `id` (column B). Both writers reference this
# order by field NAME, so the column letters adapt automatically.
COLUMNS: tuple[str, ...] = (
    # --- Identification ---
    "id",               # A
    "user_story_id",    # B
    "module",           # C
    "qa_layer",         # D
    "scenario",         # E
    "category",         # F
    "priority",         # G
    "severity",         # H
    "risk_level",       # I  <-- risk-based ordering level
    # --- Test Definition ---
    "preconditions",    # J
    "steps",            # K
    "test_data",        # L
    "test_data_source",  # M  <-- origin of test data (inline/file/fixture/api)
    "expected",         # N
    "rule_id",          # O
    "source_reference",  # P
    "source_origin",    # Q
    # --- Execution Result ---
    "status",           # R  <-- status lives here (computed, not hardcoded)
    "actual",           # S
    "executed_by",      # T
    "executed_at",      # U
    "environment",      # V
    "defect_id",        # W
    "notes",            # X
    # --- Evidence ---
    "screenshot",       # Y  <-- cell holds the ACTUAL embedded image
    "screenshot_path",  # Z  <-- text path note to the image file
    "video_path",       # AA
)

# Header labels mirror the column order above.
HEADERS: tuple[str, ...] = COLUMNS

# Run-stage ("result") columns cleared/backed up on a re-run.
RESULT_FIELDS: tuple[str, ...] = (
    "status",
    "actual",
    "executed_by",
    "executed_at",
    "environment",
    "defect_id",
    "notes",
    "screenshot_path",
    "video_path",
)

# Excel column letter for status — COMPUTED from the column order so the
# workbook never breaks if the grouping changes. Every Summary formula uses it.
STATUS_COL_LETTER = get_column_letter(COLUMNS.index("status") + 1)

# The concise INDEX sheet ("TestCases") — important columns only. The TC id is a
# HYPERLINK to that case's full detail row in its per-type sheet. `id` and
# `status` here keep the master status source for the Summary formulas.
INDEX_HEADERS: tuple[str, ...] = (
    "TC ID",
    "Test Type",
    "Module",
    "Scenario",
    "Priority",
    "Device Type",
    "Status",
)
# Column letters in the INDEX sheet (Summary formulas reference these).
INDEX_ID_COL_LETTER = get_column_letter(INDEX_HEADERS.index("TC ID") + 1)      # A
INDEX_STATUS_COL_LETTER = get_column_letter(INDEX_HEADERS.index("Status") + 1)  # G

# Font for the clickable TC-id hyperlinks in the INDEX sheet (blue + underline).
LINK_FONT = Font(color="0563C1", underline="single")

# Field labels for the Info sheet -> canonical normalize_meta() key.
INFO_FIELDS: tuple[tuple[str, str], ...] = (
    ("System", "system_name"),
    ("Company", "company"),
    ("Version", "version"),
    ("Author (Prepared By)", "author"),
    ("Created Date", "created_date"),
    ("Updated Date", "updated_date"),
    ("Executed Date", "executed_date"),
    ("Environment", "environment"),
    ("OS", "os"),
    ("Node", "node_version"),
    ("Browser", "browser_version"),
    ("Tool", "tool"),
    ("App URL", "app_url"),
    ("API Base", "api_base"),
    ("Scope", "scope"),
)

# Header rows for the standard QA sheets.
TRACEABILITY_HEADERS: tuple[str, ...] = (
    "User Story ID",
    "Covered TC IDs",
    "#TC",
    "#Pass",
    "#Fail",
    "Coverage",
)

DEFECTLOG_HEADERS: tuple[str, ...] = (
    "Defect ID",
    "Linked TC ID",
    "Title",
    "Steps to Reproduce",
    "Severity",
    "Priority",
    "Status",
    "Assigned To",
    "Suggested Root Cause",
    "Resolution",
    "Found Date",
    "Fixed Date",
)

# Bounded reference window used by the Summary formulas. 1000 data rows is
# comfortably above any realistic suite size while keeping formulas simple.
_MAX_ROW = 1000


# ---------------------------------------------------------------------------
# Professional styling palette (Excel-safe: only standard fills/fonts/borders).
# ---------------------------------------------------------------------------

# Dark navy header band with white bold text, used on every table header row.
HEADER_FILL = PatternFill(fill_type="solid", fgColor="1F4E78")
HEADER_FONT = Font(bold=True, color="FFFFFF", size=11)
HEADER_ALIGN = Alignment(horizontal="center", vertical="center", wrap_text=True)

# Larger, bold sheet title (merged across the table width).
TITLE_FONT = Font(bold=True, color="1F4E78", size=14)
TITLE_ALIGN = Alignment(horizontal="left", vertical="center")

# Big company-name title band (the formal top title on Info & Summary).
COMPANY_TITLE_FONT = Font(bold=True, color="2C3E50", size=18)
COMPANY_TITLE_ALIGN = Alignment(horizontal="left", vertical="center")
# Subtitle line beneath the company title.
SUBTITLE_FONT = Font(bold=False, italic=True, color="566573", size=11)

# KPI block: a label/value pair per headline metric with a subtle accent fill.
KPI_LABEL_FONT = Font(bold=True, color="2C3E50", size=11)
KPI_VALUE_FONT = Font(bold=True, color="1A1A1A", size=12)
KPI_VALUE_ALIGN = Alignment(horizontal="center", vertical="center")
KPI_FILLS: dict[str, PatternFill] = {
    "Passed": PatternFill(fill_type="solid", fgColor="C6EFCE"),   # green
    "Failed": PatternFill(fill_type="solid", fgColor="FFC7CE"),   # red
    "Blocked": PatternFill(fill_type="solid", fgColor="FFEB9C"),  # amber
    "_other": PatternFill(fill_type="solid", fgColor="EAECEE"),   # grey
}

# Bold label font for section headers / row labels on Info & Summary.
LABEL_FONT = Font(bold=True)

# Thin light-grey border applied around all data tables.
_THIN_SIDE = Side(style="thin", color="BFBFBF")
THIN_BORDER = Border(left=_THIN_SIDE, right=_THIN_SIDE, top=_THIN_SIDE, bottom=_THIN_SIDE)

# Wrap + top/left alignment for long free-text columns.
WRAP_TOPLEFT = Alignment(horizontal="left", vertical="top", wrap_text=True)
TOPLEFT = Alignment(horizontal="left", vertical="top")

# Header row height (a touch taller so wrapped labels stay readable).
HEADER_ROW_HEIGHT = 24

# Status -> solid fill on TestCases data rows (and detailed views).
STATUS_FILLS: dict[str, PatternFill] = {
    "Pass": PatternFill(fill_type="solid", fgColor="C6EFCE"),     # green
    "Fail": PatternFill(fill_type="solid", fgColor="FFC7CE"),     # red
    "Blocked": PatternFill(fill_type="solid", fgColor="FFEB9C"),  # amber
    "Skipped": PatternFill(fill_type="solid", fgColor="D9D9D9"),  # grey
    "Not Run": PatternFill(fill_type="solid", fgColor="F2F2F2"),  # light grey
}

# Severity -> solid fill on the DefectLog severity column.
SEVERITY_FILLS: dict[str, PatternFill] = {
    "Critical": PatternFill(fill_type="solid", fgColor="FFC7CE"),  # red-ish
    "High": PatternFill(fill_type="solid", fgColor="FFC7CE"),      # red-ish
    "Medium": PatternFill(fill_type="solid", fgColor="FFEB9C"),    # amber
    "Low": PatternFill(fill_type="solid", fgColor="D9D9D9"),       # grey
}

# Defect lifecycle status -> solid fill on the DefectLog status column.
DEFECT_STATUS_FILLS: dict[str, PatternFill] = {
    "New": PatternFill(fill_type="solid", fgColor="FFEB9C"),       # amber (open)
    "Open": PatternFill(fill_type="solid", fgColor="FFC7CE"),      # red-ish
    "In Progress": PatternFill(fill_type="solid", fgColor="FFEB9C"),
    "Fixed": PatternFill(fill_type="solid", fgColor="C6EFCE"),     # green
    "Resolved": PatternFill(fill_type="solid", fgColor="C6EFCE"),
    "Closed": PatternFill(fill_type="solid", fgColor="C6EFCE"),
}

# Per-field column widths for the TestCases sheet (by field name).
_TC_COLUMN_WIDTHS: dict[str, int] = {
    "id": 12,
    "user_story_id": 14,
    "module": 16,
    "qa_layer": 14,
    "scenario": 38,
    "category": 12,
    "priority": 10,
    "severity": 10,
    "risk_level": 12,
    "preconditions": 28,
    "steps": 40,
    "test_data": 24,
    "test_data_source": 14,
    "expected": 34,
    "rule_id": 10,
    "source_reference": 26,
    "source_origin": 12,
    "status": 11,
    "actual": 30,
    "executed_by": 14,
    "executed_at": 22,
    "environment": 14,
    "defect_id": 12,
    "notes": 30,
    "screenshot_path": 26,
    "screenshot": 26,
    "video_path": 24,
}

# Cap on any single column width so one huge cell never blows out the layout.
_MAX_COL_WIDTH = 60

# Distinct, modern flat tab colors per sheet so the workbook reads at a glance.
# History_* sheets all share a cool grey.
TAB_COLORS: dict[str, str] = {
    "Info": "2C3E50",          # slate
    "Summary": "16A085",       # teal
    "UI-E2E": "3498DB",        # per-type sheets share one blue
    "UI-Functional": "3498DB",
    "API": "3498DB",
    "Unit-Integration": "3498DB",
    "TestCases": "2980B9",     # index (distinct darker blue)
    "Traceability": "8E44AD",  # purple
    "DefectLog": "E74C3C",     # rose-red
    "Desktop": "E67E22",       # per-device sheets share one orange
    "Tablet": "E67E22",
    "Mobile": "E67E22",
}
_HISTORY_TAB_COLOR = "95A5A6"  # cool grey for every History_* backup sheet

# Row-height tuning for wrapped text (points). One wrapped line is ~15pt; we add
# a little padding and cap the height so a very long cell never dominates.
_LINE_HEIGHT_PT = 15.0
_ROW_BASE_PAD_PT = 4.0
_MAX_ROW_HEIGHT_PT = 240.0


def _set_tab_color(ws: Worksheet, name: str) -> None:
    """Color a sheet's tab from :data:`TAB_COLORS` (History_* -> grey)."""
    if name.startswith("History_"):
        ws.sheet_properties.tabColor = _HISTORY_TAB_COLOR
    elif name in TAB_COLORS:
        ws.sheet_properties.tabColor = TAB_COLORS[name]


def _estimate_lines(text: object, col_width: float) -> int:
    """Estimate how many wrapped lines ``text`` needs at ``col_width`` chars.

    Counts explicit newlines and the soft-wrap of each physical line against the
    column width (one Excel width unit ≈ one character). Always at least 1.
    """
    if text in (None, ""):
        return 1
    width = max(int(col_width), 1)
    lines = 0
    for physical in str(text).split("\n"):
        length = len(physical)
        lines += max(1, -(-length // width))  # ceil division
    return max(1, lines)


def _autosize_row_heights(
    ws: Worksheet,
    first_row: int,
    last_row: int,
    widths: dict[int, float],
) -> None:
    """Set each row's height so wrapped text in every column stays visible.

    For each data row the needed line count is the max across columns (text
    length vs that column's width); the row height is that many line-heights
    (plus padding), capped. Any height already set larger (e.g. by an embedded
    image) is preserved.
    """
    for r in range(first_row, last_row + 1):
        needed = 1
        for col, width in widths.items():
            value = ws.cell(row=r, column=col).value
            needed = max(needed, _estimate_lines(value, width))
        height = min(_ROW_BASE_PAD_PT + needed * _LINE_HEIGHT_PT, _MAX_ROW_HEIGHT_PT)
        existing = ws.row_dimensions[r].height
        if existing is None or height > existing:
            ws.row_dimensions[r].height = height


def _style_header_row(ws: Worksheet, row: int, ncols: int) -> None:
    """Apply the dark header band (bold white text + borders) to one row."""
    for col in range(1, ncols + 1):
        cell = ws.cell(row=row, column=col)
        cell.font = HEADER_FONT
        cell.fill = HEADER_FILL
        cell.border = THIN_BORDER
        cell.alignment = HEADER_ALIGN
    ws.row_dimensions[row].height = HEADER_ROW_HEIGHT


def _border_block(ws: Worksheet, first_row: int, last_row: int, ncols: int) -> None:
    """Draw thin borders around every cell in a rectangular block."""
    for r in range(first_row, max(first_row, last_row) + 1):
        for c in range(1, ncols + 1):
            ws.cell(row=r, column=c).border = THIN_BORDER


def _merged_title(ws: Worksheet, text: str, ncols: int) -> None:
    """Write a merged, bold, larger title across row 1 spanning ``ncols``."""
    ws.cell(row=1, column=1, value=text)
    ws.merge_cells(start_row=1, start_column=1, end_row=1, end_column=ncols)
    title = ws.cell(row=1, column=1)
    title.font = TITLE_FONT
    title.alignment = TITLE_ALIGN
    ws.row_dimensions[1].height = 26


def _verify_readable(path: str) -> None:
    """Excel-safety guard: reload the saved workbook and touch every sheet.

    Re-opening with openpyxl exercises the same XML parse Excel performs; if any
    sheet were malformed this would raise. Touching ``max_row``/``max_column``
    forces the dimension to be read so a silently truncated sheet is caught.
    """
    check = load_workbook(path)
    for name in check.sheetnames:
        sheet = check[name]
        # Access dimensions to force the sheet body to be read.
        _ = (sheet.max_row, sheet.max_column)
    check.close()


# Known viewport pixel sizes per device (mirrors the bundled Playwright config)
# so the Info sheet can show the responsive matrix that was tested.
DEVICE_VIEWPORTS: dict[str, str] = {
    "desktop": "1440×900",
    "tablet": "768×1024",
    "mobile": "390×844",
}


def _tested_matrix(cases: list[TestCase] | None, norm: dict | None = None,
                   lang: str = "en") -> str:
    """Describe the device/viewport matrix (actual, else planned), from the model.

    Reads the per-device result keys present across the cases (e.g. desktop /
    tablet / mobile, or browser-prefixed like ``chrome-desktop``) and renders
    them with their known viewport size. Before a run there are no per-device
    results, so it falls back to the PLANNED devices recorded in report_meta
    (``norm['planned_devices']``) and tags them as planned — so the Info sheet
    shows the intended responsive matrix in the Generate PREVIEW instead of
    inventing "—". Returns "—" only when neither actual nor planned devices
    exist (e.g. a pure API/Unit run with no responsive plan).
    """
    devices = report_model.devices_tested(cases or [])
    planned = False
    if not devices:
        devices = list((norm or {}).get("planned_devices") or [])
        planned = bool(devices)
    if not devices:
        return "—"
    parts = []
    for d in devices:
        # d may be a bare device ("desktop") or "browser-device" ("chrome-desktop").
        base = d.split("-")[-1]
        vp = DEVICE_VIEWPORTS.get(base)
        parts.append(f"{d} ({vp})" if vp else d)
    matrix = ", ".join(parts)
    if planned:
        matrix += " (วางแผนทดสอบ)" if lang == "th" else " (planned)"
    return matrix


def _fill_info(ws: Worksheet, meta: dict | None = None,
               cases: list[TestCase] | None = None) -> Worksheet:
    """Fill ``ws`` with the Info sheet rows (Field | Value) from run metadata.

    Row 1 is a merged, bold title; row 2 is the ``Field | Value`` header band;
    data rows follow. The value column wraps long text and every cell carries a
    light border so the sheet reads as a clean specification table. A derived
    "Devices / Viewports Tested" row records the actual responsive/cross-browser
    matrix that was exercised (from the model), so Info shows real coverage.
    """
    norm = normalize_meta(meta or {})
    # Cases-aware planned devices: default to the standard responsive set when the
    # run recorded none but there ARE UI cases, so the Info sheet states the device
    # matrix even in the Generate preview (mirrors the DOCX writer).
    norm["planned_devices"] = report_model.effective_planned_devices(meta, cases)
    company = norm.get("company") or "—"
    company = str(company).strip() or "—"
    # Formal company title band on row 1, then the Field | Value header on row 2.
    _merged_title(ws, company, 2)
    ws.cell(row=1, column=1).font = COMPANY_TITLE_FONT
    ws.row_dimensions[1].height = 30
    ws.append(["Field", "Value"])  # row 2 header band
    # The Info sheet is *information*: it must read as real data, never a bare
    # dash. Values come from run metadata (auto-detected env + Intake-collected
    # fields); anything genuinely absent renders as a clear "N/A" rather than "—".
    for label, key in INFO_FIELDS:
        value = norm.get(key, DASH)
        text = str(value).strip() if value not in (None, "") else ""
        if not text or text == DASH:
            text = "N/A"
        ws.append([label, text])
    # Derived coverage row: the device/viewport matrix — actual after a run, or
    # the PLANNED responsive matrix (from report_meta) in the pre-run preview.
    _info_lang = labels.resolve_language(meta, *(getattr(c, "scenario", "") for c in (cases or [])[:12]))
    matrix = _tested_matrix(cases, norm, _info_lang)
    ws.append(["Devices / Viewports Tested", matrix if matrix != DASH else "N/A"])

    _style_header_row(ws, 2, 2)
    last_row = ws.max_row
    _border_block(ws, 2, last_row, 2)
    for r in range(3, last_row + 1):
        ws.cell(row=r, column=1).font = LABEL_FONT
        ws.cell(row=r, column=1).alignment = TOPLEFT
        ws.cell(row=r, column=2).alignment = WRAP_TOPLEFT
    ws.column_dimensions["A"].width = 22
    ws.column_dimensions["B"].width = 50
    _autosize_row_heights(ws, 3, last_row, {1: 22, 2: 50})
    _set_tab_color(ws, ws.title)
    return ws


def add_info_sheet(wb: Workbook, meta: dict | None = None) -> Worksheet:
    """Append an ``Info`` sheet (Field | Value) from normalized run metadata.

    Reuses ``report_docx.normalize_meta`` so both reports share one metadata
    contract; any field the metadata does not supply renders as "—".
    """
    return _fill_info(wb.create_sheet(title="Info"), meta)


def _ordered_unique(values) -> list[str]:
    """First-appearance-ordered unique non-empty strings (blank -> '—')."""
    seen: list[str] = []
    for v in values:
        key = (v or "").strip() or "—"
        if key not in seen:
            seen.append(key)
    return seen


def add_summary_sheet(
    wb: Workbook,
    cases: list[TestCase] | None = None,
    meta: dict | None = None,
    device_counts: dict[str, dict[str, int]] | None = None,
) -> Worksheet:
    """Append a redesigned, modern ``Summary`` sheet.

    Row 1 is the COMPANY NAME (formal top title, merged + large bold); row 2 is
    the subtitle line. A KPI block of headline metrics (Total / Passed / Failed
    / Blocked / Skipped / Not Run / Pass Rate) follows as labeled rows with
    subtle accent fills (green Passed, red Failed, amber Blocked, grey others),
    each value a COUNTIF/COUNTA formula built from the COMPUTED status column.
    Breakdowns by QA layer, by test type, by priority, and defect counts by
    severity follow as bordered sub-tables with clean header bands. The defect
    counts table always renders all four severities (Critical/High/Medium/Low,
    0 when none).
    """
    cases = cases or []
    ws = wb.create_sheet(title="Summary")

    norm = normalize_meta(meta or {})
    company = str(norm.get("company") or "—").strip() or "—"

    # Row 1: company name (formal title). Row 2: subtitle.
    _merged_title(ws, company, 5)
    ws.cell(row=1, column=1).font = COMPANY_TITLE_FONT
    ws.row_dimensions[1].height = 30
    # Subtitle follows the content language so an English workbook stays English
    # (no Thai leakage) and a Thai workbook reads Thai.
    _lang = labels.resolve_language(meta, *(getattr(c, "scenario", "") for c in cases[:12]))
    _subtitle = "สรุปผลการทดสอบ" if _lang == "th" else "Test Execution Summary"
    ws.cell(row=2, column=1, value=_subtitle)
    ws.merge_cells(start_row=2, start_column=1, end_row=2, end_column=5)
    ws.cell(row=2, column=1).font = SUBTITLE_FONT
    ws.cell(row=2, column=1).alignment = TITLE_ALIGN

    k = INDEX_STATUS_COL_LETTER
    a_range = f"TestCases!{INDEX_ID_COL_LETTER}2:{INDEX_ID_COL_LETTER}{_MAX_ROW}"
    k_range = f"TestCases!{k}2:{k}{_MAX_ROW}"

    # KPI block. Status counts first, then Total, then Pass Rate (a headline %).
    # Total = COUNT of non-empty Test_Case ids (COUNTA); Pass Rate = Passed/Total.
    kpi_first = 4
    kpis = [
        ("Passed", f'=COUNTIF({k_range},"Pass")', "Passed"),
        ("Failed", f'=COUNTIF({k_range},"Fail")', "Failed"),
        ("Blocked", f'=COUNTIF({k_range},"Blocked")', "Blocked"),
        ("Skipped", f'=COUNTIF({k_range},"Skipped")', "_other"),
        ("Not Run", f'=COUNTIF({k_range},"Not Run")', "_other"),
        ("Total", f"=COUNTA({a_range})", "_other"),
    ]
    # Rows that Pass Rate needs (computed, never hardcoded so reordering is safe).
    passed_row = kpi_first + 0
    total_row = kpi_first + len(kpis) - 1  # Total is the last count row
    kpis.append(
        ("Pass Rate", f"=IF(B{total_row}=0,0,B{passed_row}/B{total_row})", "_other")
    )
    for offset, (label, formula, fill_key) in enumerate(kpis):
        r = kpi_first + offset
        label_cell = ws.cell(row=r, column=1, value=label)
        value_cell = ws.cell(row=r, column=2, value=formula)
        label_cell.font = KPI_LABEL_FONT
        value_cell.font = KPI_VALUE_FONT
        value_cell.alignment = KPI_VALUE_ALIGN
        fill = KPI_FILLS[fill_key]
        label_cell.fill = fill
        value_cell.fill = fill
    kpi_last = kpi_first + len(kpis) - 1
    ws.cell(row=kpi_last, column=2).number_format = "0.0%"  # Pass Rate
    _border_block(ws, kpi_first, kpi_last, 2)

    row = kpi_last + 2
    # --- Pass/Fail breakdown BY qa_layer (with Pass Rate %) ---
    _section_band(ws, row, "By QA Layer", 5)
    row += 1
    header_row = row
    ws.cell(row=row, column=1, value="Layer")
    ws.cell(row=row, column=2, value="Total")
    ws.cell(row=row, column=3, value="Pass")
    ws.cell(row=row, column=4, value="Fail")
    ws.cell(row=row, column=5, value="Pass Rate")
    row += 1
    for layer in _ordered_unique(c.qa_layer for c in cases):
        group = [c for c in cases if ((c.qa_layer or "").strip() or "—") == layer]
        total = len(group)
        passed = sum(1 for c in group if c.status == "Pass")
        ws.cell(row=row, column=1, value=layer)
        ws.cell(row=row, column=2, value=total)
        ws.cell(row=row, column=3, value=passed)
        ws.cell(row=row, column=4, value=sum(1 for c in group if c.status == "Fail"))
        rate_cell = ws.cell(row=row, column=5, value=(passed / total) if total else 0.0)
        rate_cell.number_format = "0.0%"
        row += 1
    _style_subtable(ws, header_row, row - 1, 5)

    row += 1
    # --- Pass/Fail breakdown BY device (with Pass Rate %) ---
    if device_counts:
        _section_band(ws, row, "By Device", 5)
        row += 1
        header_row = row
        ws.cell(row=row, column=1, value="Device")
        ws.cell(row=row, column=2, value="Total")
        ws.cell(row=row, column=3, value="Pass")
        ws.cell(row=row, column=4, value="Fail")
        ws.cell(row=row, column=5, value="Pass Rate")
        row += 1
        for dev, dc in device_counts.items():
            total = dc["total"]
            passed = dc["passed"]
            ws.cell(row=row, column=1, value=dev)
            ws.cell(row=row, column=2, value=total)
            ws.cell(row=row, column=3, value=passed)
            ws.cell(row=row, column=4, value=dc["failed"])
            rate_cell = ws.cell(row=row, column=5, value=(passed / total) if total else 0.0)
            rate_cell.number_format = "0.0%"
            row += 1
        _style_subtable(ws, header_row, row - 1, 5)
        row += 1

    # --- Pass/Fail breakdown BY test type (with Pass Rate %) ---
    _section_band(ws, row, "By Test Type", 5)
    row += 1
    header_row = row
    ws.cell(row=row, column=1, value="Test Type")
    ws.cell(row=row, column=2, value="Total")
    ws.cell(row=row, column=3, value="Pass")
    ws.cell(row=row, column=4, value="Fail")
    ws.cell(row=row, column=5, value="Pass Rate")
    row += 1
    for type_name, group in report_model.group_by_test_type(cases):
        total = len(group)
        passed = sum(1 for c in group if c.status == "Pass")
        ws.cell(row=row, column=1, value=type_name)
        ws.cell(row=row, column=2, value=total)
        ws.cell(row=row, column=3, value=passed)
        ws.cell(row=row, column=4, value=sum(1 for c in group if c.status == "Fail"))
        rate_cell = ws.cell(row=row, column=5, value=(passed / total) if total else 0.0)
        rate_cell.number_format = "0.0%"
        row += 1
    _style_subtable(ws, header_row, row - 1, 5)

    row += 1
    # --- Pass/Fail breakdown BY priority (with Pass Rate %) ---
    _section_band(ws, row, "By Priority", 5)
    row += 1
    header_row = row
    ws.cell(row=row, column=1, value="Priority")
    ws.cell(row=row, column=2, value="Total")
    ws.cell(row=row, column=3, value="Pass")
    ws.cell(row=row, column=4, value="Fail")
    ws.cell(row=row, column=5, value="Pass Rate")
    row += 1
    for priority in _ordered_unique(c.priority for c in cases):
        group = [c for c in cases if ((c.priority or "").strip() or "—") == priority]
        total = len(group)
        passed = sum(1 for c in group if c.status == "Pass")
        ws.cell(row=row, column=1, value=priority)
        ws.cell(row=row, column=2, value=total)
        ws.cell(row=row, column=3, value=passed)
        ws.cell(row=row, column=4, value=sum(1 for c in group if c.status == "Fail"))
        rate_cell = ws.cell(row=row, column=5, value=(passed / total) if total else 0.0)
        rate_cell.number_format = "0.0%"
        row += 1
    _style_subtable(ws, header_row, row - 1, 5)

    row += 1
    # --- Defect counts BY severity ---
    _section_band(ws, row, "Defects By Severity", 2)
    row += 1
    header_row = row
    ws.cell(row=row, column=1, value="Severity")
    ws.cell(row=row, column=2, value="Count")
    row += 1
    sev_counts = report_model.defect_severity_counts(cases)
    for severity, count in sev_counts.items():
        ws.cell(row=row, column=1, value=severity)
        ws.cell(row=row, column=2, value=count)
        row += 1
    _style_subtable(ws, header_row, row - 1, 2)

    # Summary note UNDER the Defects By Severity table (a short written verdict).
    total_def = sum(sev_counts.values())
    executed = sum(1 for c in cases if (c.status or "").strip() not in ("", "Not Run", "Skipped"))
    if total_def == 0:
        note = ("Note: No defects were logged — all executed test cases passed. "
                "No Critical/High/Medium/Low severity issues were found.")
    else:
        crit_high = sev_counts.get("Critical", 0) + sev_counts.get("High", 0)
        parts = ", ".join(f"{s}: {sev_counts.get(s,0)}" for s in ("Critical","High","Medium","Low"))
        verdict = ("blocking issues require a fix before release"
                   if crit_high else "no blocking (Critical/High) issues were found")
        note = (f"Note: {total_def} defect(s) logged over {executed} executed case(s) — "
                f"{parts}. {crit_high} Critical/High → {verdict}.")
    ws.cell(row=row, column=1, value=note)
    ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=5)
    nc = ws.cell(row=row, column=1)
    nc.font = Font(italic=True, color="566573", size=10)
    nc.alignment = WRAP_TOPLEFT
    ws.row_dimensions[row].height = 30
    row += 1

    ws.column_dimensions["A"].width = 24
    ws.column_dimensions["B"].width = 16
    ws.column_dimensions["C"].width = 12
    ws.column_dimensions["D"].width = 12
    ws.column_dimensions["E"].width = 12
    _set_tab_color(ws, ws.title)
    return ws


def _section_band(ws: Worksheet, row: int, text: str, ncols: int) -> None:
    """Write a professional section-label band: navy fill + white bold text,
    merged across ``ncols`` so it reads as a clean header for the sub-table
    below it."""
    ws.cell(row=row, column=1, value=text)
    if ncols > 1:
        ws.merge_cells(start_row=row, start_column=1, end_row=row, end_column=ncols)
    for col in range(1, ncols + 1):
        cell = ws.cell(row=row, column=col)
        cell.fill = HEADER_FILL
        cell.font = HEADER_FONT
        cell.border = THIN_BORDER
    ws.cell(row=row, column=1).alignment = Alignment(horizontal="left", vertical="center")


def _style_subtable(ws: Worksheet, header_row: int, last_row: int, ncols: int) -> None:
    """Style a small Summary breakdown table: navy header band (white bold) +
    light borders, matching the professional main-table header look."""
    for col in range(1, ncols + 1):
        cell = ws.cell(row=header_row, column=col)
        cell.font = HEADER_FONT
        cell.fill = HEADER_FILL
        cell.alignment = HEADER_ALIGN
    _border_block(ws, header_row, last_row, ncols)


def add_traceability_sheet(wb: Workbook, cases: list[TestCase]) -> Worksheet:
    """Append a ``Traceability`` (RTM) sheet mapping user stories to TC ids."""
    ws = wb.create_sheet(title="Traceability")
    ws.append(list(TRACEABILITY_HEADERS))
    for row in report_model.build_traceability(cases):
        ws.append(
            [
                row["user_story_id"],
                row["tc_ids"],
                row["tc_count"],
                row["passed"],
                row["failed"],
                row["coverage"],
            ]
        )
    ncols = len(TRACEABILITY_HEADERS)
    _style_header_row(ws, 1, ncols)
    _border_block(ws, 1, ws.max_row, ncols)
    ws.freeze_panes = "A2"
    widths = {"A": 16, "B": 40, "C": 8, "D": 8, "E": 8, "F": 10}
    for col, width in widths.items():
        ws.column_dimensions[col].width = width
    # Wrap every data cell so nothing spills past its border, then size rows.
    width_by_index = {i + 1: w for i, w in enumerate([16, 40, 8, 8, 8, 10])}
    for r in range(2, ws.max_row + 1):
        for c in range(1, ncols + 1):
            ws.cell(row=r, column=c).alignment = WRAP_TOPLEFT
    _autosize_row_heights(ws, 2, ws.max_row, width_by_index)
    _set_tab_color(ws, ws.title)
    return ws


def add_defect_log_sheet(wb: Workbook, cases: list[TestCase]) -> Worksheet:
    """Append a ``DefectLog`` sheet, one row per failed/blocked case.

    When the suite has no failures a single "— no defects —" row is written
    beneath the header so the sheet is never empty.
    """
    ws = wb.create_sheet(title="DefectLog")
    ws.append(list(DEFECTLOG_HEADERS))
    rows = report_model.build_defect_log(cases)
    if rows:
        for r in rows:
            ws.append(
                [
                    r["defect_id"],
                    r["linked_tc_id"],
                    r["title"],
                    r["steps"],
                    r["severity"],
                    r["priority"],
                    r["status"],
                    r["assigned_to"],
                    r["root_cause"],
                    r["resolution"],
                    r["found_date"],
                    r["fixed_date"],
                ]
            )
    else:
        ws.append(["— no defects —"] + [""] * (len(DEFECTLOG_HEADERS) - 1))

    ncols = len(DEFECTLOG_HEADERS)
    _style_header_row(ws, 1, ncols)
    _border_block(ws, 1, ws.max_row, ncols)
    ws.freeze_panes = "A2"

    # Column letters by header position (1-based).
    severity_col = DEFECTLOG_HEADERS.index("Severity") + 1
    status_col = DEFECTLOG_HEADERS.index("Status") + 1
    # Long free-text columns wrap; everything else stays compact.
    wrap_cols = {
        DEFECTLOG_HEADERS.index(h) + 1
        for h in ("Title", "Steps to Reproduce", "Suggested Root Cause", "Resolution")
    }
    if rows:
        for r in range(2, ws.max_row + 1):
            sev = ws.cell(row=r, column=severity_col).value
            if sev in SEVERITY_FILLS:
                ws.cell(row=r, column=severity_col).fill = SEVERITY_FILLS[sev]
            st_val = ws.cell(row=r, column=status_col).value
            if st_val in DEFECT_STATUS_FILLS:
                ws.cell(row=r, column=status_col).fill = DEFECT_STATUS_FILLS[st_val]
            for c in wrap_cols:
                ws.cell(row=r, column=c).alignment = WRAP_TOPLEFT

    widths = {
        "A": 12, "B": 14, "C": 30, "D": 32, "E": 11, "F": 10,
        "G": 12, "H": 14, "I": 30, "J": 24, "K": 20, "L": 16,
    }
    for col, width in widths.items():
        ws.column_dimensions[col].width = width
    # Wrap every data cell so nothing spills past its border, then size rows.
    if rows:
        width_by_index = {
            idx + 1: widths[get_column_letter(idx + 1)] for idx in range(ncols)
        }
        for r in range(2, ws.max_row + 1):
            for c in range(1, ncols + 1):
                ws.cell(row=r, column=c).alignment = WRAP_TOPLEFT
        _autosize_row_heights(ws, 2, ws.max_row, width_by_index)
    _set_tab_color(ws, ws.title)
    return ws


def _next_data_row(ws: Worksheet) -> int:
    """Return the 1-based row index after the last populated data row.

    Header is row 1, so the first data row is 2.
    """
    return ws.max_row + 1


def backup_results_to_history(wb: Workbook, timestamp: str | None = None) -> Worksheet:
    """Copy the current ``TestCases`` result columns into a ``History_<ts>`` sheet.

    Used on a re-run to preserve prior execution values before the result
    columns are cleared. The history sheet stores the row id alongside each
    backed-up result field so a prior run can be correlated by Test_Case id.

    Returns the created history sheet. Raises ``KeyError`` if there is no
    ``TestCases`` sheet to back up.
    """
    if "TestCases" not in wb.sheetnames:
        raise KeyError("No 'TestCases' sheet to back up")

    src = wb["TestCases"]
    if timestamp is None:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    title = f"History_{timestamp}"
    hist = wb.create_sheet(title=title)

    # Header: id + the result fields being preserved.
    backup_fields = ("id",) + RESULT_FIELDS
    hist.append(list(backup_fields))

    col_index = {name: COLUMNS.index(name) + 1 for name in backup_fields}
    for row in range(2, src.max_row + 1):
        # Skip fully empty rows.
        id_val = src.cell(row=row, column=col_index["id"]).value
        if id_val in (None, ""):
            continue
        hist.append([src.cell(row=row, column=col_index[name]).value for name in backup_fields])

    _set_tab_color(hist, title)
    return hist


def clear_result_columns(ws: Worksheet) -> None:
    """Blank out the run-stage result columns on every data row in ``ws``."""
    col_index = {name: COLUMNS.index(name) + 1 for name in RESULT_FIELDS}
    for row in range(2, ws.max_row + 1):
        for name in RESULT_FIELDS:
            ws.cell(row=row, column=col_index[name]).value = None


# Screenshot cell box (points/pixels). The image is anchored INTO the cell with a
# TwoCellAnchor (editAs="twoCell" → moves AND hides with its row on filter/sort),
# sized to this box while preserving aspect ratio (no distortion, no floating).
_SHOT_BOX_W_PX = 220
_SHOT_BOX_H_PX = 150
_SHOT_COL_WIDTH = 34      # Excel column-width units (~ chars)
_SHOT_ROW_HEIGHT_PT = 120  # points


def _resolve_asset(assets_root: str | None, rel_path: str) -> str | None:
    """Resolve a skill-relative screenshot path to an existing file.

    Screenshot paths are stored rooted at ``qa-automation-testing/...``. When an
    ``assets_root`` (the run's qa_dir) is given, strip that leading segment and
    join under it; otherwise use the path as-is (works when CWD is the project).
    Returns the path only when the file exists on disk.
    """
    if not rel_path:
        return None
    if os.path.exists(rel_path):
        return rel_path
    if assets_root:
        rel = rel_path.replace("\\", "/").strip()
        parts = rel.split("/", 1)
        tail = parts[1] if len(parts) == 2 and parts[0] == "qa-automation-testing" else rel
        candidate = os.path.join(assets_root, tail)
        if os.path.exists(candidate):
            return candidate
    return None


def _embed_screenshot(ws: Worksheet, row: int, screenshot_path: str,
                      assets_root: str | None = None) -> None:
    """Embed the real image at ``screenshot_path`` anchored INSIDE the cell.

    The target column is computed from the column order (the ``screenshot``
    field), never hardcoded. The image is placed with a **TwoCellAnchor**
    (``editAs="twoCell"`` — "move and size with cells") pinned to the data row's
    cell, so it **moves with the row AND is hidden by Excel when that row is
    filtered/sorted out** (unlike a floating OneCellAnchor image). The image is
    scaled to fit :data:`_SHOT_BOX_W_PX` ×
    :data:`_SHOT_BOX_H_PX` while **preserving aspect ratio** (no distortion). If
    the file is missing the cell is left blank; any read/anchor failure is
    swallowed so a bad image never breaks the workbook.
    """
    resolved = _resolve_asset(assets_root, screenshot_path)
    if not resolved:
        return
    screenshot_path = resolved
    try:
        from openpyxl.drawing.spreadsheet_drawing import AnchorMarker, TwoCellAnchor
        from openpyxl.utils.units import pixels_to_EMU

        col0 = COLUMNS.index("screenshot")  # 0-based column index
        col_letter = get_column_letter(col0 + 1)

        img = XLImage(screenshot_path)
        # Scale to fit the box, preserving aspect ratio.
        try:
            ow, oh = float(img.width), float(img.height)
        except Exception:
            ow, oh = float(_SHOT_BOX_W_PX), float(_SHOT_BOX_H_PX)
        scale = min(_SHOT_BOX_W_PX / ow, _SHOT_BOX_H_PX / oh, 1.0) if ow and oh else 1.0
        w, h = int(ow * scale), int(oh * scale)
        img.width, img.height = w, h

        # TwoCellAnchor with editAs="twoCell" ("Move and size with cells"): the
        # image is tied to its DATA ROW's cell, so it moves with the row AND — the
        # key fix — Excel HIDES it when that row is hidden by an AutoFilter/sort
        # (a OneCellAnchor image floats and stays visible, mis-aligning the sheet
        # after filtering). Both anchor markers sit in the SAME screenshot cell,
        # with the `to` marker offset by exactly the scaled image size, so the
        # from→to box equals w×h and the aspect ratio is preserved (no stretching).
        margin = pixels_to_EMU(4)
        _from = AnchorMarker(col=col0, colOff=margin, row=row - 1, rowOff=margin)
        _to = AnchorMarker(
            col=col0, colOff=margin + pixels_to_EMU(w),
            row=row - 1, rowOff=margin + pixels_to_EMU(h),
        )
        img.anchor = TwoCellAnchor(editAs="twoCell", _from=_from, to=_to)
        ws.add_image(img)
        # Size the cell so the image sits comfortably inside it.
        if (ws.column_dimensions[col_letter].width or 0) < _SHOT_COL_WIDTH:
            ws.column_dimensions[col_letter].width = _SHOT_COL_WIDTH
        if (ws.row_dimensions[row].height or 0) < _SHOT_ROW_HEIGHT_PT:
            ws.row_dimensions[row].height = _SHOT_ROW_HEIGHT_PT
    except Exception:
        # Tolerate unreadable/corrupt images: leave the cell blank.
        pass


def _device_sheet_title(device: str) -> str:
    """Sheet title for a per-device results sheet (e.g. 'desktop' -> 'Desktop')."""
    return str(device).strip().capitalize()[:31] or "Device"


def _detail_locations(cases: list[TestCase]) -> dict[str, tuple[str, int]]:
    """Map each case id -> (per-type sheet title, row) for INDEX hyperlinks.

    Mirrors the per-type sheet layout built by :func:`write_xlsx`
    (:func:`report_model.group_by_test_type`, header on row 1, data from row 2),
    so a link always lands on the case's own detail row.
    """
    loc: dict[str, tuple[str, int]] = {}
    for type_name, group in report_model.group_by_test_type(cases):
        for i, case in enumerate(group):
            loc[case.id] = (type_name, i + 2)
    return loc


def _device_type_label(case: TestCase) -> str:
    """Human label of the devices a case was tested on (canonical order)."""
    results = case.device_results if isinstance(case.device_results, dict) else {}
    ordered = [d for d in report_model.DEVICES if d in results]
    extras = sorted(d for d in results if d not in report_model.DEVICES)
    names = [d.capitalize() for d in (*ordered, *extras)]
    return ", ".join(names) if names else "—"


def _add_index_sheet(wb: Workbook, cases: list[TestCase]) -> Worksheet:
    """Add the concise ``TestCases`` INDEX sheet (important columns only).

    Columns: TC ID · Test Type · Module · Scenario · Priority · Device Type ·
    Status. Each **TC ID** is a hyperlink jumping to that case's full detail row
    in its per-type sheet, so the reader clicks an id to see everything.
    """
    ws = wb.create_sheet(title="TestCases")
    ws.append(list(INDEX_HEADERS))
    for c in cases:
        ws.append(
            [
                c.id,
                report_model.effective_test_type(c),
                c.module,
                c.scenario,
                c.priority,
                _device_type_label(c),
                c.status,
            ]
        )

    ncols = len(INDEX_HEADERS)
    last_row = ws.max_row
    _style_header_row(ws, 1, ncols)
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = f"A1:{get_column_letter(ncols)}{max(1, last_row)}"

    widths = [14, 15, 18, 46, 10, 22, 12]
    for i, w in enumerate(widths):
        ws.column_dimensions[get_column_letter(i + 1)].width = w

    loc = _detail_locations(cases)
    status_col = INDEX_HEADERS.index("Status") + 1
    if last_row >= 2:
        _border_block(ws, 1, last_row, ncols)
        for r in range(2, last_row + 1):
            for c in range(1, ncols + 1):
                ws.cell(row=r, column=c).alignment = WRAP_TOPLEFT
            # TC id -> internal hyperlink to its per-type detail row.
            tc = ws.cell(row=r, column=1).value
            target = loc.get(tc)
            if target:
                sheet_title, row_no = target
                id_cell = ws.cell(row=r, column=1)
                id_cell.hyperlink = Hyperlink(
                    ref=id_cell.coordinate, location=f"'{sheet_title}'!A{row_no}"
                )
                id_cell.font = LINK_FONT
            # Color the status cell by value.
            sv = ws.cell(row=r, column=status_col).value
            if sv in STATUS_FILLS:
                ws.cell(row=r, column=status_col).fill = STATUS_FILLS[sv]
        _autosize_row_heights(
            ws, 2, last_row, {i + 1: widths[i] for i in range(ncols)}
        )
    _set_tab_color(ws, "TestCases")
    return ws


def write_xlsx(cases: list[TestCase], out_path: str, meta: dict | None = None,
               assets_root: str | None = None) -> None:
    """Write ``cases`` to the single standard QA workbook at ``out_path``.

    One workbook. Tab order:

        Info → Summary → TestCases (concise INDEX, TC ids hyperlinked) →
        per-type detail sheets → per-device detail sheets → Traceability →
        DefectLog

    The **TestCases** sheet is a compact index (important columns only) whose TC
    ids link to each case's full detail. The **per-type** sheets
    (UI-E2E / UI-Functional / API / Unit-Integration) carry the full canonical
    columns + embedded screenshots (the link targets). One **per-device** sheet
    (Desktop / Tablet / Mobile) is added for each device tested, showing that
    device's own results (:func:`report_model.device_view`). Summary carries a
    **By Device** table; its formulas read the INDEX id/status columns.
    """
    device_counts = report_model.device_summary_counts(cases) or None

    wb = Workbook()

    # Sheets are created in tab order. The default active sheet becomes Info.
    info_ws = wb.active
    info_ws.title = "Info"
    _fill_info(info_ws, meta, cases)

    add_summary_sheet(wb, cases, meta, device_counts=device_counts)

    # Concise INDEX sheet (right after Summary), TC ids hyperlinked to detail.
    _add_index_sheet(wb, cases)

    # Per-type DETAIL sheets (full columns + embedded screenshots) — link targets.
    for type_name, group in report_model.group_by_test_type(cases):
        _add_cases_sheet(wb, type_name, group, embed_images=True, assets_root=assets_root)

    # Per-device DETAIL sheets (full columns, device-scoped via device_view).
    # Embed each device's OWN screenshot (device_view carries the per-device
    # screenshot_path) so by-device sheets show evidence too, not just UI-E2E.
    for device in report_model.devices_tested(cases):
        views = report_model.device_views(cases, device)
        _add_cases_sheet(wb, _device_sheet_title(device), views, embed_images=True,
                         assets_root=assets_root)

    add_traceability_sheet(wb, cases)
    add_defect_log_sheet(wb, cases)

    wb.save(out_path)
    # Excel-safety: reload the saved file and assert every sheet is readable.
    _verify_readable(out_path)


def _format_steps(value: object) -> str:
    """Render the ``steps`` field as a numbered, one-per-line list.

    Splits the stored steps (``;``-separated and/or newline-separated) into
    individual actions and renders them as ``1. …`` / ``2. …`` / ``3. …`` on
    separate lines. Any pre-existing ``N.`` / ``N)`` prefix on a step is stripped
    first so steps are not double-numbered. A single step (or empty) is returned
    as-is. Cells that hold steps are wrapped, so the line breaks show in Excel.
    """
    text = "" if value is None else str(value)
    parts = [
        re.sub(r"^\s*\d+[.)]\s*", "", p.strip())
        for p in re.split(r"[;\n]+", text)
        if p.strip()
    ]
    if len(parts) <= 1:
        return text.strip()
    return "\n".join(f"{i}. {p}" for i, p in enumerate(parts, 1))


# Placeholder for a genuinely empty test-case cell so NO column ever looks blank
# in the document (per the "if no data, put -" rule). The Info sheet uses "N/A"
# instead; this dash is only for the per-case detail tables.
CELL_DASH = "-"


def _rollup_evidence(case: TestCase, field: str) -> str:
    """Return ``case.<field>`` or, when empty, the first per-device value.

    Used for ``screenshot_path`` / ``video_path`` so the top-level (per-type) sheet
    still shows a path even when only the per-device results carry it.
    """
    val = str(getattr(case, field, "") or "").strip()
    if val:
        return val
    dr = case.device_results if isinstance(case.device_results, dict) else {}
    for d in report_model.DEVICES:
        r = dr.get(d)
        if isinstance(r, dict) and str(r.get(field, "") or "").strip():
            return str(r[field]).strip()
    for r in dr.values():  # any non-canonical device
        if isinstance(r, dict) and str(r.get(field, "") or "").strip():
            return str(r[field]).strip()
    return ""


def _display_value(case: TestCase, field: str) -> str:
    """Resolve the DISPLAY value for a detail-sheet cell — never an empty blank.

    - ``screenshot`` stays empty (the image lives in that cell).
    - ``severity`` / ``risk_level`` are DERIVED when unset (never blank).
    - ``steps`` is rendered as a numbered list.
    - ``screenshot_path`` / ``video_path`` roll up from per-device results.
    - anything else that is empty renders as ``-`` so no column looks missing.
    """
    if field == "screenshot":
        return ""
    if field == "severity":
        return report_model.derive_severity(case)
    if field == "risk_level":
        return report_model.derive_risk_level(case)
    if field == "steps":
        return _format_steps(case.steps) or CELL_DASH
    if field in ("screenshot_path", "video_path"):
        return _rollup_evidence(case, field) or CELL_DASH
    val = getattr(case, field, "")
    text = str(val).strip() if val not in (None, "") else ""
    return text if text else CELL_DASH


def _add_cases_sheet(
    wb: Workbook, title: str, cases: list[TestCase], embed_images: bool,
    assets_root: str | None = None
) -> Worksheet:
    """Create a Test_Cases sheet (``title``) with the canonical columns.

    Writes the header + one row per case in :data:`COLUMNS` order, optionally
    embedding the real screenshot image into each row's ``screenshot`` cell
    (detail sheets only), then applies the professional table styling. The
    ``steps`` field is rendered as a numbered, one-per-line list.
    """
    ws = wb.create_sheet(title=title)
    ws.append(list(HEADERS))
    for offset, c in enumerate(cases):
        row = [_display_value(c, f) for f in COLUMNS]
        ws.append(row)
        if embed_images:
            # Embed from the resolved (possibly rolled-up) screenshot path.
            _embed_screenshot(ws, offset + 2, _rollup_evidence(c, "screenshot_path"),
                              assets_root=assets_root)
    _style_testcases_sheet(ws, len(cases), embed_images=embed_images)
    return ws


def _style_testcases_sheet(ws: Worksheet, ndata: int, embed_images: bool = True) -> None:
    """Apply the professional table styling to a Test_Cases sheet.

    Styles the header band, freezes the header row, adds an autofilter over the
    header, sets per-column widths, wraps long free-text columns, fills the
    status cell on each data row by value, and borders the whole table. When
    ``embed_images`` is True the screenshot column width / row heights set during
    image embedding are left untouched so embedded images stay visible; when
    False (per-type view sheets) the screenshot column is styled like any other.
    """
    ncols = len(COLUMNS)
    last_data_row = ndata + 1  # header is row 1

    _style_header_row(ws, 1, ncols)
    ws.freeze_panes = "A2"
    last_col_letter = get_column_letter(ncols)
    ws.auto_filter.ref = f"A1:{last_col_letter}{max(1, last_data_row)}"

    screenshot_col = COLUMNS.index("screenshot") + 1
    status_col = COLUMNS.index("status") + 1

    # Per-column widths (when embedding, skip screenshot: the embed step sizes it).
    for idx, field in enumerate(COLUMNS):
        if field == "screenshot" and embed_images:
            continue
        letter = get_column_letter(idx + 1)
        width = min(_TC_COLUMN_WIDTHS.get(field, 16), _MAX_COL_WIDTH)
        ws.column_dimensions[letter].width = width

    if ndata:
        _border_block(ws, 1, last_data_row, ncols)
        width_by_index: dict[int, float] = {}
        for idx, field in enumerate(COLUMNS):
            if field == "screenshot" and embed_images:
                continue
            width_by_index[idx + 1] = min(
                _TC_COLUMN_WIDTHS.get(field, 16), _MAX_COL_WIDTH
            )
        for r in range(2, last_data_row + 1):
            for c in range(1, ncols + 1):
                if c == screenshot_col and embed_images:
                    continue  # image cell: leave the embed sizing untouched
                ws.cell(row=r, column=c).alignment = WRAP_TOPLEFT
            status_val = ws.cell(row=r, column=status_col).value
            if status_val in STATUS_FILLS:
                ws.cell(row=r, column=status_col).fill = STATUS_FILLS[status_val]
        _autosize_row_heights(ws, 2, last_data_row, width_by_index)

    _set_tab_color(ws, ws.title)


def main(argv: list[str] | None = None) -> int:
    import sys

    args = argv if argv is not None else sys.argv[1:]
    if len(args) not in (2, 3):
        print(
            "Usage: python report_xlsx.py <test_cases.json> <out.xlsx> [report_meta.json]",
            file=sys.stderr,
        )
        return 2

    json_path, out_path = args[0], args[1]
    meta = report_docx_load_meta(args[2]) if len(args) == 3 else None
    cases = report_model.load(json_path)
    write_xlsx(cases, out_path, meta)
    print(f"Wrote {len(cases)} test case(s) to {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
