"""Language-neutral label sets for the report writers (no hardcoded prose in code).

The report writers hold NO project-specific text and NO single fixed language.
Every human-readable label, heading, table header, and default sentence lives
here in two sets — ``th`` and ``en`` — and the writers pick a set at render time
from the resolved report language.

The language itself is NOT fixed either: :func:`resolve_language` uses the
``language`` field the orchestrator records in ``report_meta.json`` (after it
analyses the project) and falls back to auto-detecting Thai vs. Latin script
from the actual content when the field is absent. So company, dates, prose AND
language are all per-project data — the code only carries neutral vocabulary.

Values may contain ``{name}`` placeholders filled with :meth:`str.format`.
"""

from __future__ import annotations

import re

SUPPORTED = ("th", "en")
DEFAULT_LANGUAGE = "en"

_THAI_CHAR_RE = re.compile(r"[\u0E00-\u0E7F]")
_LATIN_CHAR_RE = re.compile(r"[A-Za-z]")


def _norm_lang(value: object) -> str | None:
    text = str(value or "").strip().lower()
    if not text:
        return None
    if text in ("th", "tha", "thai", "th-th", "ภาษาไทย", "ไทย"):
        return "th"
    if text.startswith("en") or text in ("english", "eng", "อังกฤษ"):
        return "en"
    return None


def detect_language(*text_sources: object) -> str:
    """Auto-detect 'th' or 'en' from content by Thai vs. Latin letter frequency."""
    thai = latin = 0
    for source in text_sources:
        if not source:
            continue
        s = str(source)
        thai += len(_THAI_CHAR_RE.findall(s))
        latin += len(_LATIN_CHAR_RE.findall(s))
    if thai == 0:
        return "en"
    return "th" if thai * 4 >= latin else "en"


def resolve_language(meta: dict | None, *content: object) -> str:
    """Explicit meta.language wins; otherwise auto-detect from content."""
    if isinstance(meta, dict):
        explicit = _norm_lang(meta.get("language"))
        if explicit:
            return explicit
    detected = detect_language(*content)
    return detected if detected in SUPPORTED else DEFAULT_LANGUAGE


def get(lang: str) -> dict:
    """Return the label dict for ``lang`` (falls back to DEFAULT_LANGUAGE)."""
    return LABELS.get(lang, LABELS[DEFAULT_LANGUAGE])


LABELS: dict[str, dict] = {}


LABELS["en"] = {
    "report_title": "Application Testing Report",
    "sections": {
        1: "1. Introduction",
        2: "2. Test Strategy & Scope",
        3: "3. Test Environment & Tools",
        4: "4. Executive Summary",
        5: "5. Detailed Results",
        6: "6. Issues & Defects",
        7: "7. Conclusion & Recommendations",
        8: "8. Appendix",
    },
    "subs": {
        "1.1": "1.1 Purpose", "1.2": "1.2 Scope", "1.3": "1.3 Audience",
        "1.4": "1.4 Glossary", "1.5": "1.5 References",
        "2.1": "2.1 Objectives", "2.2": "2.2 Test Types Covered",
        "2.3": "2.3 Out of Scope", "2.4": "2.4 Risk & Assumptions",
        "2.5": "2.5 Pass/Fail Criteria",
        "3.1": "3.1 Hardware / Software", "3.2": "3.2 Tools Used",
        "3.3": "3.3 Test Data", "3.4": "3.4 Test Accounts",
        "3.5": "3.5 Network / API Endpoints",
        "4.1": "Test Execution Overview", "4.2": "Pass/Fail Summary by Test Type",
        "4.device": "Pass/Fail Summary by Device", "4.coverage": "Coverage",
        "4.dist": "Result Distribution", "4.findings": "Key Findings",
        "4.verdict": "Overall Verdict",
        "5.1.title": "UI End-to-End Test Results", "5.api.title": "Backend API Test Results",
        "5.unit.title": "Unit & Integration Test Results", "5.generic.title": "{group} Test Results",
        "5.methodology": "Methodology", "5.success": "Successful Cases",
        "5.failed": "Failed Cases", "5.notrun": "Cases Not Yet Executed",
        "5.evidence": "Evidence",
        "6.1": "6.1 Issue Summary", "6.2": "6.2 Severity Classification",
        "6.3": "6.3 Defect Log", "6.4": "6.4 Detailed Issue Reports",
        "7.1": "7.1 Test Coverage Achievement", "7.2": "7.2 Quality Assessment",
        "7.3": "7.3 Recommendations for Production", "7.4": "7.4 Next Steps / Future Test Cycles",
        "8.1": "8.1 Test Case ID Index", "8.2": "8.2 Glossary",
        "8.3": "8.3 Tools Configuration", "8.4": "8.4 References",
        "8.5": "8.5 Test Case Workbook", "8.6": "8.6 Test Evidence (All Test Cases)",
    },
    "doc": {
        "approval": "Document Approval",
        "revision": "Document Revision History",
        "toc": "Table of Contents",
        "lot": "List of Tables",
        "lof": "List of Figures",
        "no_entries": "— no entries —",
        "cap_label_table": "Table",
        "cap_label_figure": "Figure",
        "cap_prefix": "{label} ",
        "cap_sep": ": ",
    },
    "cover": {
        "version": "Version: {v}", "date": "Date: {d}",
        "prepared_by": "Prepared By: {a}",
        "created_updated": "Created: {c}    ·    Last Updated: {u}",
        "confidential": "CONFIDENTIAL",
    },
    "footer_confidential": "Confidential", "footer_page": "Page ",
    "approval_headers": ("Role", "Name", "Signature", "Date"),
    "approval_rows": ("Prepared By", "Reviewed By", "Approved By"),
    "revision_headers": ("Version", "Date", "Author", "Description"),
    "initial_release": "Initial release",
    "headers": {
        "exec_summary": ("Test Type", "Total", "Passed", "Failed", "Pass Rate"),
        "device_summary": ("Device", "Total", "Passed", "Failed", "Pass Rate"),
        "detail": ("TC ID", "User Story ID", "Module", "Scenario", "Severity",
                   "Risk Level", "Status", "Defect ID", "Notes"),
        "defect": ("Defect ID", "Linked TC", "Severity", "Status", "Assigned To",
                   "Root Cause", "Resolution"),
        "rtm": ("User Story ID", "Covered TC IDs", "#TC", "#Pass", "#Fail", "Coverage"),
        "kv_item_value": ("Item", "Value"),
        "tool_version": ("Tool", "Version"),
        "endpoint_value": ("Endpoint", "Value"),
        "term_definition": ("Term", "Definition"),
        "test_types": ("Test Type", "Tool", "Scope"),
        "severity_count": ("Severity", "Count"),
        "account": ("Role", "Username", "Privileges"),
        "tc_index": ("TC ID", "Scenario"),
    },
    "caps": {
        "glossary": "Glossary of terms used in this document",
        "test_types": "Test types covered",
        "hardware": "Hardware and software used for testing",
        "tools": "Tools used for testing",
        "endpoints": "Network / API endpoints",
        "exec_by_type": "Pass/Fail summary by test type",
        "exec_by_device": "Pass/Fail summary by device",
        "rtm": "Requirements Traceability Matrix (RTM)",
        "success": "Successful test cases ({group})",
        "failed": "Failed test cases ({group})",
        "notrun": "Test cases not yet executed ({group})",
        "severity": "Issues by severity",
        "defect_log": "Defect Log",
        "tc_index": "Test Case ID Index",
        "tools_config": "Tools configuration",
        "accounts": "Test accounts",
    },
    "rows": {
        "os": "Operating System", "node": "Node.js", "browser": "Browser",
        "memory": "Memory", "app_url": "Application URL", "api_base": "API Base URL",
        "framework_detected": "Detected framework",
        "test_type_ui": "Critical user journeys through the browser",
        "test_type_api": "Service-layer request/response checks",
        "test_type_unit": "Module and integration logic",
        "glossary": [
            ("UI E2E", "User-interface end-to-end test driven through the browser."),
            ("API", "Application Programming Interface test at the service layer."),
            ("TC", "Test Case — a single verifiable scenario."),
            ("Pass Rate", "Passed test cases divided by total executed test cases."),
        ],
    },
    "prose": {
        "purpose": ("This System Test Report is issued by {company} to formally document the "
                    "results of the automated test cycle conducted on {system}. It sets out "
                    "the objectives, scope, methodology, and outcomes of the testing effort — "
                    "including coverage achieved, pass and fail rates, defects identified, and "
                    "recommendations for quality improvement — and is prepared in accordance "
                    "with the quality-assurance standards adopted by {company}. The report is "
                    "intended to provide the stakeholders and governance bodies of {company} "
                    "with an authoritative, auditable basis for assessing the quality and "
                    "release-readiness of {system} prior to its promotion to the production "
                    "environment."),
        "scope": ("The scope of this test cycle is defined by the test cases actually executed "
                  "against {system} in the {environment}, comprising {layers}"
                  "{devices_clause}. This scope was agreed with the relevant stakeholders of "
                  "{company} and reflects the areas deemed material to the quality of {system} "
                  "for this release; any area not covered by these test cases was not exercised "
                  "in this cycle and is addressed under the Out of Scope section below."),
        "audience": ("This report is prepared for the stakeholders of {company} responsible for "
                     "the quality and release governance of {system}, including quality-"
                     "assurance engineers, software developers, test leads, project and "
                     "delivery managers, product owners, and the business and technical "
                     "sponsors who authorise the system's promotion to production. It is "
                     "intended to support their review, sign-off, and decision-making."),
        "objectives": ("The objectives of this test cycle are to verify that {system} behaves "
                        "correctly and completely against its specifications across every "
                        "defined test type and pass criterion; to identify and record defects "
                        "as early as possible in the delivery cycle; to assess the residual "
                        "quality risk carried by the release; and to confirm consistent "
                        "behaviour across the devices and browsers standardised by {company}. "
                        "Collectively these objectives provide {company} with the evidence "
                        "required to make an informed release decision."),
        "out_of_scope": ("The following activities are explicitly out of scope for this report: "
                         "performance and load testing, security and penetration testing, and "
                         "manual exploratory testing. This report covers only the automated "
                         "testing actually performed in this cycle ({layers}); any activity not "
                         "listed above was not carried out. Out-of-scope activities should be "
                         "planned and conducted separately under their own governance, and their "
                         "results consolidated with this report before {company} reaches a final "
                         "release decision."),
        "risks": ("This test cycle assumes that the {environment} is a faithful "
                  "representation of production for the scenarios covered, that the test data "
                  "provisioned is valid and sufficient, and that all dependent external "
                  "services remain available throughout execution. The principal risk to the "
                  "validity of these results is any divergence in configuration or data "
                  "between the test and production environments; accordingly, {company} is "
                  "advised to perform confirmatory re-verification in production prior to "
                  "final delivery and go-live."),
        "pass_fail": ("A test case is recorded as Passed when the observed result matches the "
                      "expected result in full, and as Failed otherwise. Cases marked Blocked "
                      "could not be executed owing to a blocking constraint or defect, while "
                      "cases marked Skipped are reported for completeness but excluded from the "
                      "pass/fail calculation. In line with the acceptance criteria adopted by "
                      "{company}, the overall release verdict is determined from the pass rate "
                      "together with the severity of any outstanding defects."),
        "test_data": ("Test data was provisioned for each scenario to exercise {system} across "
                      "the full range of conditions, covering both valid (positive) and invalid "
                      "(negative) inputs. In accordance with the information-security and "
                      "personal-data protection policies of {company}, sensitive credentials "
                      "such as passwords and authentication tokens are never stored or disclosed "
                      "in this report, and representative mock data is used in place of "
                      "production data throughout."),
        "no_accounts": "No dedicated test accounts were defined for this run.",
        "method_ui": ("User-interface tests were executed with {tool}, simulating real "
                      "end-user journeys{devices_clause} to verify responsive rendering, with a "
                      "screenshot and a video captured for every test case as supporting "
                      "evidence."),
        "method_api": ("API-layer tests exercised each endpoint over HTTP, asserting status "
                       "codes, the response envelope and content, and error codes per each "
                       "API specification, while recording latency to assess service "
                       "correctness and reliability."),
        "method_unit": ("Unit and integration tests were exercised through the project's own "
                        "test runner, verifying module-level logic and cross-module "
                        "integration to confirm correctness at the code level before "
                        "higher-level testing."),
        "method_generic": ("{group} test cases were executed sequentially, with supporting "
                           "evidence captured for each case."),
        "device_note": ("Each UI test case was executed on every device viewport shown below; "
                        "the counts reflect that device's own result, demonstrating "
                        "responsive consistency across devices."),
        "rtm_intro": ("The Requirements Traceability Matrix (RTM) below links each requirement "
                      "(user story) to the test cases that cover it:"),
        "rtm_none": "No requirement-to-test-case traceability data is available yet.",
        "result_dist": ("The distribution of results can be seen in the pass/fail summary "
                        "tables above, broken down by test type and device."),
        "findings_fail": "{n} failing test case(s) were identified across all test types.",
        "findings_none": "No failing test cases were identified in this run.",
        "exec_overview": ("This report documents the execution of {total} test case(s) for "
                          "{system}. Of these, {passed} passed, {failed} failed, and {notrun} "
                          "were not executed, giving an overall pass rate of {rate}."),
        "verdict_pass": ("All {executed} executed test case(s) passed ({rate}) and no defects "
                         "were logged. Within the tested scope, {system} shows no blocking "
                         "issues and is considered ready for release of the covered functionality."),
        "verdict_fail": ("{failed} of {total} test case(s) failed ({rate} pass rate). {system} "
                         "should not be released for the affected functionality until the defects "
                         "listed in Section 6 are resolved and re-tested."),
        "verdict_notrun": ("No test cases have been executed yet, so no execution verdict can be "
                           "given; this document reflects the planned scope only."),
        "next_pass": ("No defects were found in this cycle. {company} should keep this suite as "
                      "the standing regression set and re-run it against future changes to "
                      "{system}, and consider extending coverage to the currently out-of-scope "
                      "areas (performance, security, accessibility)."),
        "detail_none": "No detailed results: the suite contains no test cases.",
        "success_none": "No passing test cases in this group.",
        "failed_none": "No failing test cases in this group.",
        "evidence_ui": ('UI evidence consists of screenshots captured automatically during '
                        'execution on each device viewport (Desktop, Tablet, Mobile). The '
                        'complete set of screenshots for every test case is collected in the '
                        'Appendix, section "Test Evidence (All Test Cases)". The file paths of '
                        'each capture are referenced below:'),
        "evidence_ui_none": "{id} — {scenario}: no evidence image for this case",
        "evidence_ref": "{id} [{device} · {status}] — {path}",
        "evidence_api": ('API-layer evidence consists of captured request/response logs, HTTP '
                         'status codes, data structures, and latency values recorded alongside '
                         'the results and referenced in the companion workbook "test_cases.xlsx"; '
                         'API testing has no screenshots by nature.'),
        "device_default": "default",
        "severity_class": ("The severity of a defect is taken first from the explicit severity "
                           "recorded on each failing or blocked test case; when unspecified it "
                           "is inferred from the case priority, across four levels — Critical, "
                           "High, Medium, and Low — defaulting to Medium when no value is given."),
        "severity_summary_none": ("Classification result: no defects were logged in this run — "
                                  "every executed test case passed, so there are no Critical, "
                                  "High, Medium, or Low severity issues to classify. The system "
                                  "shows no blocking defects at the tested scope, and no fixes "
                                  "are required before release on the basis of this run."),
        "severity_summary": ("Classification result: {total} defect(s) were logged over {executed} "
                             "executed case(s) — Critical {crit}, High {high}, Medium {med}, "
                             "Low {low}. Of these, {blocking} are Critical/High and are treated "
                             "as release-blocking: they must be fixed and re-tested before the "
                             "system goes live. The remaining {nonblocking} Medium/Low issue(s) "
                             "should be scheduled and resolved in order of severity. {verdict}"),
        "severity_verdict_block": ("Overall, the current defect profile indicates the release "
                                   "should be held until the blocking issues are resolved."),
        "severity_verdict_ok": ("Overall, no release-blocking (Critical/High) defects were found, "
                                "so the remaining issues can be addressed without blocking release."),
        "defect_none": "No defects were recorded in this run.",
        "quality_exec": ("Based on this test cycle, {system} achieved an overall pass rate of "
                         "{overall} ({passed} of {total} test cases passed). Considering only "
                         "the {executed} case(s) actually executed, the effective pass rate is "
                         "{exec_rate}, which {company} rates as \"{level}\". Defects are "
                         "distributed by severity as follows: Critical {crit}, High {high}, "
                         "Medium {med}, and Low {low}. This assessment forms the basis of "
                         "{company}'s quality judgement for the release."),
        "quality_noexec": ("No test cases were executed (neither passed nor failed) in this "
                           "cycle, so {company} rates the quality of {system} as \"{level}\" "
                           "pending execution. Total test cases prepared: {total}. Defects by "
                           "severity: Critical {crit}, High {high}, Medium {med}, and Low {low}."),
        "recommend_default": ("{company} recommends that the failed test cases listed in "
                              "Section 6 be resolved before {system} is promoted to production, "
                              "prioritised by defect severity, with each fix verified through "
                              "re-testing. Test coverage should be extended to the currently "
                              "out-of-scope areas as appropriate, and the outcomes reviewed "
                              "with the relevant stakeholders as part of the release "
                              "governance process."),
        "recommend_pass": ("All executed test cases for {system} passed and no defects were "
                           "logged in this cycle, so {company} finds no blocking issue for the "
                           "tested scope. {company} recommends proceeding with the release for "
                           "the covered functionality, while keeping this suite as the standing "
                           "regression set and extending coverage to the currently out-of-scope "
                           "areas (performance, security, accessibility) before or shortly after "
                           "go-live, as part of the release governance process."),
        "next_default": ("Once the identified defects have been remediated, {company} should "
                         "re-execute the same suite (regression testing) and compare the "
                         "results against this cycle to confirm that the defects are resolved "
                         "without introducing regressions elsewhere. The defect-detecting "
                         "cases should be incorporated into {company}'s standing regression "
                         "set to guard against recurrence in future development cycles."),
        "appendix_glossary": "See section 1.4 for the glossary of terms used throughout this document.",
        "appendix_workbook": ('The full test-case details, the Requirements Traceability Matrix '
                             '(RTM), and the defect log are maintained in the companion workbook '
                             '"test_cases.xlsx" located in the same folder as this report.'),
        "appendix_evidence_intro": ("This appendix collects the complete screenshot evidence for "
                                    "every test case, ordered by test-case ID and grouped by the "
                                    "device under test, each with a caption beneath, for full "
                                    "traceability."),
        "appendix_evidence_none": ("This test case has no screenshots (e.g., API/service-layer "
                                   "testing); reference evidence is stored in the companion "
                                   'workbook "test_cases.xlsx". Result: {status}'),
        "appendix_evidence_missing": "[evidence image not found] {id} [{device}] — reference: {path}",
        "no_test_cases": "No test cases.",
    },
    "quality_levels": {
        "not_assessed": "Not Assessed", "excellent": "Excellent", "good": "Good",
        "acceptable": "Acceptable", "needs": "Needs Improvement",
    },
    "cover_date_start": "Start date: {d}",
    "cover_date_end": "End date: {d}",
    "cover_total": "Total test cases: {n}",
}


LABELS["th"] = {
    "report_title": "Application Testing Report",
    "sections": {
        1: "1. บทนำ (Introduction)",
        2: "2. กลยุทธ์และขอบเขตการทดสอบ (Test Strategy & Scope)",
        3: "3. สภาพแวดล้อมและเครื่องมือ (Test Environment & Tools)",
        4: "4. สรุปผลรวม (Executive Summary)",
        5: "5. ผลการทดสอบรายละเอียด (Detailed Results)",
        6: "6. ปัญหาและข้อบกพร่อง (Issues & Defects)",
        7: "7. สรุปและข้อเสนอแนะ (Conclusion & Recommendations)",
        8: "8. ภาคผนวก (Appendix)",
    },
    "subs": {
        "1.1": "1.1 Purpose / วัตถุประสงค์", "1.2": "1.2 Scope / ขอบเขต",
        "1.3": "1.3 Audience / กลุ่มเป้าหมาย", "1.4": "1.4 Glossary / อภิธานศัพท์",
        "1.5": "1.5 References / เอกสารอ้างอิง",
        "2.1": "2.1 Objectives / วัตถุประสงค์การทดสอบ",
        "2.2": "2.2 Test Types Covered / ประเภทการทดสอบ",
        "2.3": "2.3 Out of Scope / นอกขอบเขต",
        "2.4": "2.4 Risk & Assumptions / ความเสี่ยงและข้อสมมติ",
        "2.5": "2.5 Pass/Fail Criteria / เกณฑ์ผ่าน/ไม่ผ่าน",
        "3.1": "3.1 Hardware / Software / ฮาร์ดแวร์และซอฟต์แวร์",
        "3.2": "3.2 Tools Used / เครื่องมือที่ใช้",
        "3.3": "3.3 Test Data / ข้อมูลทดสอบ",
        "3.4": "3.4 Test Accounts / บัญชีทดสอบ",
        "3.5": "3.5 Network/API Endpoints / เครือข่ายและ API Endpoints",
        "4.1": "Test Execution Overview / ภาพรวมการดำเนินการทดสอบ",
        "4.2": "Pass/Fail Summary by Test Type / สรุปผลตามประเภท",
        "4.device": "Pass/Fail Summary by Device / สรุปผลตามอุปกรณ์",
        "4.coverage": "Coverage / ความครอบคลุม",
        "4.dist": "Result Distribution / การกระจายผล",
        "4.findings": "Key Findings / ข้อค้นพบสำคัญ",
        "4.verdict": "Overall Verdict / บทสรุปผลรวม",
        "5.1.title": "UI End-to-End Test Results / ผลการทดสอบ UI End-to-End",
        "5.api.title": "Backend API Test Results / ผลการทดสอบ Backend API",
        "5.unit.title": "Unit & Integration Test Results / ผลการทดสอบ Unit & Integration",
        "5.generic.title": "{group} Test Results",
        "5.methodology": "Methodology / วิธีการทดสอบ",
        "5.success": "Successful Cases / กรณีที่ผ่าน",
        "5.failed": "Failed Cases / กรณีที่ไม่ผ่าน",
        "5.notrun": "Cases Not Yet Executed / กรณีที่ยังไม่ได้ดำเนินการ",
        "5.evidence": "Evidence / หลักฐาน",
        "6.1": "6.1 Issue Summary / สรุปข้อบกพร่อง",
        "6.2": "6.2 Severity Classification / การจัดระดับความรุนแรง",
        "6.3": "6.3 Defect Log / ทะเบียนข้อบกพร่อง",
        "6.4": "6.4 Detailed Issue Reports / รายงานข้อบกพร่องโดยละเอียด",
        "7.1": "7.1 Test Coverage Achievement / ผลความครอบคลุมการทดสอบ",
        "7.2": "7.2 Quality Assessment / การประเมินคุณภาพ",
        "7.3": "7.3 Recommendations for Production / ข้อเสนอแนะสำหรับ Production",
        "7.4": "7.4 Next Steps / Future Test Cycles / ขั้นตอนถัดไป",
        "8.1": "8.1 Test Case ID Index / ดัชนีรหัสกรณีทดสอบ",
        "8.2": "8.2 Glossary / อภิธานศัพท์",
        "8.3": "8.3 Tools Configuration / การตั้งค่าเครื่องมือ",
        "8.4": "8.4 References / เอกสารอ้างอิง",
        "8.5": "8.5 Test Case Workbook / ไฟล์กรณีทดสอบ",
        "8.6": "8.6 หลักฐานการทดสอบทุก testcase / Test Evidence (All Test Cases)",
    },
    "doc": {
        "approval": "Document Approval / การอนุมัติเอกสาร",
        "revision": "Document Revision History / ประวัติการแก้ไขเอกสาร",
        "toc": "สารบัญ / Table of Contents",
        "lot": "สารบัญตาราง / List of Tables",
        "lof": "สารบัญรูปภาพ / List of Figures",
        "no_entries": "— ไม่มีรายการ / no entries —",
        "cap_label_table": "ตาราง",
        "cap_label_figure": "รูป",
        "cap_prefix": "{label}ที่ ",
        "cap_sep": " : ",
    },
    "cover": {
        "version": "เวอร์ชัน (Version): {v}", "date": "วันที่ทดสอบ (Date): {d}",
        "prepared_by": "จัดทำโดย (Prepared By): {a}",
        "created_updated": "สร้างเมื่อ (Created): {c}    ·    ปรับปรุงล่าสุด (Last Updated): {u}",
        "confidential": "ลับ (CONFIDENTIAL)",
    },
    "footer_confidential": "ลับ (Confidential)", "footer_page": "หน้า ",
    "approval_headers": ("บทบาท", "ชื่อ-นามสกุล", "ลายมือชื่อ", "วันที่"),
    "approval_rows": ("ผู้จัดทำ", "ผู้ตรวจทาน", "ผู้อนุมัติ"),
    "revision_headers": ("เวอร์ชัน", "วันที่", "ผู้จัดทำ", "รายละเอียดการแก้ไข"),
    "initial_release": "จัดทำและเผยแพร่ฉบับแรก",
    "headers": {
        "exec_summary": ("ประเภทการทดสอบ", "ทั้งหมด", "ผ่าน", "ไม่ผ่าน", "อัตราการผ่าน"),
        "device_summary": ("อุปกรณ์", "ทั้งหมด", "ผ่าน", "ไม่ผ่าน", "อัตราการผ่าน"),
        "detail": ("รหัสกรณีทดสอบ", "รหัสความต้องการ", "โมดูล", "สถานการณ์ทดสอบ",
                   "ความรุนแรง", "ระดับความเสี่ยง", "สถานะ", "รหัสข้อบกพร่อง", "หมายเหตุ"),
        "defect": ("รหัสข้อบกพร่อง", "กรณีทดสอบที่เกี่ยวข้อง", "ความรุนแรง", "สถานะ",
                   "ผู้รับผิดชอบ", "สาเหตุ", "การแก้ไข"),
        "rtm": ("รหัสความต้องการ (User Story)", "รหัสกรณีทดสอบที่ครอบคลุม", "จำนวน TC",
                "จำนวนผ่าน", "จำนวนไม่ผ่าน", "ความครอบคลุม"),
        "kv_item_value": ("รายการ", "ค่า"),
        "tool_version": ("เครื่องมือ", "เวอร์ชัน"),
        "endpoint_value": ("จุดเชื่อมต่อ (Endpoint)", "ค่า"),
        "term_definition": ("คำศัพท์", "คำอธิบาย"),
        "test_types": ("ประเภทการทดสอบ", "เครื่องมือ", "ขอบเขต"),
        "severity_count": ("ระดับความรุนแรง", "จำนวน"),
        "account": ("บทบาท", "ชื่อผู้ใช้", "สิทธิ์การใช้งาน"),
        "tc_index": ("รหัสกรณีทดสอบ", "สถานการณ์ทดสอบ"),
    },
    "caps": {
        "glossary": "คำศัพท์ที่ใช้ในเอกสาร",
        "test_types": "ประเภทการทดสอบที่ครอบคลุม",
        "hardware": "ฮาร์ดแวร์และซอฟต์แวร์ที่ใช้ในการทดสอบ",
        "tools": "เครื่องมือที่ใช้ในการทดสอบ",
        "endpoints": "เครือข่ายและ API Endpoints",
        "exec_by_type": "สรุปผล Pass/Fail แยกตาม Test Type",
        "exec_by_device": "สรุปผล Pass/Fail แยกตามอุปกรณ์",
        "rtm": "ตารางสอบทานความต้องการ (Requirements Traceability Matrix)",
        "success": "ผลการทดสอบกรณีที่ผ่าน ({group})",
        "failed": "ผลการทดสอบกรณีที่ไม่ผ่าน ({group})",
        "notrun": "กรณีทดสอบที่ยังไม่ได้ดำเนินการ ({group})",
        "severity": "สรุปข้อบกพร่องจำแนกตามระดับความรุนแรง",
        "defect_log": "ทะเบียนข้อบกพร่อง (Defect Log)",
        "tc_index": "ดัชนีรหัสกรณีทดสอบ",
        "tools_config": "การตั้งค่าเครื่องมือที่ใช้ในการทดสอบ",
        "accounts": "บัญชีผู้ใช้สำหรับการทดสอบ",
    },
    "rows": {
        "os": "ระบบปฏิบัติการ (Operating System)", "node": "Node.js",
        "browser": "เบราว์เซอร์ (Browser)", "memory": "หน่วยความจำ (Memory)",
        "app_url": "ที่อยู่แอปพลิเคชัน (Application URL)",
        "api_base": "ที่อยู่หลักของบริการ (API Base URL)",
        "framework_detected": "เฟรมเวิร์กที่ตรวจพบ",
        "test_type_ui": "เส้นทางการใช้งานสำคัญของผู้ใช้ผ่านเบราว์เซอร์",
        "test_type_api": "การตรวจสอบคำขอและการตอบกลับในระดับบริการ",
        "test_type_unit": "ตรรกะระดับหน่วยและการผสานรวมระหว่างโมดูล",
        "glossary": [
            ("UI E2E", "การทดสอบส่วนติดต่อผู้ใช้แบบครบวงจร (End-to-End) ผ่านเบราว์เซอร์"),
            ("API", "การทดสอบส่วนต่อประสานโปรแกรมประยุกต์ในระดับบริการ (Service Layer)"),
            ("TC", "กรณีทดสอบ (Test Case) หนึ่งสถานการณ์ที่สามารถตรวจสอบผลได้"),
            ("Pass Rate", "อัตราการผ่าน คือจำนวนกรณีที่ผ่านหารด้วยจำนวนกรณีที่ดำเนินการทั้งหมด"),
        ],
    },
    "prose": {
        "purpose": ("เอกสารรายงานผลการทดสอบระบบฉบับนี้จัดทำขึ้นโดย {company} เพื่อรายงานผลการดำเนินการ "
                    "ทดสอบระบบ {system} อย่างเป็นทางการ โดยครอบคลุมวัตถุประสงค์ ขอบเขต วิธีการ และผลลัพธ์ "
                    "ของการทดสอบแบบอัตโนมัติทั้งหมด รวมถึงความครอบคลุมของกรณีทดสอบ อัตราการผ่านและไม่ผ่าน "
                    "ข้อบกพร่อง (Defect) ที่ตรวจพบ ตลอดจนข้อเสนอแนะเพื่อการปรับปรุงคุณภาพ ซึ่งจัดทำขึ้นตาม "
                    "มาตรฐานการประกันคุณภาพที่ {company} ถือปฏิบัติ ทั้งนี้เพื่อให้ผู้เกี่ยวข้องและ "
                    "คณะทำงานของ {company} สามารถใช้เป็นหลักฐานอ้างอิงที่ตรวจสอบย้อนกลับได้ "
                    "ประกอบการพิจารณาคุณภาพและความพร้อมของระบบ {system} ก่อนนำขึ้นใช้งานจริงบนสภาพแวดล้อม "
                    "Production"),
        "scope": ("ขอบเขตของการทดสอบในรอบนี้กำหนดจากกรณีทดสอบที่ดำเนินการจริงกับระบบ {system} บน "
                  "{environment} ประกอบด้วย {layers}{devices_clause} ทั้งนี้ขอบเขตดังกล่าวได้ผ่าน "
                  "การตกลงร่วมกับผู้เกี่ยวข้องของ {company} และสะท้อนส่วนงานที่มีนัยสำคัญต่อ "
                  "คุณภาพของระบบ {system} สำหรับการส่งมอบรอบนี้ ส่วนงานใดที่ไม่ได้ครอบคลุมด้วยกรณีทดสอบเหล่านี้ "
                  "ถือว่าไม่ได้ดำเนินการทดสอบในรอบนี้ และได้ระบุไว้ในหัวข้อนอกขอบเขต (Out of Scope) ด้านล่าง"),
        "audience": ("เอกสารฉบับนี้จัดทำขึ้นสำหรับผู้เกี่ยวข้องของ {company} ที่รับผิดชอบด้านคุณภาพและการ "
                     "กำกับดูแลการส่งมอบระบบ {system} อันประกอบด้วยวิศวกรประกันคุณภาพ (QA Engineer) นักพัฒนา "
                     "ซอฟต์แวร์ (Developer) หัวหน้าทีมทดสอบ ผู้จัดการโครงการและผู้จัดการการส่งมอบ ผู้ดูแลผลิตภัณฑ์ "
                     "(Product Owner) ตลอดจนผู้สนับสนุนทางธุรกิจและทางเทคนิคที่มีอำนาจอนุมัติการนำระบบขึ้นใช้งานจริง "
                     "เพื่อใช้ประกอบการพิจารณา การลงนามรับรอง และการตัดสินใจส่งมอบระบบ"),
        "objectives": ("การทดสอบในรอบนี้มีวัตถุประสงค์เพื่อยืนยันว่าระบบ {system} ทำงานได้อย่างถูกต้องครบถ้วนตาม "
                       "ข้อกำหนดที่ระบุไว้ ครอบคลุมทุกประเภทการทดสอบและเกณฑ์การผ่านที่กำหนด เพื่อค้นหาและบันทึก "
                       "ข้อบกพร่อง (Defect) ตั้งแต่ระยะเริ่มต้นของวงจรการส่งมอบ เพื่อประเมินความเสี่ยงด้านคุณภาพที่ยัง "
                       "คงเหลืออยู่ของการส่งมอบ และเพื่อยืนยันความสอดคล้องของการทำงานข้ามอุปกรณ์และเบราว์เซอร์ที่ "
                       "{company} กำหนดเป็นมาตรฐาน ทั้งนี้เพื่อให้ {company} มีหลักฐานเพียงพอต่อการตัดสินใจส่งมอบ "
                       "อย่างมีข้อมูลรองรับ"),
        "out_of_scope": ("กิจกรรมต่อไปนี้อยู่นอกขอบเขตของรายงานฉบับนี้โดยชัดแจ้ง: การทดสอบสมรรถนะและการรับภาระ "
                         "(Performance / Load Testing) การทดสอบความมั่นคงปลอดภัยและการเจาะระบบ "
                         "(Security / Penetration Testing) และการทดสอบเชิงสำรวจด้วยตนเอง (Manual Exploratory "
                         "Testing) รายงานฉบับนี้ครอบคลุมเฉพาะการทดสอบแบบอัตโนมัติที่ได้ดำเนินการจริงในรอบนี้เท่านั้น "
                         "({layers}) กิจกรรมใดที่ไม่ได้ระบุไว้ข้างต้นถือว่ายังไม่ได้ดำเนินการ ทั้งนี้กิจกรรมที่อยู่นอก "
                         "ขอบเขตควรวางแผนและดำเนินการแยกต่างหากภายใต้การกำกับดูแลเฉพาะด้าน และควรนำผลมาประกอบ "
                         "กับรายงานฉบับนี้ก่อนที่ {company} จะพิจารณาตัดสินใจส่งมอบขั้นสุดท้าย"),
        "risks": ("การทดสอบตั้งอยู่บนข้อสมมติว่า {environment} เป็นตัวแทนที่ใกล้เคียงกับการใช้งานจริง "
                  "สำหรับกรณีทดสอบที่ครอบคลุม ข้อมูลทดสอบที่จัดเตรียมมีความถูกต้องและเพียงพอ และบริการภายนอกที่ "
                  "เกี่ยวข้องพร้อมให้บริการตลอดช่วงการดำเนินการ ความเสี่ยงสำคัญที่สุดต่อความสมบูรณ์ของผลการทดสอบ "
                  "ได้แก่ ความแตกต่างของการตั้งค่าหรือข้อมูลระหว่างสภาพแวดล้อมทดสอบกับการใช้งานจริง ด้วยเหตุนี้ "
                  "{company} จึงควรดำเนินการตรวจสอบยืนยันซ้ำบนสภาพแวดล้อมจริงก่อนการส่งมอบและการนำขึ้นใช้งานจริง"),
        "pass_fail": ("กรณีทดสอบจะถือว่า 'ผ่าน' (Pass) เมื่อผลลัพธ์ที่เกิดขึ้นจริงตรงกับผลลัพธ์ที่คาดหวังทุกประการ "
                      "และถือว่า 'ไม่ผ่าน' (Fail) เมื่อผลลัพธ์จริงไม่เป็นไปตามที่คาดหวังหรือเกิดข้อผิดพลาดระหว่างการ "
                      "ดำเนินการ กรณีที่ถูกระงับ (Blocked) หมายถึงกรณีที่ไม่สามารถดำเนินการได้เนื่องจากข้อจำกัดหรือ "
                      "ข้อบกพร่องที่ขัดขวาง ส่วนกรณีที่ข้าม (Skipped) จะถูกรายงานไว้เพื่อความครบถ้วนแต่ไม่นำมารวมใน "
                      "การคำนวณอัตราการผ่านและไม่ผ่าน ทั้งนี้ตามเกณฑ์การยอมรับที่ {company} ถือปฏิบัติ ผลสรุปการ "
                      "ยอมรับโดยรวมจะพิจารณาจากอัตราการผ่านของกรณีทดสอบประกอบกับระดับความรุนแรงของข้อบกพร่องที่ "
                      "ยังคงค้างอยู่"),
        "test_data": ("ข้อมูลที่ใช้ในการทดสอบถูกจัดเตรียมเฉพาะสำหรับแต่ละกรณีทดสอบ เพื่อทดสอบระบบ {system} ให้ "
                      "ครอบคลุมทุกสถานการณ์ ทั้งข้อมูลที่ถูกต้อง (Positive) และข้อมูลที่ไม่ถูกต้อง (Negative) ทั้งนี้ตาม "
                      "นโยบายด้านความมั่นคงปลอดภัยสารสนเทศและการคุ้มครองข้อมูลส่วนบุคคลของ {company} ข้อมูลที่มี "
                      "ความอ่อนไหว เช่น รหัสผ่านหรือโทเคนการยืนยันตัวตน (Token) จะไม่ถูกจัดเก็บหรือเปิดเผยในรายงาน "
                      "ฉบับนี้ และมีการใช้ข้อมูลจำลองที่เป็นตัวแทนแทนข้อมูลจริงตลอดการดำเนินการ"),
        "no_accounts": "ไม่มีการกำหนดบัญชีผู้ใช้เฉพาะสำหรับการทดสอบชุดนี้",
        "method_ui": ("การทดสอบส่วนติดต่อผู้ใช้ดำเนินการด้วย {tool} โดยจำลองการใช้งานจริงของผู้ใช้ตั้งแต่ต้นจนจบใน "
                      "แต่ละเส้นทางการทำงานหลัก{devices_clause} เพื่อตรวจสอบความถูกต้องของการแสดงผลแบบ Responsive "
                      "พร้อมบันทึกภาพหน้าจอและวิดีโอของทุกกรณีทดสอบไว้เป็นหลักฐานประกอบ"),
        "method_api": ("การทดสอบระดับบริการดำเนินการโดยส่งคำขอ (Request) ไปยังแต่ละจุดเชื่อมต่อ (Endpoint) ผ่าน "
                       "โปรโตคอล HTTP และตรวจสอบรหัสสถานะการตอบกลับ (Status Code) โครงสร้างและเนื้อหาของข้อมูล "
                       "ตอบกลับ (Response) รวมถึงรหัสข้อผิดพลาดตามข้อกำหนดของ API แต่ละรายการ พร้อมบันทึกค่าเวลาแฝง "
                       "(Latency) เพื่อประเมินความถูกต้องและความน่าเชื่อถือของบริการ"),
        "method_unit": ("การทดสอบระดับหน่วยและการผสานรวมดำเนินการผ่านชุดทดสอบของโครงการ โดยตรวจสอบตรรกะการทำงาน "
                        "ของแต่ละโมดูลและการทำงานร่วมกันระหว่างโมดูล เพื่อยืนยันความถูกต้องในระดับโค้ดก่อนการทดสอบ "
                        "ในระดับที่สูงขึ้น"),
        "method_generic": ("กรณีทดสอบประเภท {group} ดำเนินการตามลำดับ พร้อมบันทึกหลักฐานประกอบของแต่ละกรณีอย่างครบถ้วน"),
        "device_note": ("กรณีทดสอบส่วนติดต่อผู้ใช้ (UI) แต่ละรายการได้รับการดำเนินการซ้ำบนอุปกรณ์แต่ละขนาดหน้าจอ "
                        "ตามที่แสดงในตารางด้านล่าง โดยจำนวนที่แสดงคือผลการทดสอบเฉพาะของอุปกรณ์นั้น ๆ เพื่อให้เห็น "
                        "ความสอดคล้องของการทำงานแบบ Responsive ในแต่ละอุปกรณ์"),
        "rtm_intro": ("ตารางสอบทานความต้องการ (Requirements Traceability Matrix — RTM) แสดงการเชื่อมโยงระหว่าง "
                      "ความต้องการ (User Story) กับกรณีทดสอบที่ครอบคลุม ดังนี้:"),
        "rtm_none": "ยังไม่มีข้อมูลการเชื่อมโยงความต้องการกับกรณีทดสอบ",
        "result_dist": ("การกระจายของผลการทดสอบสามารถพิจารณาได้จากตารางสรุปผลการผ่านและไม่ผ่านข้างต้น ซึ่งจำแนก "
                        "ตามประเภทการทดสอบและอุปกรณ์"),
        "findings_fail": "ตรวจพบกรณีทดสอบที่ไม่ผ่านจำนวน {n} กรณี จากการทดสอบทุกประเภท",
        "findings_none": "ไม่พบกรณีทดสอบที่ไม่ผ่านในการทดสอบครั้งนี้",
        "exec_overview": ("รายงานฉบับนี้บันทึกการดำเนินการทดสอบระบบ {system} จำนวน {total} กรณี "
                          "โดยผ่าน {passed} กรณี ไม่ผ่าน {failed} กรณี และยังไม่ได้ดำเนินการ {notrun} กรณี "
                          "คิดเป็นอัตราการผ่านโดยรวม {rate}"),
        "verdict_pass": ("กรณีทดสอบที่ดำเนินการทั้งหมด {executed} กรณีผ่านครบ ({rate}) และไม่พบข้อบกพร่อง "
                         "ภายในขอบเขตที่ทดสอบ ระบบ {system} ไม่พบประเด็นที่เป็นอุปสรรค จึงถือว่าพร้อมสำหรับ "
                         "การนำฟังก์ชันที่ครอบคลุมขึ้นใช้งาน"),
        "verdict_fail": ("มีกรณีทดสอบไม่ผ่าน {failed} จาก {total} กรณี (อัตราการผ่าน {rate}) จึงไม่ควรนำ "
                         "ระบบ {system} ส่วนที่เกี่ยวข้องขึ้นใช้งาน จนกว่าจะแก้ไขข้อบกพร่องตามหัวข้อที่ 6 "
                         "และตรวจสอบยืนยันซ้ำแล้ว"),
        "verdict_notrun": ("ยังไม่มีการดำเนินการทดสอบ จึงยังไม่สามารถสรุปผลได้ เอกสารฉบับนี้แสดงเฉพาะ "
                           "ขอบเขตที่วางแผนไว้เท่านั้น"),
        "next_pass": ("การทดสอบรอบนี้ไม่พบข้อบกพร่อง {company} ควรคงชุดทดสอบนี้ไว้เป็นชุดทดสอบถดถอย "
                      "(Regression) ประจำ และรันซ้ำเมื่อมีการเปลี่ยนแปลงระบบ {system} ในอนาคต พร้อมทั้ง "
                      "พิจารณาขยายความครอบคลุมไปยังส่วนที่อยู่นอกขอบเขตเดิม (สมรรถนะ ความมั่นคงปลอดภัย การเข้าถึง)"),
        "detail_none": "ไม่มีผลการทดสอบโดยละเอียด เนื่องจากชุดการทดสอบไม่มีกรณีทดสอบ",
        "success_none": "ไม่มีกรณีทดสอบที่ผ่านในกลุ่มนี้",
        "failed_none": "ไม่มีกรณีทดสอบที่ไม่ผ่านในกลุ่มนี้",
        "evidence_ui": ('หลักฐานการทดสอบส่วนติดต่อผู้ใช้ (UI) ประกอบด้วยภาพหน้าจอที่บันทึกโดยอัตโนมัติระหว่างการ '
                        'ดำเนินการทดสอบบนอุปกรณ์แต่ละขนาดหน้าจอ (เดสก์ท็อป แท็บเล็ต และมือถือ) โดยภาพหลักฐานทั้งหมด '
                        'ของทุกกรณีทดสอบได้รวบรวมไว้ในภาคผนวก หัวข้อ "หลักฐานการทดสอบทุก testcase" ทั้งนี้รายการ '
                        'อ้างอิงตำแหน่งไฟล์ภาพของแต่ละกรณีมีดังต่อไปนี้:'),
        "evidence_ui_none": "{id} — {scenario}: ไม่มีไฟล์ภาพหลักฐานสำหรับกรณีนี้",
        "evidence_ref": "{id} [{device} · {status}] — {path}",
        "evidence_api": ('หลักฐานการทดสอบระดับบริการ (API) จัดเก็บในรูปแบบของบันทึกคำขอและการตอบกลับ '
                         '(Request/Response) รหัสสถานะ HTTP โครงสร้างข้อมูล และค่าเวลาแฝง (Latency) ซึ่งบันทึกไว้ '
                         'พร้อมกับผลการทดสอบและอ้างอิงรายละเอียดในไฟล์เวิร์กบุ๊กประกอบ "test_cases.xlsx" โดยลักษณะ '
                         'งานของการทดสอบระดับบริการจะไม่มีภาพหน้าจอประกอบ'),
        "device_default": "ค่าเริ่มต้น",
        "severity_class": ("ระดับความรุนแรงของข้อบกพร่องพิจารณาจากค่าความรุนแรง (Severity) ที่ระบุไว้อย่างชัดเจนในแต่ละ "
                           "กรณีทดสอบที่ไม่ผ่านหรือถูกระงับเป็นลำดับแรก หากไม่ได้ระบุไว้ จะอนุมานจากระดับความสำคัญ "
                           "(Priority) ของกรณีทดสอบนั้น โดยแบ่งเป็นสี่ระดับ ได้แก่ วิกฤต (Critical) สูง (High) ปานกลาง "
                           "(Medium) และต่ำ (Low) ทั้งนี้กำหนดค่าเริ่มต้นเป็นระดับปานกลาง (Medium) เมื่อไม่มีการระบุค่าใด ๆ"),
        "severity_summary_none": ("ผลการจัดระดับ: การทดสอบครั้งนี้ไม่พบข้อบกพร่องที่ต้องบันทึก กรณีทดสอบที่ดำเนินการทั้งหมด "
                                  "ผ่านทุกกรณี จึงไม่มีประเด็นระดับวิกฤต (Critical) สูง (High) ปานกลาง (Medium) หรือต่ำ (Low) "
                                  "ที่ต้องจัดระดับ ระบบไม่พบข้อบกพร่องที่เป็นอุปสรรคต่อการใช้งานในขอบเขตที่ทดสอบ และไม่จำเป็นต้อง "
                                  "แก้ไขก่อนนำระบบขึ้นใช้งานจริงจากผลการทดสอบครั้งนี้"),
        "severity_summary": ("ผลการจัดระดับ: พบข้อบกพร่องจำนวน {total} รายการ จากกรณีที่ดำเนินการจริง {executed} กรณี "
                             "จำแนกเป็น วิกฤต (Critical) {crit} สูง (High) {high} ปานกลาง (Medium) {med} ต่ำ (Low) {low} "
                             "ในจำนวนนี้เป็นระดับวิกฤต/สูงจำนวน {blocking} รายการ ซึ่งถือเป็นอุปสรรคต่อการนำขึ้นใช้งาน "
                             "(Release-blocking) ต้องได้รับการแก้ไขและตรวจสอบซ้ำก่อนนำระบบขึ้นใช้งานจริง ส่วนที่เหลือระดับ "
                             "ปานกลาง/ต่ำจำนวน {nonblocking} รายการ ควรวางแผนแก้ไขตามลำดับความรุนแรง {verdict}"),
        "severity_verdict_block": ("โดยสรุป จากลักษณะข้อบกพร่องที่พบ ควรระงับการนำระบบขึ้นใช้งานจริงไว้ก่อนจนกว่าจะแก้ไข "
                                   "ประเด็นที่เป็นอุปสรรคทั้งหมดแล้วเสร็จ"),
        "severity_verdict_ok": ("โดยสรุป ไม่พบข้อบกพร่องระดับวิกฤต/สูงที่เป็นอุปสรรคต่อการนำขึ้นใช้งาน จึงสามารถดำเนินการ "
                                "แก้ไขประเด็นที่เหลือได้โดยไม่กระทบต่อการนำระบบขึ้นใช้งาน"),
        "defect_none": "ไม่พบข้อบกพร่องที่ต้องบันทึกในการทดสอบครั้งนี้",
        "quality_exec": ("จากการทดสอบในรอบนี้ ระบบ {system} มีอัตราการผ่านโดยรวม {overall} (ผ่าน {passed} จาก "
                         "{total} กรณี) เมื่อพิจารณาเฉพาะกรณีที่ดำเนินการจริงจำนวน {executed} กรณี อัตราการผ่านที่แท้จริง "
                         "คิดเป็น {exec_rate} ซึ่ง {company} ประเมินว่าอยู่ในระดับ \"{level}\" โดยจำนวนข้อบกพร่องจำแนก "
                         "ตามระดับความรุนแรงได้ดังนี้: วิกฤต (Critical) {crit} สูง (High) {high} ปานกลาง (Medium) {med} "
                         "และต่ำ (Low) {low} ผลการประเมินนี้ถือเป็นพื้นฐานของการพิจารณาคุณภาพการส่งมอบโดย {company}"),
        "quality_noexec": ("ยังไม่มีกรณีทดสอบที่ดำเนินการจริง (ทั้งผ่านและไม่ผ่าน) ในรอบนี้ {company} จึงจัดระดับคุณภาพ "
                           "ของระบบ {system} เป็น \"{level}\" ไว้ก่อนจนกว่าจะมีการดำเนินการทดสอบ จำนวนกรณีทดสอบที่ "
                           "จัดเตรียมทั้งหมด: {total} กรณี จำนวนข้อบกพร่องจำแนกตามระดับความรุนแรง: วิกฤต (Critical) "
                           "{crit} สูง (High) {high} ปานกลาง (Medium) {med} และต่ำ (Low) {low}"),
        "recommend_default": ("{company} เสนอแนะให้ดำเนินการแก้ไขกรณีทดสอบที่ไม่ผ่านตามที่ระบุไว้ในหัวข้อที่ 6 ให้ "
                              "ครบถ้วนก่อนนำระบบ {system} ขึ้นใช้งานจริง โดยจัดลำดับความสำคัญตามระดับความรุนแรงของ "
                              "ข้อบกพร่อง และตรวจสอบยืนยันซ้ำ (Re-test) ทุกครั้งภายหลังการแก้ไข พร้อมทั้งขยายความ "
                              "ครอบคลุมของการทดสอบไปยังส่วนที่อยู่นอกขอบเขตเดิมตามความเหมาะสม และนำผลไปทบทวน "
                              "ร่วมกับผู้เกี่ยวข้องภายใต้กระบวนการกำกับดูแลการส่งมอบ"),
        "recommend_pass": ("กรณีทดสอบที่ดำเนินการทั้งหมดของระบบ {system} ผ่านครบถ้วนและไม่พบข้อบกพร่องในรอบนี้ "
                           "{company} จึงพิจารณาว่าไม่มีประเด็นที่เป็นอุปสรรคต่อการนำขึ้นใช้งานสำหรับขอบเขตที่ทดสอบ "
                           "และเสนอแนะให้สามารถดำเนินการส่งมอบ/นำขึ้นใช้งานฟังก์ชันที่ครอบคลุมได้ พร้อมทั้งคงชุดทดสอบนี้ "
                           "ไว้เป็นชุดทดสอบถดถอย (Regression) ประจำ และขยายความครอบคลุมไปยังส่วนที่อยู่นอกขอบเขตเดิม "
                           "(สมรรถนะ ความมั่นคงปลอดภัย การเข้าถึง) ก่อนหรือหลังการนำขึ้นใช้งานตามความเหมาะสม ภายใต้ "
                           "กระบวนการกำกับดูแลการส่งมอบ"),
        "next_default": ("ภายหลังการแก้ไขข้อบกพร่องที่ตรวจพบ {company} ควรดำเนินการทดสอบชุดเดิมซ้ำ (Regression "
                         "Testing) และเปรียบเทียบผลกับการทดสอบรอบนี้ เพื่อยืนยันว่าข้อบกพร่องได้รับการแก้ไขโดยไม่เกิด "
                         "ผลกระทบย้อนกลับต่อฟังก์ชันอื่น รวมทั้งควรผนวกกรณีทดสอบที่ตรวจพบข้อบกพร่องเข้าไว้ในชุดทดสอบ "
                         "ถดถอยประจำของ {company} เพื่อป้องกันการเกิดข้อบกพร่องซ้ำในรอบการพัฒนาถัดไป"),
        "appendix_glossary": "โปรดดูอภิธานศัพท์ที่ใช้ตลอดทั้งเอกสารในหัวข้อ 1.4",
        "appendix_workbook": ('รายละเอียดกรณีทดสอบทั้งหมด ตารางสอบทานความต้องการ (RTM) และทะเบียนข้อบกพร่อง จัดเก็บ '
                             'อยู่ในไฟล์เวิร์กบุ๊กประกอบ "test_cases.xlsx" ซึ่งอยู่ในโฟลเดอร์เดียวกับรายงานฉบับนี้'),
        "appendix_evidence_intro": ("ภาคผนวกส่วนนี้รวบรวมหลักฐานภาพหน้าจอของการทดสอบทุกกรณี (Test Case) อย่างครบถ้วน "
                                    "โดยจัดเรียงตามรหัสกรณีทดสอบ และจำแนกตามอุปกรณ์ที่ใช้ทดสอบ พร้อมคำอธิบายกำกับใต้ภาพ "
                                    "เพื่อให้สามารถตรวจสอบย้อนกลับได้อย่างสมบูรณ์"),
        "appendix_evidence_none": ('กรณีทดสอบนี้ไม่มีภาพหน้าจอประกอบ (เช่น การทดสอบระดับบริการ/API) หลักฐานอ้างอิง '
                                   'จัดเก็บในไฟล์เวิร์กบุ๊กประกอบ "test_cases.xlsx" สถานะผลการทดสอบ: {status}'),
        "appendix_evidence_missing": "[ไม่พบไฟล์ภาพหลักฐาน] {id} [{device}] — อ้างอิงไฟล์: {path}",
        "no_test_cases": "ไม่มีกรณีทดสอบ",
    },
    "quality_levels": {
        "not_assessed": "ยังไม่ได้ประเมิน", "excellent": "ดีเยี่ยม", "good": "ดี",
        "acceptable": "ยอมรับได้", "needs": "ต้องปรับปรุง",
    },
    "cover_date_start": "วันที่เริ่มการทดสอบ: {d}",
    "cover_date_end": "วันที่สิ้นสุดการทดสอบ: {d}",
    "cover_total": "จำนวนกรณีทดสอบทั้งหมด: {n} กรณี",
}
