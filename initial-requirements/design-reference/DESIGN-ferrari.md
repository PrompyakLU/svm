---
version: alpha
name: Ferrari Racing
description: A high-contrast, performance-led editorial system with minimal chrome and dramatic hero imagery.
colors:
  primary: "#000000"
  secondary: "#8F8F8F"
  tertiary: "#E5E7EB"
  neutral: "#FFFFFF"
  surface: "#FFFFFF"
  on-surface: "#000000"
  background: "#FFFFFF"
  text: "#000000"
  accent: "#000000"
  border: "#E5E7EB"
  muted: "#F5F5F5"
typography:
  headline-display:
    fontFamily: FerrariSans
    fontSize: 32px
    fontWeight: 700
    lineHeight: 38px
    letterSpacing: 0px
  headline-lg:
    fontFamily: FerrariSans
    fontSize: 27px
    fontWeight: 700
    lineHeight: 32px
    letterSpacing: 0px
  headline-md:
    fontFamily: FerrariSans
    fontSize: 23px
    fontWeight: 500
    lineHeight: 28px
    letterSpacing: 0.08px
  headline-sm:
    fontFamily: FerrariSans
    fontSize: 19px
    fontWeight: 500
    lineHeight: 23px
    letterSpacing: 0px
  body-lg:
    fontFamily: FerrariSans
    fontSize: 16px
    fontWeight: 400
    lineHeight: 24px
    letterSpacing: 0.195px
  body-md:
    fontFamily: FerrariSans
    fontSize: 14px
    fontWeight: 400
    lineHeight: 21px
    letterSpacing: 0.12px
  body-sm:
    fontFamily: FerrariSans
    fontSize: 13px
    fontWeight: 400
    lineHeight: 18px
    letterSpacing: 0.12px
  label-lg:
    fontFamily: FerrariSans
    fontSize: 16px
    fontWeight: 400
    lineHeight: 24px
    letterSpacing: 0.195px
  label-md:
    fontFamily: FerrariSans
    fontSize: 13px
    fontWeight: 400
    lineHeight: 18px
    letterSpacing: 0.12px
  label-sm:
    fontFamily: FerrariSans
    fontSize: 12px
    fontWeight: 400
    lineHeight: 16px
    letterSpacing: 0.1em
  caption:
    fontFamily: FerrariSans
    fontSize: 11px
    fontWeight: 400
    lineHeight: 14px
    letterSpacing: 0.08em
rounded:
  none: 0px
  sm: 4px
  md: 8px
  lg: 32px
  xl: 9999px
  full: 9999px
spacing:
  xs: 6px
  sm: 14px
  md: 38px
  lg: 48px
  xl: 58px
  gutter: 24px
  section: 64px
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.neutral}"
    typography: "{typography.label-md}"
    rounded: "{rounded.lg}"
    padding: "8px 16px"
    height: "47px"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.neutral}"
    typography: "{typography.label-md}"
    rounded: "{rounded.lg}"
    padding: "8px 16px"
    height: "47px"
  button-tertiary:
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    typography: "{typography.label-md}"
    rounded: "{rounded.none}"
    padding: "0px"
    height: "auto"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.md}"
    padding: "16px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    rounded: "{rounded.md}"
    padding: "12px 16px"
  chip:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.on-surface}"
    typography: "{typography.label-sm}"
    rounded: "{rounded.full}"
    padding: "6px 12px"
---

# Ferrari Racing

## Overview
Ferrari’s interface feels premium, fast, and intentionally restrained. The brand leans on stark contrast, large hero imagery, and minimal UI chrome to keep attention on the cars and racing story. It is aspirational and editorial rather than playful, with a dense visual opening balanced by clean navigation and sparse text overlays.

## Colors
- **Primary (#000000):** Deep black used for navigation, text, button fills, and the brand’s strongest contrast moments. It communicates speed, precision, and luxury.
- **Secondary (#8F8F8F):** A muted gray used for softer borders and secondary emphasis when the interface needs to step back from pure black.
- **Tertiary (#E5E7EB):** A light gray used for subtle borders and surface definition without adding visual noise.
- **Neutral (#FFFFFF):** Bright white used for text on dark imagery, button text, and clean surface areas.
- **Surface (#FFFFFF):** The dominant page and card surface color, keeping the system crisp and gallery-like.
- **On-surface (#000000):** Primary content color on light backgrounds, ensuring sharp readability.
- **Background (#FFFFFF):** The page foundation is white, which supports the brand’s high-contrast black styling.
- **Text (#000000):** Default reading color for strong editorial hierarchy.
- **Border (#E5E7EB):** Soft structural divider for cards and low-emphasis UI boundaries.
- **Muted (#F5F5F5):** A quiet neutral for chips, subtle blocks, and low-priority supporting surfaces.

## Typography
Ferrari Sans is the sole type family, giving the system a unified, engineered voice. Headlines use bold weights at compact sizes, which keeps them forceful without feeling oversized. Body copy remains regular-weight and highly legible, while labels stay clean and understated.

The hierarchy is tight and practical:
- `headline-display`, `headline-lg`, and `headline-md` carry the main editorial and hero messaging.
- `headline-sm` is reserved for section titles or smaller content blocks.
- `body-lg`, `body-md`, and `body-sm` support descriptive content and navigation context.
- `label-lg`, `label-md`, `label-sm`, and `caption` are for controls, nav items, and microcopy.

Letter spacing is minimal overall, with slightly expanded spacing in small labels and captions to reflect the refined, technical tone. Uppercase navigation styling appears in the source and should remain restrained and consistent, especially in top-level site navigation and compact UI labels.

## Layout
The layout feels full-bleed and image-led, with a strong preference for edge-to-edge presentation in hero areas. Navigation is fixed at the top, while content overlays sit centered over photography with generous negative space around key calls to action. Spacing is disciplined and rhythmic, using compact increments for UI controls and much larger section intervals for editorial breathing room.

Use the spacing scale as a clear hierarchy:
- `xs` and `sm` for fine internal gaps and compact UI grouping.
- `md` for mid-level separation between related blocks.
- `lg` and `xl` for section spacing and significant compositional breaks.
- `section` for broad page transitions and major content changes.

Cards and light content panels should stay compact, with 16px internal padding and subtle boundaries rather than heavy framing.

## Elevation & Depth
The system is intentionally flat. Instead of shadows and layered elevation, hierarchy comes from contrast, typography scale, and image treatment. Surfaces are separated with light borders and tonal shifts, while overlaid text relies on the darkness of the hero imagery for readability.

When depth is needed, keep it minimal:
- Use borders and contrast before shadows.
- Avoid glossy effects, gradients, and heavy blur.
- Reserve any shadow-like treatment for special media containers only, and even then keep it understated.

## Shapes
The shape language is streamlined and high-performance. Interactive controls use a rounded pill form, especially buttons, which softens the otherwise sharp, mechanical composition. Cards and content containers are subtly rounded with an 8px radius to stay modern without looking playful.

Overall, the shape system should feel aerodynamic rather than bubbly:
- `rounded.lg` and `rounded.full` for buttons and chips.
- `rounded.md` for cards and inputs.
- `rounded.none` for text links and utility elements.

## Components
### Buttons
Primary buttons use a solid black fill with white text, rounded pill corners, and medium padding. They should feel decisive and immediate, matching the brand’s racing intensity. Secondary buttons are transparent with a light outline and white text for use over dark imagery. Tertiary buttons behave like text links: no border, no fill, minimal padding, and black text on light surfaces.

- Primary: `button-primary` for the most important action.
- Secondary: `button-secondary` for alternate actions on dark media.
- Tertiary: `button-tertiary` for low-emphasis actions and inline navigation.

Buttons should stay approximately 47px tall, with 8px vertical and 16px horizontal padding.

### Cards
Cards are clean white containers with a 1px light-gray border, 8px radius, and 16px padding. They should not rely on shadows for separation. Use them for grouped content, feature blocks, or supporting editorial modules.

### Inputs
Inputs should match the card language: white background, subtle border, 8px radius, and comfortable internal padding. Keep them visually quiet and avoid aggressive focus styling; use contrast and border color changes for state communication.

### Chips
Chips are small pill-shaped labels with muted fills and compact padding. They work well for tags, filters, and status markers. They should remain understated so they do not compete with photography or headline text.

### Navigation and Links
Top navigation uses uppercase styling, generous letter spacing, and compact horizontal distribution. Links should be lean and direct, with little to no decoration except where a true text-link treatment is needed. The brand favors utility over ornament here.

### Media Overlays
Overlay copy on imagery should be centered, high contrast, and sparse. Use small labels above strong headlines, followed by a single clear action. Pagination dots and subtle controls should remain minimal and never overpower the hero asset.

## Do's and Don'ts
- Do keep the interface high-contrast and editorial, with the photography as the primary visual event.
- Do use Ferrari Sans consistently across headings, body, and controls.
- Do preserve the pill-button feel for major actions and keep padding compact.
- Do rely on typography scale and spacing to create hierarchy instead of shadow-heavy elevation.
- Don't introduce colorful accents that compete with the Ferrari black-and-white system.
- Don't add large border radii to cards, inputs, or media containers.
- Don't clutter hero sections with multiple CTAs or dense copy blocks.
- Don't use playful, soft, or rounded visual language that weakens the performance-driven tone.