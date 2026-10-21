"""Scope_Detector deterministic reference core.

This module is the executable, testable reference implementation of the
orchestrator's scope-detection logic described in ``design.md`` ("Scope
Detection"). The natural-language ``SKILL.md`` prose mirrors the behavior
encoded here.

It maps a workspace manifest (a set of marker flags plus a few state flags)
to a :class:`ScopeResult`:

* ``layers``   - subset of {UI, API, Unit-Integration} (union of matched markers)
* ``level``    - exactly one of {smoke, focused, full}
* ``entry``    - the lifecycle stage to start from, by artifacts present
* ``url_mode`` - True when no in-project application (UI/API) markers exist

The functions here are pure and deterministic so the design's correctness
properties (Properties 1-4 and 9) are directly checkable.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import FrozenSet, Iterable, Set

# ---------------------------------------------------------------------------
# QA layer / level / entry vocabularies
# ---------------------------------------------------------------------------

UI = "UI"
API = "API"
UNIT_INTEGRATION = "Unit-Integration"

LAYERS = frozenset({UI, API, UNIT_INTEGRATION})

# Scope levels (exactly one is selected per project state).
SMOKE = "smoke"
FOCUSED = "focused"
FULL = "full"

LEVELS = frozenset({SMOKE, FOCUSED, FULL})

# Lifecycle entry stages.
INTAKE = "Intake"
ANALYZE = "Analyze"
GENERATE = "Generate"
VALIDATE_ONE = "ValidateOne"
SCALE_SUITE = "ScaleSuite"
REPORT = "Report"
RERUN = "ReRun"

ENTRIES = frozenset(
    {INTAKE, ANALYZE, GENERATE, VALIDATE_ONE, SCALE_SUITE, REPORT, RERUN}
)

# ---------------------------------------------------------------------------
# Marker vocabularies (signals scanned for in the workspace)
#
# ADVISORY ONLY. These lists are a convenience heuristic of common signals —
# they are NOT exhaustive and NOT a hard gate. The skill (see SKILL.md
# "Intake & Project Analysis") asks the user a short intake, analyzes the
# project open-endedly, and lets the user confirm/add/remove layers. This
# module just provides deterministic, testable defaults; the user's answers win.
# ---------------------------------------------------------------------------

# Web application code -> UI QA_Layer.
WEB_MARKERS: FrozenSet[str] = frozenset(
    {
        "index.html",
        "package.json:react",
        "package.json:vue",
        "package.json:svelte",
        "package.json:next",
        "package.json:angular",
        "package.json:astro",
        "package.json:solid",
        "package.json:qwik",
        "package.json:remix",
        "app/routes",  # Remix / Next file-based routing
        "src/jsx",
        "src/tsx",
        "src/vue",
        "devserver",
    }
)

# API definitions -> API QA_Layer.
API_MARKERS: FrozenSet[str] = frozenset(
    {
        "openapi.json",
        "openapi.yaml",
        "swagger.json",
        "swagger.yaml",
        "openapi_dir",
        "route_file",
        "proto",
        # GraphQL schema/directory markers.
        "schema.graphql",
        "graphql_dir",
    }
)

# Unit/integration test frameworks -> unit/integration QA_Layer.
TEST_MARKERS: FrozenSet[str] = frozenset(
    {
        "jest",
        "vitest",
        "mocha",
        "pytest",
        "tests_dir",
        "go_test",
        "phpunit",
        "junit",
        "bun_test",         # Bun's built-in test runner
        "rspec",            # Ruby RSpec
        "cargo_test",       # Rust cargo test
        "playwright_test",  # existing Playwright specs
    }
)

# "In-project application" markers are UI or API source signals. A test
# framework alone does not constitute an application (see design pseudocode:
# ``url_mode = no_app_markers(workspace)  # UI/API source absent``).
APP_MARKERS: FrozenSet[str] = WEB_MARKERS | API_MARKERS

# ---------------------------------------------------------------------------
# UI end-to-end DRIVER detection (tool-agnostic).
#
# The skill is NOT fixed to one UI test tool. If the project already uses a UI
# e2e driver, reuse it; only when none is present does the skill fall back to its
# bundled Playwright default. Each marker flag maps to the driver it implies.
# Whatever driver runs, the EVIDENCE CONTRACT is identical (a screenshot per
# case, per-case video, per-device + per-flow full videos) so the reports never
# depend on the tool.
# ---------------------------------------------------------------------------

UI_DRIVER_PLAYWRIGHT = "playwright"
UI_DRIVER_CYPRESS = "cypress"
UI_DRIVER_WEBDRIVERIO = "webdriverio"
UI_DRIVER_SELENIUM = "selenium"
UI_DRIVER_PUPPETEER = "puppeteer"

UI_DRIVERS = frozenset(
    {
        UI_DRIVER_PLAYWRIGHT,
        UI_DRIVER_CYPRESS,
        UI_DRIVER_WEBDRIVERIO,
        UI_DRIVER_SELENIUM,
        UI_DRIVER_PUPPETEER,
    }
)

# Marker flag -> the driver it indicates (existing project tooling).
UI_DRIVER_MARKERS: dict[str, str] = {
    "playwright.config": UI_DRIVER_PLAYWRIGHT,
    "package.json:@playwright/test": UI_DRIVER_PLAYWRIGHT,
    "cypress.config": UI_DRIVER_CYPRESS,
    "package.json:cypress": UI_DRIVER_CYPRESS,
    "wdio.conf": UI_DRIVER_WEBDRIVERIO,
    "package.json:webdriverio": UI_DRIVER_WEBDRIVERIO,
    "package.json:selenium-webdriver": UI_DRIVER_SELENIUM,
    "selenium": UI_DRIVER_SELENIUM,
    "package.json:puppeteer": UI_DRIVER_PUPPETEER,
}

# Default UI driver when the project has no existing e2e tooling.
DEFAULT_UI_DRIVER = UI_DRIVER_PLAYWRIGHT


def detect_ui_driver(workspace: "Workspace") -> str:
    """Return the UI e2e driver to use.

    Reuses the project's existing driver when a :data:`UI_DRIVER_MARKERS` flag is
    present (first match by marker order); otherwise returns
    :data:`DEFAULT_UI_DRIVER` (the bundled Playwright reference driver). The
    returned driver is advisory — the user may override it at the scope gate.
    """
    present = set(workspace.markers)
    for marker, driver in UI_DRIVER_MARKERS.items():
        if marker in present:
            return driver
    return DEFAULT_UI_DRIVER

# Source-richness thresholds for scope-level selection.
MODERATE_SOURCE_THRESHOLD = 3
RICH_SOURCE_THRESHOLD = 10


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Workspace:
    """A workspace manifest.

    ``markers`` is the set of detected marker flags (drawn from
    ``WEB_MARKERS`` / ``API_MARKERS`` / ``TEST_MARKERS``). The remaining
    fields capture lightweight project state used for scope-level and
    entry-stage selection.
    """

    markers: FrozenSet[str] = field(default_factory=frozenset)
    source_count: int = 0
    has_prior_run: bool = False
    # Artifact presence flags used for entry-stage selection.
    has_xlsx: bool = False  # test_cases.xlsx present
    has_specs: bool = False  # generated Playwright specs present
    has_results: bool = False  # prior execution results present

    def __post_init__(self) -> None:
        # Normalize markers to a frozenset regardless of input iterable type.
        object.__setattr__(self, "markers", frozenset(self.markers))


@dataclass(frozen=True)
class ScopeResult:
    """Output of :func:`detect_scope`."""

    layers: FrozenSet[str]
    level: str
    entry: str
    url_mode: bool


# ---------------------------------------------------------------------------
# Marker helpers
# ---------------------------------------------------------------------------


def _any_marker(markers: Iterable[str], vocabulary: FrozenSet[str]) -> bool:
    """True when any marker in ``markers`` belongs to ``vocabulary``."""
    return bool(set(markers) & vocabulary)


def has_app_markers(workspace: Workspace) -> bool:
    """True when in-project application (UI or API) markers are present."""
    return _any_marker(workspace.markers, APP_MARKERS)


def detect_layers(workspace: Workspace) -> FrozenSet[str]:
    """Map present markers to enabled QA layers (union semantics)."""
    layers: Set[str] = set()
    if _any_marker(workspace.markers, WEB_MARKERS):
        layers.add(UI)
    if _any_marker(workspace.markers, API_MARKERS):
        layers.add(API)
    if _any_marker(workspace.markers, TEST_MARKERS):
        layers.add(UNIT_INTEGRATION)
    return frozenset(layers)


# ---------------------------------------------------------------------------
# Scope-level selection (Req 1.6)
# ---------------------------------------------------------------------------


def select_level(workspace: Workspace, layers: FrozenSet[str]) -> str:
    """Select exactly one Scope_Level from {smoke, focused, full}.

    Decision matrix (from design.md):
    * full    - multiple layers (>=2) AND rich sources
    * focused - one or two layers, moderate sources, and a prior run exists
    * smoke   - otherwise (single layer / few sources / first-time repo)
    """
    n_layers = len(layers)

    if n_layers >= 2 and workspace.source_count >= RICH_SOURCE_THRESHOLD:
        return FULL

    if (
        1 <= n_layers <= 2
        and workspace.has_prior_run
        and workspace.source_count >= MODERATE_SOURCE_THRESHOLD
    ):
        return FOCUSED

    return SMOKE


# ---------------------------------------------------------------------------
# Entry-stage selection (Req 7.5)
# ---------------------------------------------------------------------------


def select_entry(workspace: Workspace) -> str:
    """Select the entry Lifecycle_Stage from the project state.

    State -> entry mapping (from design.md "Entry-Stage Selection"):
    * no in-project app markers              -> Intake (External_URL_Mode)
    * no test_cases.xlsx AND no specs        -> Intake
    * test_cases.xlsx AND specs missing      -> Generate (regenerate specs)
    * xlsx AND specs AND prior results       -> ReRun
    * xlsx AND specs AND no results          -> ScaleSuite (specs ready, unrun)
    * specs present without xlsx (degenerate)-> Intake
    """
    # No in-project application -> start at Intake (url mode set separately).
    if not has_app_markers(workspace):
        return INTAKE

    if not workspace.has_xlsx and not workspace.has_specs:
        return INTAKE

    if workspace.has_xlsx and not workspace.has_specs:
        return GENERATE

    if workspace.has_xlsx and workspace.has_specs:
        if workspace.has_results:
            return RERUN
        return SCALE_SUITE

    # Remaining: specs exist but no xlsx (unusual) -> fresh Intake.
    return INTAKE


# ---------------------------------------------------------------------------
# Top-level detection
# ---------------------------------------------------------------------------


def detect_scope(workspace: Workspace) -> ScopeResult:
    """Run the full Scope_Detector over a workspace manifest."""
    layers = detect_layers(workspace)
    url_mode = not has_app_markers(workspace)
    level = select_level(workspace, layers)
    entry = select_entry(workspace)
    return ScopeResult(layers=layers, level=level, entry=entry, url_mode=url_mode)


def run_set(detected: Iterable[str], user_selection: Iterable[str]) -> FrozenSet[str]:
    """Return the layers to actually run: the user's confirmed selection.

    The user may narrow, expand, or fully replace the detected layers at the
    scope-confirmation gate (SKILL.md Intake §3). Detection is a proposal;
    the user's explicit selection is authoritative.
    """
    selected = frozenset(user_selection)
    # Validate that all requested layers are known vocabulary.
    unknown = selected - LAYERS
    if unknown:
        raise ValueError(f"Unknown QA layers requested: {unknown}")
    return selected
