---
inclusion: always
---

# SVM Design System

This is the authoritative product design direction for SVM. It supersedes earlier dark SOC and Ferrari directions, which remain historical decisions. `web/src/styles.css` is the runtime source of truth for implemented token values; this document records the approved design and token contract.

## Approved direction

- Default to a **white light theme**: teal brand `#087F8C`, cool gray page `#F5F7FA`, navy text `#172B4D`, white surfaces.
- Use a collapsible white left sidebar: 240px expanded and 76px collapsed. Preserve the seven existing product routes: Dashboard, Vulnerabilities (list and detail), Intake, Remediation, Verification, Admin, and Audit. On mobile, use an accessible navigation drawer.
- Keep the interface calm and operational: fine borders, 12px corner radius, subtle shadow, clear hierarchy, and meaningful keyboard focus.
- Communicate status with text and icon as well as color. Give Critical, overdue, and KEV work the strongest visual priority.
- Use a centered content region with maximum width 1600px.

## Dashboard surface

Use the [dashboard surface contract](../../docs/ui/dashboard.md) for dashboard composition, data behavior, and interactions. Consult it when changing the dashboard; keep its detailed rules there.

## Authentication surface

Use the [authentication UI contract](../../docs/ui/authentication.md) for login layout, validation, loading and error states, demo labeling, and safe return navigation. The current mock session is browser-only; backend AuthModule and real-API integration remain pending, so the login surface must not imply server-enforced security.

## Token map

- Surface tokens: `--color-bg-base: #F5F7FA`, `--color-bg-surface: #FFFFFF`, `--color-bg-border: #DCE3EC`.
- Text tokens: `--color-fg-primary: #172B4D`, `--color-fg-secondary: #4B5E78`, `--color-fg-muted: #5B6B7E`.
- Brand tokens: `--color-brand-primary: #087F8C`, `--color-brand-soft: #E3F4F5`.
- Risk text and soft pairs: Critical `#B42318` / `#FEF3F2`; High `#A63F00` / `#FFF4E5`; Medium `#805B00` / `#FFF8DB`; Low `#18794E` / `#ECFDF3`; Info `#1D4ED8` / `#EFF6FF`. Keep status labels and icons alongside these colors.
- Typography: `--font-sans` is self-hosted Noto Sans Thai for Thai and Latin; `--fw-{regular,medium,bold}` are 400/500/700; body line-height is 1.6; use tabular numerals. A monospace face may be used for identifiers such as CVEs.
- Shape: `--radius-sm: 6px`, `--radius-md: 12px`, `--radius-lg: 12px`, `--radius-full: 9999px`.
- Frame: `--sidebar-width: 240px`, `--sidebar-collapsed-width: 76px`, `--container-max: 1600px`, `--container-pad: 24px`.
- Spacing: use the `--space-*` scale (4/8/16/24/32px); elevation uses `--shadow-{sm,md,lg}` from the runtime stylesheet.

## Accessibility

- Preserve keyboard access, visible focus, readable contrast, and status meaning that does not depend on color alone. Mobile navigation must be an accessible drawer.
- Use the existing self-hosted `@fontsource/noto-sans-thai` package; do not load fonts from an external CDN.

## Dark theme (approved opt-in)

The user approved a complete dark theme on 2026-10-21, so a theme toggle is now shown (satisfying the "complete alternative theme" condition below). Light remains the product default; dark is opt-in, remembered per browser (`localStorage` key `svm-theme`), and falls back to the OS `prefers-color-scheme` on first visit.

- Implemented as a full color-token override under `:root[data-theme="dark"]` in `web/src/styles.css`, toggled by `ThemeService` (`web/src/app/core/theme/theme.service.ts`) via the topbar toggle. Only color + shadow tokens change; typography, shape, spacing, and motion are shared with light.
- Every dark text/surface pair was verified against WCAG 2.2 AA (worst ratio 4.52). Risk colors are lightened for dark surfaces (Critical `#F87171`, High `#FBBF24`, Medium `#FCD34D`, Low `#4ADE80`, Info `#60A5FA`); soft fills become low-alpha dark tints. Status meaning still pairs color with icon + label.
- Dark token values (surfaces `#0F1623` base / `#1A2332` surface / `#222D3D` raised; text `#E6EBF2` / `#AAB6C6` / `#8A99AD`; brand `#2BB7C6`) live in `styles.css` as the runtime source of truth; this section records the approved direction.

## Scope and implementation guidance

- The approved default is light; dark is an approved opt-in alternative (see above). The toggle is shown because a complete dark theme now exists.
- Keep implemented token values in `web/src/styles.css`; update this document when the approved design direction changes.
- Treat the earlier Ferrari brief and dark SOC decision as superseded history, not current requirements. The user's approval of this light redesign authorizes the change in direction.
