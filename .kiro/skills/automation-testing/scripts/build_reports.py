"""Deterministic one-command report build — identical output on ANY tool.

This is the **canonical entrypoint** for the Report stage. Whatever AI tool or
agent drives the skill (Kiro, Amazon Q, Claude, Cursor, Windsurf, or a plain
shell), running this single command against the same ``data/test_cases.json``
produces the **exact same** ``doc/`` tree — same files, same paths, same layout —
because every path comes from :mod:`paths` and every document is rendered by the
bundled writers (one model → XLSX + DOCX). No per-tool variance.

What it writes (all under ``<qa_dir>/doc/``, system-named from report_meta):
  * ``Test_Case_Summary - <system>.xlsx``          — the single QA workbook (Info →
    Summary → TestCases → per-type sheets → per-device sheets → Traceability →
    DefectLog)
  * ``Application_Testing_Report - <system>.docx`` — the consolidated Word report

The HTML report is intentionally not produced — the deliverables are the XLSX + DOCX.

Inputs (under ``<qa_dir>/data/``): ``test_cases.json`` (required) and the optional
``report_meta.json``.

CLI:
    python build_reports.py [qa_dir]        # qa_dir defaults to qa-automation-testing
"""

from __future__ import annotations

import os
import platform
import shutil
import subprocess
import sys
from datetime import datetime

import paths
import report_docx
import report_model
import report_xlsx
import validate
from report_docx import DASH, normalize_meta


class ValidationError(ValueError):
    """Raised when test_cases.json / report_meta.json fail pre-flight validation."""


def _tool_version(cmd: list[str]) -> str:
    """Best-effort ``<cmd>`` version string; '' when the tool is unavailable."""
    exe = cmd[0]
    if not shutil.which(exe):
        return ""
    try:
        out = subprocess.run(cmd, capture_output=True, text=True, timeout=8)
        return (out.stdout or out.stderr or "").strip().splitlines()[0].strip()
    except Exception:
        return ""


def enrich_env_meta(meta: dict | None) -> dict:
    """Fill MISSING environment fields with real, auto-detected values.

    The Info sheet is *information* — it should carry real data, not "—". This
    detects the OS, Node, and Playwright versions of the machine building the
    report (which, in this skill's flow, is where the tests ran) and stamps the
    execution date, but only for fields the user did not already supply. Author
    values always win; project-specific fields (company/system) are never
    invented. Detection failures are ignored (the field stays "—").
    """
    enriched = dict(meta or {})
    norm = normalize_meta(enriched)

    def missing(field: str) -> bool:
        return norm.get(field) in (DASH, "", None)

    if missing("os"):
        try:
            enriched["os"] = platform.platform()
        except Exception:
            pass
    if missing("node_version"):
        v = _tool_version(["node", "--version"])
        if v:
            enriched["node"] = v
    pw_ver = ""
    if missing("playwright_version"):
        pw_ver = _tool_version(["npx", "playwright", "--version"])
        if pw_ver:
            pw_ver = pw_ver.replace("Version ", "").strip()
            enriched["playwright"] = pw_ver
    if missing("browser_version"):
        # Which browsers ran (QA_BROWSERS, default chromium); annotate with the
        # Playwright version so Info shows a real value instead of "—".
        browsers = os.environ.get("QA_BROWSERS", "chromium").strip() or "chromium"
        label = ", ".join(b.strip().capitalize() for b in browsers.split(",") if b.strip())
        if pw_ver or normalize_meta(enriched).get("playwright_version") not in (DASH, "", None):
            pv = pw_ver or normalize_meta(enriched).get("playwright_version")
            enriched["browser"] = f"{label} (Playwright {pv})"
        else:
            enriched["browser"] = label
    if missing("executed_date"):
        enriched["executed_date"] = datetime.now().strftime("%Y-%m-%d")
    return enriched


def _abs(qa_dir: str, rel: str) -> str:
    """Resolve a skill-relative path (rooted at ``qa-automation-testing/``) under ``qa_dir``.

    ``paths`` returns paths rooted at ``qa-automation-testing/...``; strip that
    prefix and join under the caller's ``qa_dir`` so the same helpers work no
    matter what the target directory is named.
    """
    prefix = paths.QA_ROOT + "/"
    tail = rel[len(prefix):] if rel.startswith(prefix) else rel
    return os.path.join(qa_dir, tail)


def build_reports(qa_dir: str = paths.QA_ROOT) -> list[str]:
    """Render the full ``doc/`` tree deterministically. Returns files written (sorted).

    Raises ``FileNotFoundError`` when ``data/test_cases.json`` is missing.
    """
    data_json = _abs(qa_dir, paths.test_cases_json())
    if not os.path.exists(data_json):
        raise FileNotFoundError(f"no {paths.test_cases_json()} under {qa_dir!r}")

    meta_path = _abs(qa_dir, paths.report_meta_json())

    # Pre-flight validation: fail fast with clear messages instead of an opaque
    # crash deep in a writer. test_cases errors are fatal; recommended-field
    # gaps and any report_meta issue that is non-fatal are printed as warnings.
    errors, warnings = validate.validate_test_cases(data_json)
    errors += validate.validate_report_meta(meta_path)
    for w in warnings:
        print(f"⚠️  {w}", file=sys.stderr)
    if errors:
        raise ValidationError(
            "input validation failed:\n" + "\n".join(f"  - {e}" for e in errors)
        )

    raw_meta = report_docx.load_meta(meta_path) if os.path.exists(meta_path) else {}
    # Warn (don't fail) when no authored narrative is present: the documents will
    # use the engine's computed/default prose (which is always result-consistent),
    # but NOT the project-specific analysis the Evaluate helper would author. This
    # nudges the Report stage to run `automation-testing-evaluate` first.
    if not report_docx._extract_narrative(raw_meta):
        print(
            "⚠️  report_meta has no authored 'narrative' — the reports will use the "
            "engine's generic (but result-consistent) prose. Run the "
            "automation-testing-evaluate helper first for analysed, project-specific "
            "content.",
            file=sys.stderr,
        )
    # Enrich Info with real auto-detected environment (OS/Node/Playwright/date)
    # for any field the user did not supply, so Info shows real data not "—".
    meta = enrich_env_meta(raw_meta)

    cases = report_model.load(data_json)

    # Professional, system-named output files (e.g.
    # "Application_Testing_Report - <system>.docx"). Falls back to the base name
    # when no system name is supplied.
    sys_name = normalize_meta(meta).get("system_name")
    label = None if sys_name in (DASH, "", None) else str(sys_name)

    # Ensure the document folder exists (idempotent).
    os.makedirs(_abs(qa_dir, paths.DOC_ROOT), exist_ok=True)

    written: list[str] = []

    # Single workbook (per-device results live in per-device sheets inside it).
    xlsx_out = _abs(qa_dir, paths.xlsx_report(label))
    report_xlsx.write_xlsx(cases, xlsx_out, meta, assets_root=qa_dir)
    written.append(xlsx_out)

    # Single consolidated Word report.
    docx_out = _abs(qa_dir, paths.docx_report(label))
    report_docx.write_docx(cases, docx_out, meta, assets_root=qa_dir)
    written.append(docx_out)

    # NOTE: the HTML report is intentionally NOT generated — the deliverables are
    # the XLSX workbook + the DOCX report only (the HTML duplicated their content).

    return sorted(written)


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    if len(args) > 1:
        print("Usage: python build_reports.py [qa_dir]", file=sys.stderr)
        return 2
    qa_dir = args[0] if args else paths.QA_ROOT
    try:
        written = build_reports(qa_dir)
    except FileNotFoundError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    except ValidationError as exc:
        print(f"❌ {exc}", file=sys.stderr)
        return 1
    print(f"Built {len(written)} report file(s):")
    for path in written:
        print(f"  {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
