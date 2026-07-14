# Layout, styling, text, and media in Gameface

Doc paths below are relative to `https://docs.coherent-labs.com/cpp-gameface/`.
Fetch them with the skill's `scripts/fetch-doc.mjs`.
Version gates like "(2.2+)" mean: check the game's Cohtml version before relying on the feature (see `version-gating.md`).

## Layout modes

Gameface has one real layout engine (flexbox) plus two opt-in modes:

1. **Flex (default).** Every element is a flex container; `display: block` and `inline` run the flex algorithm with adjusted direction.
   Styled text runs become text boxes laid out as flex-row with wrapping, so mixed text/element content wraps between boxes, not between words.
2. **`display: simple`** (non-standard performance mode, `content_development/simple_layout/`).
   Activates only when set on `<body>`.
   Rules: elements need explicit `top`/`left` (static elements sit at the parent content position, siblings never offset each other), margins are ignored, `right`/`bottom` and `min/max-width/height` are unsupported, and all `flex-*` and alignment properties are ignored.
   A `display: flex` child starts a normal flex subtree; `display: simple` inside a flex subtree is ignored.
3. **`cohinline`** (non-standard attribute on `<p>`, `content_development/inlinelayout/`).
   Browser-like paragraph layout where text plus inline elements wrap between words.
   Gaps: `text-align: justify` and `text-overflow: ellipsis` inoperative; box decorations (background/border/mask) on child elements unsupported; `aspect-ratio` unsupported inside.
   `vertical-align` works only here (`baseline`, `text-top`, `middle`, `text-bottom`); all baseline content shares one baseline from the line's largest font; images/SVGs align to the line-box bottom by default.
   Vertical centering pattern: set the `<p>` line-height equal to the child height.

## Default styles that differ from browsers

- Every element: `display: flex` (column) and `box-sizing: border-box`.
- `flex-shrink: 0` (browsers use 1).
  `<p>` and `<span>`: flex row.
- `min-width`/`min-height: auto` resolves to 0.
- `%` on absolutely-positioned elements resolves against the direct parent.
- `<html>` is `100vw` by `100vh`.
  `<body>` has a default margin.
- Less common elements (`h1`-`h6`, `textarea`, `blockquote`, `figure`, ...) may lack default styles entirely; copy them from the WHATWG rendering spec when parity matters.
- A missing `<!DOCTYPE html>` triggers additional legacy differences; always include it.
- To make a browser render like Gameface for comparison: `* { display: flex; box-sizing: border-box; flex-shrink: 0; min-width: 0; min-height: 0; }`.

Notable property limits (the current tables have the full lists):

- `border-style`: only `solid`/`none`/`hidden`.
  `position`: no `sticky`.
  `overflow` clips absolutely-positioned children of overflowing containers (unlike browsers).
- `justify-content` has no `space-evenly`; `align-items`/`align-content` support only `stretch`/`flex-start`/`flex-end`/`center`; `order` and `flex-flow` are unsupported.
- `flex-basis: content` is unsupported, and `flex-basis: auto` next to a sibling with a percentage basis may resolve as `content`: give siblings all-percentage bases, or avoid mixing `%` with `auto`.
- `font-size` units are limited to `px`/`em`/`rem`/`vw`/`vh`; `white-space` supports only `normal`/`nowrap`/`pre`/`pre-wrap`; `word-break`, `word-wrap`, `writing-mode`, `direction`, and `tab-size` are unsupported; `max-width`/`max-height` reject `none`; `visibility: collapse` is unsupported; named colors are a limited palette (prefer hex or rgb).
- The `skew(x, y)` two-argument form is unsupported (`skewX`/`skewY` work); `transform-origin` has no z-offset; `mask-image` takes a single PNG with alpha; `clip-path` takes basic shapes only.
- Wholly unsupported families: `list-style-*`, multi-column `column-*`, table layout properties, CSS counters and `quotes`, `outline`, `will-change`, `scroll-behavior`, `touch-action`, `resize`, `zoom`.
- `object-fit` does not exist: for cover-fit images use a `<div>` with `background-size: cover`.
  Flex `gap` (2.2+) on older engines: space children with margins.
  Percentage `width`/`height` on inline images are unsupported: size the container instead.

Layout-bug workarounds (undocumented, observed on 1.64):

- Animating `width`/`height` from 0 to `auto` fails: transition `max-width`/`max-height` instead.
- An image refusing to stretch to its flex container's height: set an explicit `height` on the container.
- A `<span>` randomly collapsing: `white-space: nowrap`.
- A text node breaking across two lines unexpectedly: force `display: flex` on it.
- Misbehaving flex configurations sometimes resolve with `align-items: flex-start`.

## Reading geometry and styles from JS

Layout is deferred and runs once per frame (`content_development/immediatelayout/`, `content_development/accessingcomputedstyles/`):

- Layout getters (`getBoundingClientRect`, `offsetWidth`, ...) return the previous frame's values; a node measured in the tick it was inserted reports 0x0.
- `getComputedStyle` settles 2-3 frames after a change; nest `requestAnimationFrame` calls before reading.
- `ResizeObserver` reports with up to 2 frames of delay.
- Opt-in fix: Immediate Layout.
  `engine.executeImmediateLayoutSync()` runs one layout on demand (batch DOM writes, call it once, then read many getters).
  `engine.enableImmediateLayout` makes every getter trigger layout (expensive).
  Availability is per-view and game-configurable; probe before relying on it.

## Media queries (`content_development/mediaqueries/`)

- Media types do not exist; `screen` is implied.
  `@media screen and (...)` traditionally invalidates the whole query (tolerated in parsing since 1.57); write feature-only queries: `@media (min-width: 1280px)`.
- Supported features: `min/max-width`, `min/max-height`, `aspect-ratio` variants, `orientation`, with `and` chaining.
  Queries evaluate against the Cohtml View's size.
- Bug: nested `@media` blocks must sit at the END of the stylesheet, or later plain rules (like `@keyframes`) fail to override rules inside them.
- `<link media="...">` gates rulesets only; `@font-face` and `@keyframes` inside always apply.
  Changing the `media` attribute from JS after creation has no effect.
- Custom media features (1.51+): the game defines flags via `View::SetCustomMediaFeature`, CSS queries them as `@media (myFeature: myValue)`; numeric comparisons since 3.0.2.
  Toggling one triggers a full style recalculation.

## Scaling the UI (`content_development/scalableui/`)

The documented pattern is rem-based: size everything in `rem`, set a base `html { font-size: 10px }`, and on `window.resize` set `html.style.fontSize = 10 * (window.innerWidth / designWidth) + 'px'`.
`vw`/`vh` units and `calc()` with them work; `%` widths inside flex rows build proportional grids; `px` min-sizes combine as floors.
`window.innerWidth/innerHeight` and the `resize` event work.

## Fonts (`content_development/fonts_frontend/`)

- Formats: `.ttf`/`.otf` plus collections (WOFF/WOFF2 do not load: convert to ttf/otf); loaded via CSS `@font-face` or registered by the game.
  Bitmap and MSDF atlas fonts are game-registered.
  The default font when `font-family` is unspecified is game-configured.
- The `load` event fires AFTER fonts load (no FOUT-then-swap).
- Fallback: per-CHARACTER across the `font-family` list; nearest weight within a family; family match beats weight match.
  Italic must be registered explicitly (no style fallback).
  `serif`/`sans-serif`/`monospace` map to one game-configured family each.
- Rendering is single-channel SDF above 10px (loses fine detail, rounds sharp corners); `coh-font-sdf: off` in `@font-face` rasterizes directly, but text stroke requires SDF.
- Auto-fit custom properties: `coh-font-fit-mode: none|fit|shrink`, `coh-font-fit-min-size` (floor 6px), `coh-font-fit-max-size`, shorthand `coh-font-fit` (1.55+).
  Useful for localized text in fixed containers.
- `text-transform` delegates case mapping to the game; without game-side support it does nothing.
  Probe per title.
- Glyph trails when text moves or hides: container width sums glyph boxes, so glyphs drawn outside their box (kerning tricks, line-height ratios below 1) leave artifacts.
  Fixes: `padding-right: 0.1em` or `overflow: hidden`, and ratio line-heights (~1.25).
- Emoji (1.61+): COLRv0 and COLRv1 (1.63+) color fonts as fallback families.
  SVG-in-font glyphs are unsupported; convert with nanoemoji.

## Text behavior

- Text is non-selectable by default (`user-select: none`); opt in with `user-select: text`.
  The Selection API is minimal: `setBaseAndExtent`, `empty`, `toString`, `document.caretPositionFromPoint`.
  Select-all/copy-paste UX is hand-rolled in JS.
- `<br>` works only inside a text run; between tags it is deliberately disabled.
- `<pre>` (1.3.2+) carries a real UA default of `white-space: pre` and preserves its text verbatim; unknown elements default to `white-space: normal`.
- `text-overflow: ellipsis` requires the ellipsis character in the loaded fonts and works for generic text only (input fields excluded).

## Filters and effects (`what_is_gfp/htmlfeaturesupport/`)

- Multiple `filter` functions do NOT apply sequentially: they combine into one color matrix applied in a single pass, so `brightness(2) brightness(0.5)` is identity (browsers clamp between passes).
  Intentional, for single-pass performance.
- Custom filters: `coh-color-matrix(20 numbers)` (animatable 4x5 matrix) and `coh-axis-blur(xpx ypx)` (per-axis blur, animatable).
- `filter: url(#svg-filter)` is unsupported; standard filter functions work.
- `backdrop-filter` exists since 1.18 (rewritten for performance in 1.69); `mix-blend-mode` includes the non-standard `additive` and may need `isolation: isolate` on the intended backdrop.
- `backface-visibility` evaluates per element, not per subtree; use `backface-visibility: inherit` on descendants to hide a subtree.
- A delayed CSS animation creates its stacking context only when it starts running (browsers create it immediately), so z-order can differ until the delay elapses.

## Animation (`content_development/animations/`)

- Preference order: CSS animations/transitions (evaluated in C++, fastest), then rAF-driven JS, then libraries.
  CSS `@keyframes` and all `animation-*` properties work.
- A transition triggered before the element's first style commit does not run, the value jumps (as in browsers, but the commit is frame-paced here): insert the element, wait a frame, then flip the property.
- CSS variables and `calc()` do not work inside `@keyframes`.
- Web Animations API is a control-only subset: `getAnimations()`, `play`, `pause`, `currentTime`, plus the Gameface-only `playFromTo(startMs, pauseMs)`.
  `element.animate()` does not exist.
- `transition-behavior: allow-discrete` (2.2+) covers only `display`, `visibility`, `content`, `cursor`, `pointer-events`.
  `@starting-style` (2.2+) supports the top-level at-rule form only.
- Verified libraries (`content_development/animationlibrariessupport/`): GSAP core, Anime.js (always give units; SVG animation via Anime.js unsupported), Framer Motion, Lottie light.
  All are subject to the stale `getComputedStyle` lag.
- `aspect-ratio` (2.2+): keep constraint units consistent (mixing `%` with lengths breaks the ratio); works for non-replaced elements, raster images, and SVGs.

## SVG (`content_development/supported_features_tables/svgsupport/`)

Subset of SVG 2 usable inline, as `<img>`, as `background-image`, and as `border-image-source`.
Constraints: `<tspan>` is ignored (single-line `<text>` only), and `<foreignObject>`, `<marker>`, `<pattern>`, `vector-effect`, SMIL animation, SVG `<filter>`, scripting, and `<a>` links are unsupported (CSS/Web animations of SVG work).
Containers with inline SVG children need concrete sizes (auto-sizing is unsupported).
`mask` and `clip-path` cannot combine on one SVG element (clip-path wins).
Inline SVG `<style>` tags style only their SVG and stay out of `document.styleSheets`.
SVG presentation attributes are NOT settable via CSS properties (`fill`, `stroke`, and friends as CSS are unsupported); set them as attributes.
When animating duplicated SVG-attribute/CSS properties in keyframes, include units even where the attribute form is unitless, and avoid arc path commands in interpolated paths (arcs convert to Beziers and command sequences must match).

## Canvas (`content_development/supported_features_tables/canvassupport/`)

2D context only (`getContext('2d')`; no WebGL, no context attributes).
Missing from the 2D API: `getImageData`/`putImageData`/`createImageData`, `toDataURL`/`toBlob`, `clip()`, `setLineDash`, `roundRect`, `isPointInPath`, shadows, `filter`, `imageSmoothing*`, conic gradients, and the multi-origin radial gradient form (the single-origin form works).
`globalCompositeOperation` supports `source-over` only; linear gradients apply to `fillRect()`/`fill()`/`stroke()` but not to `strokeRect()`/`fillText()`/`strokeText()`; `measureText` returns `width` and the four `actualBoundingBox*` metrics only.
The canvas support page is the one table that writes explicit NO statuses.

## Images and media

- GIF (`content_development/gifsupport/`): `<img>` only, fully decoded up front; keep at or under 128x128 and 30 frames.
  Unsupported in `background-image`, `border-image`, and `mask-image`, with no plans to add it.
  Prefer sprite sheets or video.
- Lottie (1.45+, `content_development/lottie/`): Lottie Light with the SVG renderer only; call `animation.setSubframe(false)`. dotLottie files, expressions, effects, webp-embedded images, and multiline text are unsupported.
  The container needs a concrete size.
- Video (`integration/optional_features/videosupport/`): WebM with VP8/VP9 video and Vorbis audio only; no controls UI (build one); audio PCM is handed to the game to play.
  Gameface extensions: `cohFastSeek`, `cohGetKeyframeTimestamps()`, `cohPrebufferKeyframe(t)`, and the `cohplaybackstalled`/`cohplaybackresumed` events.
  Transparent video (alpha channel, `yuva420p`) works for overlay and particle effects.
- Base64 data-URI images work since 1.40.
- Supported image formats include DDS, TGA, PNG, JPEG, BMP, PSD, ASTC, PKM, KTX; WebP only since 1.67.
