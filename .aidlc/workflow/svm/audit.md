# Audit Trail — svm

### [2026-10-08T00:00:00Z] Context: assessment
- **Phase**: context
- **Action**: Workspace assessment + artifact generation
- **Artifacts**: context.md, blueprints (product/tech/structure/resources), platform shim (.kiro/steering/aidlc.md), manifest, audit.md
- **Outcome**: Greenfield / scope `new` / comprehensive mode. Stack = dipd-mcp baseline (Angular + .NET + EF Core) + SQL Server. Core end-to-end slice first. Awaiting user approval.

### [2026-10-08T00:10:00Z] Context: approval
- **Phase**: context
- **Action**: User approved context assessment ("proceed")
- **Artifacts**: context.md (approved)
- **Outcome**: context → sharedPhases. Handoff to aidlc-requirements.

### [2026-10-08T00:15:00Z] Requirements: decision-gate + validation
- **Phase**: requirements
- **Action**: D1 decision gate filled by user ("done"), summary populated, D1 validation run
- **Artifacts**: decisions-requirements.md
- **Outcome**: D1-1=2 (Core+SLA&Verification), D1-2=1, D1-3=1, D1-4=1, D1-5=1, D1-6=1, D1-7=1, D1-8=1, D1-9=1, D1-10=1 (Solo). No outstanding conflicts.

### [2026-10-08T00:20:00Z] Requirements: generation
- **Phase**: requirements
- **Action**: Generated personas + user stories with EARS acceptance criteria
- **Artifacts**: requirements.md (32 stories, 11 areas), personas.md (8 core personas + 8 summary)
- **Outcome**: requirements draft. Awaiting user approval.

### [2026-10-08T00:25:00Z] Requirements: approval + routing
- **Phase**: requirements
- **Action**: User approved requirements ("proceed"). Routing decision.
- **Artifacts**: requirements.md (approved), personas.md
- **Outcome**: requirements → sharedPhases. teamSize=solo. Comprehensive mode → decomposition skipped per hands-on-project policy. Routing to aidlc-design.
### [2026-10-08T00:40:00Z] Design: decision-gate + validation
- **Phase**: design
- **Action**: D3 decision gate filled by user ("done"), Decisions Summary populated, D3 validation run (no conflicts)
- **Artifacts**: decisions-design.md
- **Outcome**: D3 all = recommended. Repo single (web/+api/), Layered/Clean-ish, weighted-sum risk engine (DB-config weights), table-driven workflow state machine, interface+mock+seed adapters (UseMock toggle), local JWT behind IAuthProvider (OIDC-ready), offset pagination, append-only hash-chained audit, Minimal observability, log-based error tracking, basic health, minimal/verify-by-running testing, no property-based testing. Stored in manifest decisions.design.

### [2026-10-08T00:45:00Z] Design: generation
- **Phase**: design
- **Action**: Generated technical design + sub-documents; resolved stack versions via web search (.NET 10 LTS, Angular 22, EF Core 10, Node 24); synced blueprints tech.md + structure.md with D3 decisions
- **Artifacts**: design.md + design/{components,data-model,api-spec,integration,implementation,operations,nfr,testing-strategy}.md; blueprints/tech.md, blueprints/structure.md (updated); manifest (versions + artifacts.design=draft)
- **Outcome**: 15 components (C1–C15), 8 Angular views (F1–F8), ~28 entities (IRPC naming), ~45 endpoints /api/v1, 32 stories traced (0 gaps). Design status=draft. Awaiting user approval.

### [2026-10-08T00:50:00Z] Design: approval + routing
- **Phase**: design
- **Action**: User approved design ("proceed")
- **Artifacts**: design.md + design/* (approved)
- **Outcome**: design → sharedPhases. artifacts.design.status=approved. Routing to aidlc-tasks (implementation planning + execution waves).

### [2026-10-08T00:55:00Z] Tasks: decision-gate
- **Phase**: tasks
- **Action**: Generated D4 decision gate (7 questions: breakdown, test timing, build priority, integration, testing scope, granularity, execution model) with blank answers
- **Artifacts**: decisions-tasks.md
- **Outcome**: Awaiting user — "done" (fill answers) or "use recommendations".

### [2026-10-08T01:00:00Z] Tasks: decision-gate answered + validation
- **Phase**: tasks
- **Action**: User filled D4 ("done"), Decisions Summary populated, D4 validation run
- **Artifacts**: decisions-tasks.md
- **Outcome**: D4-1=2 (vertical slice), D4-2=2 (test-after core logic), D4-3=1 (foundation-first), D4-4=1 (mock+seed from task 1), D4-5=1 (unit core logic only), D4-6=1 (single-concern), D4-7=1 (sequential waves + stop-gate). No blocking conflicts (capacity/testing rules mitigated). Stored in manifest decisions.tasks.

### [2026-10-08T01:05:00Z] Tasks: generation
- **Phase**: tasks
- **Action**: Generated sequenced tasks + execution waves + coverage/traceability
- **Artifacts**: tasks.md; manifest (artifacts.tasks=draft, totalTasks=34)
- **Outcome**: 34 tasks / 10 phases / 7 sequential waves (stop-gate per wave per hands-on-project policy). Wave order Setup→FE+mock→Data/Access+Audit→Core backend flow→Dashboard/Admin+FE-wire→unit tests+verify→deploy(optional). Coverage 32/32 US, 15/15 components, ~28 entities, ~45 endpoints; 0 gaps, no circular deps. Status=draft. Awaiting user approval.

### [2026-10-08T01:10:00Z] Tasks: approval + routing
- **Phase**: tasks
- **Action**: User approved tasks ("approve")
- **Artifacts**: tasks.md (approved)
- **Outcome**: tasks → sharedPhases. artifacts.tasks.status=approved. Routing to aidlc-implement.

### [2026-10-08T01:15:00Z] Implement: mode-selection + Wave 1 blocked (toolchain)
- **Phase**: implementation
- **Action**: User chose standard mode ("go"); began Task 1.1 (Setup); probed toolchain
- **Artifacts**: aidlc-manifest.yaml (implementationMode=standard, currentTask=1.1, currentWave=1)
- **Outcome**: BLOCKED — environment missing required tools: git (not on PATH), Node.js 24/npm/npx (absent), .NET SDK (only runtimes present incl. 10.0.12; `dotnet --list-sdks` empty → no build/run/ef). Cannot satisfy Working-Software DoD (build + dev server). Paused for user to install toolchain or approve code-only scaffold without local verification.

### [2026-10-08T01:25:00Z] Implement: toolchain installed (blocker resolved)
- **Phase**: implementation
- **Action**: Installed toolchain via winget per user request ("ติดตั้งให้หน่อย")
- **Artifacts**: (machine toolchain) Git 2.55.0, Node.js 24.19.0 LTS + npm 11.17.0, .NET SDK 10.0.302
- **Outcome**: Verified all versions (match design baseline Node 24 / .NET 10). Wave 1 unblocked. PATH refreshed from registry per command (session restart not required for agent commands).

### [2026-10-08T09:12:00Z] Task Complete: 1.2 — Angular workspace + Ferrari tokens
- **Phase**: implementation
- **Action**: task 1.2 implemented (standard mode)
- **Artifacts**: web/ (Angular 22.2.0 scaffold), web/.postcssrc.json, web/src/styles.css (Tailwind 4 + daisyUI 5.7.47 + Ferrari theme/tokens), web/src/app/app.{ts,html,css} (CoreShell: responsive header nav + router-outlet + footer), web/src/app/shared/placeholder-page/placeholder-page.ts, web/src/app/app.routes.ts (F2–F8 placeholder routes), .kiro/steering/design-system.md
- **Outcome**: pass — `ng build` สำเร็จ (bundle complete, styles 18.75kB incl Tailwind+daisyUI+Ferrari, dist/web generated). FerrariSans (licensed) substituted with libre Inter/system stack per design-system HARD RULE #4/#9. 2/34 (6%).

### [2026-10-08T09:40:00Z] Infra: relocate package stores to D: (user request)
- **Phase**: implementation (infra)
- **Action**: ย้าย NuGet global packages, npm cache, npm global (Angular CLI) จาก C: (เต็ม) ไป D:\KiroWorkshop\.devtools\ ด้วย robocopy /MOVE; ตั้ง nuget.config (globalPackagesFolder) + npm config cache/prefix ชี้ไป D:; build temp → D:\KiroWorkshop\.tmp
- **Artifacts**: nuget.config (root), ~/.npmrc (cache+prefix), .gitignore (+.devtools/.tmp), component.md
- **Outcome**: C: free 5.81→6.55 GB. ทุก store อยู่บน D: แล้ว

### [2026-10-08T09:42:00Z] Task Complete: 1.3 — .NET solution + pin versions
- **Phase**: implementation
- **Action**: task 1.3 implemented (standard mode)
- **Artifacts**: api/SvmApi.slnx (4 projects: Api/Application/Domain/Infrastructure + Tests), api/global.json (SDK 10.0.302), api/Directory.Packages.props (CPM + transitive pinning), 5× packages.lock.json, api/src/SvmApi.Api/Program.cs (OpenAPI + ProblemDetails + ForwardedHeaders), removed template samples
- **Outcome**: pass — restore+build exit 0, NU1903=0 (fixed Microsoft.OpenApi vuln CVE-2026-49451 → pin 2.12.0). 3/34 (9%).

### [2026-10-08T09:50:00Z] Task Complete: 1.4 — Cross-cutting foundation
- **Phase**: implementation
- **Action**: task 1.4 implemented + verified by running (standard mode)
- **Artifacts**: api/src/SvmApi.Api/Program.cs (JSON logging prod/readable dev, RequestIdMiddleware, /health, AddSvmInfrastructure), api/src/SvmApi.Api/Middleware/RequestIdMiddleware.cs, api/src/SvmApi.Infrastructure/DependencyInjection.cs (AddSvmInfrastructure + IntegrationOptions seam), api/src/SvmApi.Infrastructure/Configuration/IntegrationOptions.cs, appsettings.json (Integration:UseMock=true), Directory.Packages.props (+Options.ConfigurationExtensions 10.0.12)
- **Outcome**: pass — build exit 0; dev server รันจริง, `GET /health` → 200 {"status":"ok","version":"1.0.0"}, X-Request-Id header set, IntegrationOptions validate-on-start ผ่าน. 4/34 (12%).

### [2026-10-08T09:51:00Z] Wave 1 Complete: Setup & โครงสร้างพื้นฐาน
- **Phase**: implementation (wave-gate)
- **Action**: Wave 1 (tasks 1.1–1.4) complete + verified
- **Outcome**: git repo (main); Angular 22 + Tailwind4/daisyUI5 + Ferrari tokens builds; .NET 10 solution (4 proj + tests, CPM, lock files, SDK pin, OpenApi vuln fixed) builds; API runs with health/logging/request-id/mock-toggle. Package stores relocated to D:. STOP for user confirmation before Wave 2 (per hands-on-project policy).

### [2026-10-08T14:05:00Z] Wave 2 Complete: Core FE + Mock JSON
- **Phase**: implementation (wave-gate)
- **Action**: tasks 2.1 + 2.2 implemented + verified by running
- **Artifacts**: web/src/environments/environment.ts (useMock), app.config.ts (+provideHttpClient +provideSvmApi), core/models.ts, core/api/{svm-api,mock-svm-api,svm-api.provider}.ts, public/mock/*.json (vulnerabilities, dashboard, assets, import-batches, dedup-queue, notifications, audit, admin), shared/{labels.ts, ui/ui.ts, ui/bar-list.ts}, features/{dashboard,vulnerabilities(list+detail),intake,remediation,verification,admin,audit}, app.routes.ts (lazy), styles.css (+page utilities), build-web.ps1/serve-web.ps1, start.md
- **Outcome**: pass — `ng build` exit 0 (all lazy chunks emitted); dev server http://127.0.0.1:4200 runs; verified HTTP: / and /dashboard → 200 (SPA), /mock/vulnerabilities.json → 200 (21.7KB), /mock/dashboard.json → 200. 7 views on mock data with filter/offset-pagination/detail/workflow actions/states/responsive/Ferrari tokens. 6/34 (18%). STOP for user confirmation before Wave 3 (per hands-on-project policy).

### [2026-10-21T03:20:00Z] Add-on: UI motion + form UX polish (user-requested, out-of-scope enhancement)
- **Phase**: implement (add-on)
- **Action**: User requested performance-friendly animations/loading plus form UX review and app-wide success feedback pattern. Implemented after confirming scope.
- **Artifacts**:
  - `web/src/styles.css` — new compositor-only motion utilities (`.enter` fade/rise, `.skeleton` shimmer, both honoring `prefers-reduced-motion`); global `.visually-hidden`; reusable upload primitives (`.dropzone`, `.filecard`, `.icon-btn`, `.btn-spinner`, `.result-card`/`.result-stats`).
  - `web/src/app/shared/ui/ui.ts` — `StateBlock` gains a `skeleton` mode (+`rows` input) alongside existing loading/empty/error.
  - `web/src/app/shared/ui/icon.ts` — added icons: trash, file, cloud, check-circle, info.
  - `web/src/app/core/toast/` (new) — `ToastService` + `ToastHost`: non-intrusive toast feedback (success/info auto-dismiss, error persists). Mounted once in shell (`app.ts`/`app.html`).
  - `web/src/app/core/dialog/` (new) — `ConfirmService` + `ConfirmHost`: native `<dialog>` modal reserved for consequential/irreversible actions only. Mounted in shell.
  - `web/src/app/features/action-support.ts` — `ActionState.run()` now also emits toasts, so every existing form gains consistent success/error feedback without per-page edits.
  - `web/src/app/features/intake/intake.ts` — import tab rebuilt: drag-and-drop dropzone, selected-file card (name/size/remove), accepted-format hint, inline "processing…" button spinner, structured result summary card. Added drag/drop + file-type guard + size-label logic.
  - List pages (intake dedup/history, vulnerability-list, remediation, verification, audit) — loading state switched to `skeleton`; loaded tables get `.enter` fade-in.
  - `web/src/app/features/verification/verification.ts` — `perform()` now gated by a confirm dialog (danger style for result recording, which changes workflow state irreversibly).
- **UX decision**: No modal "success" dialog — routine success uses non-intrusive toasts (Nielsen: visibility of system status without interruption). Modal dialogs reserved for destructive/irreversible confirmation.
- **Verification**: `npm run build` passes. Intake component CSS budget resolved by moving shared upload styles to global `styles.css`. Remaining budget warnings (login.css, dashboard.css, app.css) are pre-existing and unrelated to this change.
- **Outcome**: Build-verified. Add-on complete.

### [2026-10-21T04:20:00Z] Add-on: install Impeccable skill (Option B) + action-button affordance pass
- **Phase**: implement (add-on)
- **Action**: User asked to install and use the "impeccable" design skill to fix hard-to-read save/action buttons. Searched, confirmed real skill = `pbakaus/impeccable` (Apache-2.0). Installed Option B (SKILL + selected references only; no npx, no engine binary, no detector CLI, no live/generate). Then ran an Operate-mode button refinement.
- **Artifacts**:
  - `.kiro/skills/impeccable/SKILL.md` — adapted setup for no-binary mode; wired precedence to SVM steering (`design-system.md`, `design-system-policies.md`) and `web/src/styles.css` tokens; marked engine-dependent commands as unavailable.
  - `.kiro/skills/impeccable/reference/operate.md`, `reference/craft-floor.md` — reproduced upstream playbooks (Apache-2.0, attributed), with SVM-specific notes so it refines rather than replaces the approved design system.
  - `.kiro/skills/impeccable/LICENSE.txt` — Apache-2.0 + Option-B scope note.
  - `web/src/styles.css` — button system upgrade (within existing tokens): `.btn` primary now carries `--shadow-sm` + pressed `:active`; `.btn-outline`/`.btn-ghost` get hover+active+clear resting affordance (ghost gains a brand-tinted hairline so it reads as a control inside table rows); new global `.btn--danger` and compact `.btn-sm`; disabled/reduced-motion guards.
  - `web/src/app/core/dialog/confirm-host.ts` — dropped local `.btn--danger` in favor of the global token-backed class.
  - `web/src/app/features/verification/verification.ts` — row actions use `.btn-ghost .btn-sm`; `.act` gap 4px→8px.
  - `web/src/app/features/remediation/remediation.ts` — row action uses `.btn-ghost .btn-sm`.
  - `web/src/app/features/intake/intake.ts` — dedup decision cell restructured into `.decision`/`.row-actions`; merge = `.btn-outline .btn-sm` (stronger intent), create-new = `.btn-ghost .btn-sm`.
- **Diagnosis (Impeccable Operate mode)**: row-action buttons were all faint borderless ghosts that read as plain text (fails "consistent affordances"/"earned familiarity"); no pressed/active state; primary vs secondary hierarchy unclear. Fixes stay inside the SVM token system — no new colors/fonts, no emoji, light theme preserved.
- **Verification**: `npm run build` passes. Pre-existing budget warnings (app.css, login.css, dashboard.css) unchanged and unrelated. Visual confirmation needs the user to run the dev server.
- **Outcome**: Skill installed (Option B) and applied. Add-on complete.

### [2026-10-21T04:43:00Z] Add-on: Impeccable audit + layout pass on Dashboard (stage 1 of 4)
- **Phase**: implement (add-on)
- **Action**: Ran Impeccable `audit` (review-only) on the Dashboard, then `layout` (code) fixes. Contrast measured programmatically (temp script, removed) before deciding.
- **Audit findings (6)**: #1 contrast (measured — ALL pairs pass WCAG 2.2 AA; worst was fg-muted on overlay 4.88 and brand on brand-soft 4.51), #2 missing explicit focus-visible on clickable metric cards / queue filters, #3 hero-metric template (kept — justified for a security dashboard, SVM design wins), #4 metric cards used border-only vs shadowed cards elsewhere, #5 hover timing inconsistency (metric used --dur-base vs --dur-fast elsewhere), #6 queue-filter missing :active/:disabled.
- **Fixes applied (option ก, layout)** in `web/src/app/features/dashboard/dashboard.css`:
  - #4 `.metric, .secondary-metrics` → `box-shadow: var(--shadow-sm)`; `.metric:hover` lifts to `--shadow-md`.
  - #5 `.metric` transition standardized to `--dur-fast` covering border-color + box-shadow.
  - #6 `.queue-filter` gains `:active:not(:disabled)` (translateY 1px, reduced-motion guarded), `:disabled` (not-allowed, 0.55), hover scoped `:not(:disabled)`.
  - #2 `.metric:focus-visible, .queue-filter:focus-visible` → brand border + 2px outline + offset (clear keyboard focus).
- **Fix applied (option ข, contrast buffer)** in `web/src/styles.css`:
  - `--color-fg-muted` darkened `#5b6b7e` → `#556377`. Worst-case contrast across all surface/base/raised/muted/overlay backgrounds rises from 4.88 to 5.47 (comfortable buffer above AA). Same blue-gray hue family; affects placeholders, scrollbar thumb, breadcrumb separator app-wide (all improve).
- **Delegation note**: layout fixes #4/#5/#6 and the initial #2 were done via a general-task-execution sub-agent (interrupted before reporting); verified its edits on disk and completed #2's focus ring.
- **Verification**: `npm run build` passes, no errors. `dashboard.css` budget warning 7.07→7.53kB (pre-existing over-budget; +0.46kB acceptable). Pre-existing app.css/login.css warnings unchanged.
- **Outcome**: Dashboard stage complete (audit + layout + contrast buffer). Next planned stages: Admin `harden`, Vulnerability-detail `critique`, buttons/forms `polish` — awaiting user go-ahead per hands-on-project stop-between-groups rule.

### [2026-10-21T05:13:00Z] Add-on: Impeccable harden pass on Admin (stage 2 of 4)
- **Phase**: implement (add-on)
- **Action**: Ran Impeccable `harden` on Admin config forms. Added client-side validation guards + inline visual warnings (no backend/data-model changes, no new fields). Defense-in-depth: same checks gate the save methods and disable the buttons.
- **Findings addressed**: SLA add/save had no validation (hours could be 0/empty, no ack≤start≤resolve ordering); assignment rules didn't enforce "match value required" or "user XOR team" (subtitle stated the rule, UI didn't); weights/thresholds could be saved while sum≠1 or ranges non-contiguous; `weightSum()` could render NaN.
- **Artifacts**:
  - `web/src/app/features/admin/admin.ts` — added `weightsValid()`, `thresholdsValid()` (0–100 contiguous, min<max), `slaValid()` (positive + ack≤start≤resolve), `ruleValid()` (matchValue + user XOR team); hardened `weightSum()` against NaN; guarded `saveWeights/saveThresholds/saveSla/saveRule` to set a clear error and bail when invalid; added `.invalid`/`.hint-error` styles.
  - `web/src/app/features/admin/admin.html` — disabled save/add buttons when invalid and added red inline hint messages for weights, thresholds, each SLA row + SLA draft, each rule row + rule draft.
- **Scope note**: client-side UX/robustness guards only; backend remains the source of truth (errors still surface via toast/ProblemDetails). No API, requirement, or data-model change.
- **Verification**: `npm run build` passes, no errors. Admin chunk 28.3→32.6kB (added logic/template). No new CSS budget warnings (admin uses inline component styles). Pre-existing app/login/dashboard css warnings unchanged.
- **Outcome**: Admin stage complete. Remaining: Vulnerability-detail `critique` (stage 3), buttons/forms `polish` (stage 4).

### [2026-10-21T05:19:00Z] Add-on: Impeccable critique + polish on Vulnerability detail (stages 3 & 4)
- **Phase**: implement (add-on)
- **Action**: Ran Impeccable `critique` (review-only) on vulnerability-detail, then `polish` incorporating the three actionable critique findings (#2/#5/#6).
- **Critique findings**: strong on function/robustness (rowVersion optimistic concurrency + reload + clear errors, permission gating, complete empty states, responsive) but weak visual hierarchy — all 9 panels look identical; the shared "reason" field silently feeds transition/assign/adjust/recalculate (ambiguous linkage); action buttons inconsistent across panels; progress bar lacked progressbar semantics.
- **Polish applied** in `web/src/app/features/vulnerabilities/vulnerability-detail.html` + `.css`:
  - #6 progress bar now `role="progressbar"` + `aria-valuenow/min/max` (a11y).
  - #5 status-transition buttons promoted from `.btn-outline` to `.btn` (primary) — the page's main action now reads as primary consistently.
  - #2 reason field gains a visible hint ("ใช้ร่วมกับทุกการดำเนินการ…", `aria-describedby`) and the risk adjust/recalculate form points back to the Workflow reason field; recalculate button now also requires a reason (consistent with the API call that already sends it). Added `.field-hint` style.
- **Scope note**: UI/UX + a11y refinements only; no API, data-model, or workflow-logic change. #3 (badge grouping) and #4 (column balance) judged cosmetic and intentionally left.
- **Verification**: `npm run build` passes, no errors. vulnerability-detail chunk 33.2→34.0kB. No new CSS budget warnings. Pre-existing app/login/dashboard warnings unchanged.
- **Outcome**: All 4 planned Impeccable stages complete — Dashboard (audit+layout+contrast), Admin (harden), Vulnerability-detail (critique+polish). Button/form system already hardened in earlier add-ons.

### [2026-10-21T05:30:00Z] Add-on: Vulnerability-detail critique remainder (#1/#3/#4)
- **Phase**: implement (add-on)
- **Action**: User asked to also fix the three critique findings previously deferred as cosmetic.
- **Artifacts** (`web/src/app/features/vulnerabilities/vulnerability-detail.html` + `.css`):
  - #3 Risk score promoted from a muted inline text in the badge row to a dedicated headline metric block at the head (`.detail-head__score`: display-size value + label, bordered card with shadow). Head is now a flex row (main + score) that wraps on narrow screens.
  - #1 Workflow actions panel given a 3px brand top-accent (`border-top`) so the primary work area reads above the read-only reference panels (Operate-mode hierarchy); token-based.
  - #4 Column rebalance: moved the Evidence panel from the (long) left column to the right column after Verification (semantically related reference material). Left = overview/risk/remediation forms; right = SLA/verification/evidence/history/comments. Reduces the left-heavy vertical imbalance.
- **Scope note**: layout/hierarchy only; no logic/data/API change. Evidence move is a DOM relocation of the full `<section>` with identical bindings.
- **Verification**: `npm run build` passes, no errors; moved section compiles (nesting intact). vulnerability-detail chunk 34.0→34.9kB. No new CSS budget warnings.
- **Outcome**: All 7 critique findings now resolved or consciously accepted. Impeccable 4-stage plan fully complete.

### [2026-10-21T05:51:00Z] Add-on: Impeccable critique + fixes on Vulnerability-list & Remediation
- **Phase**: implement (add-on)
- **Action**: `critique` on the two remaining list pages, then fixed all findings (bugs + consistency + UX + cosmetic).
- **Vulnerability-list** (`vulnerability-list.html/.css/.ts`):
  - #1/#2 BUG: the SLA filter had a stray `<label for="sort">เรียงตาม</label>` (duplicate `for`, wrong text) hidden by a `:has(#sla) > label:first-child {display:none}` CSS hack. Removed the ghost label, pointed the real label at `#sla`, deleted the hack. Fixes broken label/`for` associations (a11y).
  - #3 Filter chips are now removable: `activeFilters()` returns `{key,label}[]`, added `clearFilter(key)` + `FilterKey` type and a per-chip `×` button (aria-labelled).
  - #4 Toolbar toggles (KEV / open-only) + reset button grouped into a `.toolbar__toggles` row (reset pushed to the end) instead of being interleaved between checkboxes.
- **Remediation** (`remediation.ts`):
  - #6 Removed dead `.pbar`/`.pbar__fill` CSS (no matching markup).
  - #5 Replaced the raw "หน้า X · N รายการ" pager with the same `.pager` pattern as the list (range + page X/Y + `.btn-outline` prev/next); added `pageSize`/`totalPages`/`rangeStart`/`rangeEnd`/`prev`/`next`.
  - #7 Action cell wrapped in `.act-cell` (column, gap); the row button is now `.btn-outline .btn-sm` and the secondary link uses `.btn-link` for clearer hierarchy.
- **Scope note**: UI/UX/a11y + a real markup bug fix; no API/data/logic change. Filter semantics unchanged (clearFilter reuses existing onFilterChange + URL sync).
- **Verification**: `npm run build` passes, no errors. vulnerability-list 14.7→15.9kB, remediation 13.3→14.3kB. No new CSS budget warnings.
- **Outcome**: All feature pages (dashboard, vulnerabilities list+detail, intake, remediation, verification, admin, audit) have now had an Impeccable pass.

### [2026-10-21T06:10:00Z] Add-on: Impeccable clarify + dark theme (toggle)
- **Phase**: implement (add-on)
- **Action**: Ran `clarify` (UX copy) then built a full opt-in dark theme with a topbar toggle (user-approved, overriding the prior light-only decision per design-system HARD RULE #7).
- **clarify**:
  - `vulnerability-list`: SLA filter dropdown + active-filter chip rendered raw enum values (Breached/NearDue/…) — now use `SLA_LABEL` (Thai). Added `slaLabel` to the component.
  - `verification`: selected-item line showed raw `v.status`/`v.riskLevel` — now use `STATUS_LABEL`/`RISK_LABEL`.
- **dark theme**:
  - `web/src/styles.css`: full color-token override under `:root[data-theme="dark"]` (surfaces/text/brand/risk×5/semantic/shadows). All text-on-surface pairs verified WCAG 2.2 AA via a temp script (worst 4.52, removed after). `color-scheme: dark` so native controls/scrollbars adapt.
  - `web/src/app/core/theme/theme.service.ts` (new): light default, dark opt-in, persisted in `localStorage` (`svm-theme`), first-visit fallback to `prefers-color-scheme`; applies `data-theme` on `<html>`.
  - Topbar static `.theme-label` → real `.theme-toggle` button (icon sun/moon + LIGHT/DARK, `aria-pressed`, `aria-label`); `App` injects `ThemeService`. Added `moon` icon.
  - `.theme-toggle` styles placed in global `styles.css` (not `app.css`, which was at its 8kB hard budget — initial build FAILED at 8.25kB; moving the rule out fixed it).
  - Steering `design-system.md` updated: records dark-theme approval + token values + toggle rationale (HARD RULE #7 compliance).
- **Verification**: `npm run build` passes (after the budget fix). Pre-existing app/login/dashboard css warnings remain (app.css back to 7.65kB, under the 8kB error limit). Visual confirmation of both themes needs the user to run the dev server.
- **Outcome**: clarify + dark theme complete.
