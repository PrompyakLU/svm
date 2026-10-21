---
name: impeccable
description: "Use when the user wants to design, redesign, shape, critique, audit, polish, simplify, harden, animate, or otherwise improve a frontend interface: websites, landing pages, dashboards, product UI, components, forms, settings, onboarding, empty states. Handles UX review, hierarchy, layout, typography, color, motion, UX copy, accessibility, responsive behavior, and edge cases. Not for backend-only or non-UI tasks."
argument-hint: "[audit|critique|polish|bolder|quieter|distill|harden|animate|colorize|typeset|layout|delight|clarify|adapt|shape] [target]"
license: Apache-2.0
metadata:
  author: "Paul Bakaus (pbakaus/impeccable)"
  upstream: "https://github.com/pbakaus/impeccable"
  version: "4.7.0"
  install-mode: "Option B — SKILL + selected references only; no binary/engine, no npx. The deterministic detector CLI and live-browser commands from upstream are NOT included."
---

This skill gives you the tools and permission to make design that earns the name out-of-distribution craft. Work as an award-winning design director would: production-grade code, a clear POV, a deep grasp of what the client and its users need, and exceptional craft.

Core principles:
- Finish the job. No hedging, no shortcuts: the deliverable is complete, except assets the user must provide.
- Scale ambition to the surface's mode (see Modes). Persuade and Experience get full commitment: bold, distinct work with a point of view. On Operate and Read, ambition shows as precision and restraint, and patterns the visitor already knows earn their place.
- Verify by looking at the rendered result at the sizes it ships at, and batch the checks: fix what one look shows before looking again.

## Setup (Option B — no binary)

This install does NOT include the upstream `scripts/impeccable` binary, the live-browser commands, or the deterministic detector hook. Do not try to run `impeccable context`, `impeccable pin`, `/impeccable hooks`, `/impeccable doctor`, `live`, or `generate` — those depend on the engine that was intentionally not installed.

Instead, before any UI edit:
1. Read the project's existing design authority directly — this is the source of truth and OVERRIDES any generic guidance in this skill:
   - `.kiro/steering/design-system.md` (SVM approved light theme, token contract)
   - `.kiro/steering/design-system-policies.md` (workspace HARD RULES: tokens as CSS vars, WCAG 2.2 AA, no emoji, light default, licensed-font ban, etc.)
   - `web/src/styles.css` (runtime token values — the real values)
2. Load the matching reference playbook below for the requested command.
3. Immediately before editing, read `reference/craft-floor.md` for the quality floor and bans.

**Precedence:** SVM steering + `styles.css` tokens WIN over anything in this skill. Never introduce colors, fonts, or values outside the existing token system. Never add emoji or a theme toggle (per workspace rules). When this skill and SVM policy conflict, follow SVM policy and say so.

## How to design

- **The brief wins.** Honor the committed SVM design system even when generic "best practice" would differ. Redirecting a clear brief toward your own taste is failure.
- **Refinement preserves; redesign replaces.** Refinement keeps the incumbent identity, behavior, copy, and everything outside scope. The current task is refinement of SVM's existing UI — preserve the light theme, teal brand, token contract, and component vocabulary.
- **Loaded symbols stay out of the decoration.** No militarist/supremacist/hate iconography as motifs or ornament.

## Modes

The mode names what the visitor's success looks like on this surface. **SVM is an Operate surface** (authenticated security dashboard / task UI). On Operate: scanability, consistency, native expectations, and the real usage scene outrank expression. Brand lives in precise details, not decoration. See `reference/operate.md`.

- **Persuade:** visitor decides and acts (landing/marketing). Not SVM.
- **Operate:** visitor completes a task (app UI, dashboards, settings, tables). **This is SVM.**
- **Read:** visitor understands something (docs, guides).
- **Experience:** visitor is inside the work (portfolios, galleries).

## Commands

Available in this Option-B install (reference-backed, no binary):

| Command | Category | Description | Reference |
|---|---|---|---|
| `shape [feature]` | Build | Plan UX/UI before writing code | (inline — see How to design) |
| `critique [target]` | Evaluate | UX design review with heuristic scoring | `reference/craft-floor.md` + `reference/operate.md` |
| `audit [target]` | Evaluate | Technical quality checks (a11y, contrast, responsive, states) | `reference/craft-floor.md` |
| `polish [target]` | Refine | Final quality pass before shipping | `reference/craft-floor.md` |
| `bolder [target]` | Refine | Amplify safe or bland designs (within SVM tokens) | `reference/craft-floor.md` + `reference/operate.md` |
| `quieter [target]` | Refine | Tone down overstimulating designs | `reference/operate.md` |
| `distill [target]` | Refine | Strip to essence, remove complexity | `reference/craft-floor.md` |
| `harden [target]` | Refine | Production-ready: errors, i18n, edge cases, all states | `reference/operate.md` |
| `layout [target]` | Enhance | Fix spacing, rhythm, visual hierarchy | `reference/craft-floor.md` + `reference/operate.md` |
| `typeset [target]` | Enhance | Improve typography hierarchy | `reference/operate.md` |
| `colorize [target]` | Enhance | Strategic color on monochromatic UI (SVM semantic tokens only) | `reference/operate.md` |
| `animate [target]` | Enhance | Purposeful motion (compositor-only, respect prefers-reduced-motion) | `reference/craft-floor.md` |
| `delight [target]` | Enhance | Memorable touches (sparingly, Operate = moments not pages) | `reference/operate.md` |
| `clarify [target]` | Fix | Improve UX copy, labels, error messages (Thai product language) | `reference/craft-floor.md` |
| `adapt [target]` | Fix | Responsive across devices | `reference/operate.md` |

Commands NOT available in this install (needed the upstream engine): `init`, `document`, `extract`, `overdrive`, `optimize`, `live`, `generate`, `pin`, `hooks`, `doctor`. If asked for one, say it needs the full `npx impeccable install` and offer the closest reference-backed command instead.

Routing:
- **Explicit command:** load its reference(s) and follow them, filtered through SVM steering/tokens.
- **No argument / general "make this better":** treat as Operate-mode refinement of the named SVM surface; read `reference/operate.md` + `reference/craft-floor.md`, then propose/apply within tokens.
- Ask once if two commands fit.

## Verification (Option B)

No detector CLI here, so verify by hand against `reference/craft-floor.md` Verify list plus the SVM policy §4.1 Visual Quality Bar, and by building (`npm run build` in `web/`). State what you checked and what you could not (e.g. visual render requires the user to run the dev server).
