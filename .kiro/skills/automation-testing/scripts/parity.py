"""Report parity check for the Reporting_Engine.

Both report writers consume the *same* ordered ``report_model.TestCase`` list,
but they now render very different documents: ``report_xlsx`` writes the full
18-column data table while ``report_docx`` writes a formatted, sectioned Test
Execution Report. Full field-for-field mirroring is therefore no longer the
right contract.

Parity is **relaxed to id + status**: every ``Test_Case`` id present in the
XLSX ``TestCases`` sheet must also appear in the DOCX report's detailed-result
tables with the **same status**. This module proves it by writing both reports
to temporary files, parsing each output back independently (openpyxl for the
XLSX ``TestCases`` sheet, python-docx for the DOCX detailed tables), and
asserting the DOCX ``{id: status}`` map contains every XLSX ``{id: status}``
entry.

Design reference: design.md "Single Source of Truth → Two Writers
(Report_Parity)" and Property 12.

CLI:
    python parity.py <test_cases.json>
"""

from __future__ import annotations

import os
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

# Ensure sibling modules (report_model/report_xlsx/report_docx) are importable
# when this file is run directly as a CLI from another working directory.
sys.path.insert(0, str(Path(__file__).resolve().parent))

from docx import Document
from openpyxl import load_workbook

import report_docx
import report_model
import report_xlsx
from report_model import TestCase


# The relaxed parity surface: id + status only. Both reports must agree that a
# Test_Case with a given id ended with a given status.
KEY_FIELDS: tuple[str, ...] = ("id", "status")


class ParityError(AssertionError):
    """Raised when the XLSX and DOCX reports disagree on a Test_Case status."""


@dataclass
class ParityResult:
    """Structured result of a parity check.

    Attributes:
        ok: True when the DOCX detailed tables cover every XLSX id with an
            equal status.
        row_count: Number of XLSX Test_Case ids compared.
        xlsx_status: ``{id: status}`` extracted from the XLSX ``TestCases`` sheet.
        docx_status: ``{id: status}`` extracted from the DOCX detailed tables.
        mismatches: Human-readable descriptions of any differences found.
    """

    ok: bool
    row_count: int
    xlsx_status: dict[str, str] = field(default_factory=dict)
    docx_status: dict[str, str] = field(default_factory=dict)
    mismatches: list[str] = field(default_factory=list)


# Prevent pytest from collecting these as test classes.
ParityResult.__test__ = False


def _normalize_cell(value) -> str:
    """Normalize a parsed cell value to the canonical string form.

    openpyxl returns ``None`` for empty cells while python-docx returns ``""``;
    both correspond to the model's empty-string fields. Coerce everything to a
    string so the two extraction paths compare apples to apples.
    """
    if value is None:
        return ""
    return value if isinstance(value, str) else str(value)


def _extract_xlsx(path: str) -> dict[str, str]:
    """Extract an ``{id: status}`` map from the XLSX ``TestCases`` sheet.

    Rows are read in sheet order; for duplicate ids the last row wins (the
    model is the single source so duplicates are not expected in practice).
    """
    wb = load_workbook(path)
    ws = wb["TestCases"]

    header = [ws.cell(row=1, column=c).value for c in range(1, ws.max_column + 1)]
    # Normalize header names so the check works whether the sheet uses model
    # field names ("id"/"status") or the concise INDEX labels ("TC ID"/"Status").
    norm = {str(name).strip().lower(): idx + 1 for idx, name in enumerate(header)}
    id_col = norm.get("id") or norm.get("tc id")
    status_col = norm.get("status")
    if id_col is None or status_col is None:
        return {}

    result: dict[str, str] = {}
    for row in range(2, ws.max_row + 1):
        id_val = ws.cell(row=row, column=id_col).value
        if id_val in (None, ""):
            continue
        result[_normalize_cell(id_val)] = _normalize_cell(
            ws.cell(row=row, column=status_col).value
        )
    return result


def _extract_docx(path: str, known_ids: set[str], cases_for_lang) -> dict[str, str]:
    """Extract an ``{id: status}`` map from the DOCX detailed-result tables.

    Only the detailed-results tables (whose header row equals
    ``report_docx.DETAIL_HEADERS``) are scanned; every other table — including
    the section 6 defect table, which also carries a ``Status`` column — is
    ignored so a Defect ID can never be mistaken for a Test_Case status. For
    each detailed-results data row whose first cell is a known Test_Case id, the
    row contributes ``{id: <that row's status cell>}``.
    """
    doc = Document(path)
    result: dict[str, str] = {}

    # Detail-table headers are language-dependent; resolve the same language the
    # writer used, then take the (fixed-position) Status column index (7th).
    import labels
    # Detail-table headers follow the CONTENT language (the writer keeps table
    # column headers in the body/content language; only the numbered section and
    # document-structure headings default to English). Resolve the same content
    # language the writer used so the header row matches exactly.
    lang = report_docx._report_language(None, cases_for_lang)
    detail_headers = list(labels.get(lang)["headers"]["detail"])
    status_idx = 6

    for table in doc.tables:
        if not table.rows:
            continue
        header = [cell.text for cell in table.rows[0].cells]
        if header != detail_headers:
            continue
        for row in table.rows[1:]:
            cells = row.cells
            if not cells:
                continue
            id_text = cells[0].text
            if id_text in known_ids:
                status = cells[status_idx].text if status_idx < len(cells) else ""
                result[id_text] = status
    return result


def compute_parity(cases: list[TestCase]) -> ParityResult:
    """Write both reports, parse them back, and compare id + status.

    Args:
        cases: The single-source-of-truth ordered list of Test_Cases.

    Returns:
        A :class:`ParityResult` whose ``ok`` is True when the DOCX detailed
        tables contain every XLSX id with an equal status.
    """
    with tempfile.TemporaryDirectory() as tmp:
        xlsx_path = os.path.join(tmp, "report.xlsx")
        docx_path = os.path.join(tmp, "report.docx")

        report_xlsx.write_xlsx(cases, xlsx_path)
        report_docx.write_docx(cases, docx_path)

        xlsx_status = _extract_xlsx(xlsx_path)
        docx_status = _extract_docx(docx_path, set(xlsx_status), cases)

    mismatches: list[str] = []
    for id_value, status in xlsx_status.items():
        if id_value not in docx_status:
            mismatches.append(
                f"id {id_value!r} present in XLSX is missing from the DOCX detailed results"
            )
        elif docx_status[id_value] != status:
            mismatches.append(
                f"id {id_value!r} status differs: "
                f"xlsx={status!r} docx={docx_status[id_value]!r}"
            )

    return ParityResult(
        ok=not mismatches,
        row_count=len(xlsx_status),
        xlsx_status=xlsx_status,
        docx_status=docx_status,
        mismatches=mismatches,
    )


def check_parity(cases: list[TestCase]) -> bool:
    """Assert the two reports are in parity, returning True on success.

    Raises:
        ParityError: If the DOCX report omits an XLSX id or disagrees on its
            status. The error message lists the first differences.
    """
    result = compute_parity(cases)
    if not result.ok:
        detail = "\n".join(result.mismatches[:20])
        raise ParityError(f"Report parity check failed:\n{detail}")
    return True


def check_parity_json(json_path: str) -> bool:
    """Load Test_Cases from ``json_path`` and run :func:`check_parity`."""
    cases = report_model.load(json_path)
    return check_parity(cases)


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    if len(args) != 1:
        print("Usage: python parity.py <test_cases.json>", file=sys.stderr)
        return 2

    try:
        check_parity_json(args[0])
    except ParityError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    print("Report parity OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
