# Version-gating: does the game have feature X?

The docs describe the latest Gameface only; a game's embedded Cohtml is frozen at ship time.
Gate every feature claim through this file, then probe the running game when one is connected.

<!-- timeline-ceiling: 3.0.3 -->

The timeline below is immutable history, current through **Gameface 3.0.3 (July 2026)**.
For claims about anything newer, fetch the live changelog (procedure below).
Repo-side, `mise skills:check-changelog` diffs this ceiling against the live changelog.

## Detect the game's version

- `game_status` reports the engine version: the CDP endpoint's `GET /json/version` answers with `Browser: "Cohtml/1.64.0.7"` style values.
- Page JS has no version global and no documented userAgent format, so feature-detect instead.
  Handy probes for `game_eval`:
  - `typeof ResizeObserver` defined: >= 1.47
  - `'attributeStyleMap' in HTMLElement.prototype`: >= 1.51 (it is NOT on `Element.prototype`)
  - `'append' in Element.prototype`: >= 1.56
  - `'attachShadow' in Element.prototype`: >= 1.61
  - whitespace text nodes present in `childNodes`: >= 2.2
- `CSS.supports` does not exist (the changelog never mentions it; the `CSS` global is the Typed OM unit factories, `CSS.px(4)` and friends).
  Feature-detect CSS with a style round-trip: `el.style.setProperty(prop, value)` then read it back; the parser rejects unsupported declarations, so they read back empty.
  The round-trip proves the parser accepts the declaration, not that rendering implements it; pair it with the changelog.
- Distinguish engine APIs from the game's own polyfills with `String(fn)`: engine-provided functions print `[native code]`, polyfills print JS source.
  CS2's bundle polyfills `window.postMessage`, so a bare `typeof` probe there measures the game, not the engine.

## The lookup procedure

1. Fetch the feature changelog at `changelog/feature/`: one page, every feature release from 1.0 to current, newest first, one `## Version N` heading per release.
2. Search the feature name and its synonyms across ALL row categories.
   Genuine web-platform additions hide under Enhancement and API rows, not only Feature rows.
   Skip engine-suffixed rows (FeatureUnity, EnhancementUnreal Engine) unless targeting that engine.
3. The heading above the first match is the introducing version.
   The game has the feature iff introducing version <= game version (dotted numeric comparison: 1.9 < 1.47 < 1.64 < 2.0 < 3.0).
4. Keep scanning newer releases: features ship partial and get completed, reworked, renamed, or removed later (`position: fixed` added 1.14 then redone properly in 1.56; custom elements V0 removed in 1.52.1; flex `gap` behind a flag in 2.0.0, real in 2.2.0).
5. A feature absent from the entire changelog almost certainly does not exist; confirm in the support tables.
6. JS language features are gated by V8, not the changelog (see the V8 section below).

LTS branches (`changelog/lts/`) are fixes-only continuations after a new major ships (1.69.2-1.69.8, 2.2.4-2.2.8); both changelog pages share the history below the branch point.
Per-release notes exist as separate pages only for >= 1.42 (`releases/release_<VERSION>/`).

## Milestones timeline

Web-platform focus; C++-only changes omitted.
Dates are release dates.

### 1.0 to 1.13 (Dec 2018 to Mar 2021)

- **1.1.0** (Feb 2019): CSS custom properties `var()`, `!important`, `calc()`, `::selection`, `:root`, multiple `background-image` values.
- **1.2.0** (Apr 2019): CSS transitions, `pointer-events`, CSS cursors, built-in copy/paste for selections and inputs (DOM clipboard events stay unfired), text selection on all elements.
- **1.2.2** (May 2019): `:active`, `visibility`, `data-bind-html`.
- **1.3.0** (Jun 2019): custom data-binding attributes, `engine.whenReady`, `touchmove`.
- **1.3.2** (Sep 2019): `change` event on input fields, `<pre>`.
- **1.4.1** (Nov 2019): `document.elementFromPoint`/`elementsFromPoint`, `text-overflow`, `white-space`, experimental dynamic data binding.
- **1.4.2** (Dec 2019): `async`/`defer` script attributes per standard.
- **1.6.1** (Feb 2020): WebSockets (still requires game-side transport wiring).
- **1.8.0** (Apr 2020): experimental inline text layout (later `cohinline`).
- **1.10.0** (Oct 2020): custom elements V1 upgrading; all HTML tags drawable/stylable.
- **1.12.0** (Nov 2020): `text-stroke`, complex-script text layout (Arabic and similar).
- **1.12.4** (Feb 2021): `font-weight` property, XHR `timeout`.
- **1.13.1** (Mar 2021): V8 8.8 on Windows, font preloading, embedded last-resort font.

### 1.14 to 1.40 (Apr 2021 to May 2023)

- **1.14.0** (Apr 2021): `position: fixed` (first attempt), HTML/CSS preloading APIs, single-frame page load, `customElements.whenDefined`.
- **1.14.1** (May 2021): `clip-path`, `rotate3d`/`translate3d`.
- **1.15.0** (Jun 2021): inline SVG.
- **1.17.0** (Sep 2021): all custom properties/attributes renamed with the `coh-` prefix.
- **1.18.2** (Oct 2021): `:first-child`/`:last-child`/`:only-child`/`:nth-child()` (structural pseudo-classes exist only from here), `backdrop-filter`, BigInt binding.
- **1.19.0** (Nov 2021): `::before`/`::after` (only from here).
- **1.20.0** (Dec 2021): canvas `measureText`, `innerHTML`/`outerHTML` getters.
- **1.22.1** (Jan 2022): Inspector Network panel.
- **1.24.1** (Feb 2022): `<form>` polyfill, JS sampling profiler in the Inspector.
- **1.25.1** (Mar 2022): binary WebSockets.
- **1.26.0** (Apr 2022): **V8 9.4** (the last recorded V8 bump), `unhandledrejection`, `image-rendering`, `caret-color`.
- **1.27.0** (May 2022): `DOMContentLoaded` (only from here), `animationstart`, `transitionstart`, `propertyName`/`animationName` on events.
- **1.29.2** (Jun 2022): CSS parser recovers after invalid rules.
- **1.34.0** (Nov 2022): window `error` event on V8 platforms.
- **1.34.2** (Nov 2022): `text-decoration` family, `text-underline-offset`/`-position` (text-decoration exists only from late 2022; as of 1.64, `text-decoration-style` accepts only `solid`).
- **1.35.0** (Dec 2022): dynamic `import()` and `import.meta.url`.
- **1.37.0** (Feb 2023): VS Code debugging, `movementX`/`movementY`, multi-argument `classList.add/remove`.
- **1.39.0** (Apr 2023): `animationiteration`/`animationcancel`/`transitionrun`/ `transitioncancel`.
- **1.40.0** (May 2023): base64 data-URI images.

### 1.42 to 1.64 (Jun 2023 to Mar 2025), the Cities: Skylines II range

- **1.42.0** (Jun 2023): SVG `<image>` element, linear-color rendering pipeline.
- **1.43.0** (Jul 2023): HTML `<template>` element, `CSSStyleDeclaration.removeProperty`.
- **1.44.0** (Aug 2023): `Element.remove()` (only from here); V8 on every platform.
- **1.45.0** (Sep 2023): `queueMicrotask`, conic gradients as background-image, Lottie.
- **1.47.0** (Nov 2023): `ResizeObserver`, modern space-separated `rgb()` syntax.
- **1.48.0** (Dec 2023): flex longhand properties.
- **1.49.0** (Dec 2023): WAAPI `finish`/`cancel`/`commitStyles`; stringified property values lose trailing zeros (breaks string comparisons).
- **1.50.0** (Feb 2024): `Touch`/`TouchEvent` constructors, screenshot capture via the DevTools protocol (`Page.captureScreenshot`), `backface-visibility`.
- **1.51.0** (Mar 2024): CSS Typed OM (`attributeStyleMap`), custom media features.
- **1.52.1** (Apr 2024): custom elements V0 API removed; live `HTMLCollection` for `getElementsByTagName/ClassName`.
- **1.54.0** (Jun 2024): `clientTop/Left/Width/Height` and `getClientRects()` (only from here).
- **1.55.0** (Jul 2024): async style solving (style resolving moved off Advance); animation end events fire one frame later.
- **1.56.0** (Aug 2024): `element.append()`, `position: fixed` proper support, CSS unloading.
- **1.57.0** (Sep 2024): media types `all`/`print`/`screen` tolerated in parsing (the docs still recommend feature-only queries), `$0`-`$4` in the Inspector console.
- **1.58.0** (Sep 2024): `auxclick`, Simple Opacity (`coh-simple-opacity`), boolean media queries and the `not` keyword.
- **1.60.0** (Nov 2024): at-rules obey cascade order (BREAKING for old stylesheets).
- **1.61.0** (Dec 2024): **Shadow DOM**, `<slot>`, `::slotted`, `:host`, COLRv0 color emoji, safe data binding.
- **1.63.0** (Jan 2025): `addEventListener` options objects (`{once, ...}`), COLRv1 emoji.
- **1.64.0** (Mar 2025): data-binding synchronization optimizations.
  **CS2 ships 1.64.0.7.**

### After 1.64 (absent from Cities: Skylines II)

- **1.65.0** (Apr 2025): inline ES6 modules (`<script type="module">` with inline body), `rem` units in SVG lengths.
- **1.67.0** (Jun 2025): WebP images, `::part`/`exportparts`, `CharacterData.before/after`, SVG `pathLength`.
- **1.68.0** (Jul 2025): blending UI with game content, `white-space: nowrap` grouping.
- **1.69.0** (Aug 2025): rewritten high-performance `backdrop-filter`.
- **2.0.0** (Oct 2025): flex `gap` behind the `--use-compatibility-yoga` flag, int64-to-BigInt binding modes, `setInterval` default delay 0, large `el.style` standardization pass.
- **2.2.0** (Dec 2025): **`aspect-ratio` CSS property**, new flex algorithm with real `gap`, `@starting-style` and discrete-property transitions (`display` animates), `vertical-align: baseline`, whitespace nodes join the DOM (BREAKING for `childNodes` counts), `<img>` keeps its natural aspect ratio by default (layout-changing), `color(srgb ...)` replaces `coh-scrgb`, Svelte 5 support.
- **3.0.0** (Apr 2026): **`box-sizing: content-box`** (before 3.0 everything is effectively border-box), auto margins in flex, web-standard flex mode, dynamic SDF text, compatibility-flags system.
- **3.0.2** (Jun 2026): numeric comparisons in custom media features.

## Never existed (as of the ceiling above)

CSS Grid, `:has()`, `:is()`/`:where()`, `fetch()`, `IntersectionObserver`, `position: sticky`, `requestIdleCallback`, Web Workers, `<dialog>`, `<iframe>`, `<audio>`, `contenteditable`.
Present since before 1.0 and safe at any version: `MutationObserver`, `XMLHttpRequest`, `history`, canvas 2D basics, CSS animations/keyframes.

## V8 (the JS language ceiling)

V8 9.4 since Cohtml 1.26 (Apr 2022); the changelog records no later bump through 3.0.3.
V8 9.4 is roughly ES2021: optional chaining, nullish coalescing, `WeakRef`, `Promise.any`, logical assignment, `String.replaceAll`, and `Array.at` all work.
Newer than 9.4 and therefore missing: `Array.findLast`, `structuredClone`, `Object.groupBy`, `Array.fromAsync`.
Console platforms historically ran other VMs; V8 runs everywhere since 1.44.

## Breaking changes worth knowing

- **1.17**: custom properties/attributes renamed with the `coh-` prefix.
- **1.49**: stringified property values lose trailing zeros.
- **1.52.1**: custom elements V0 API removed.
- **1.55**: style solving moved off Advance; animation end events fire one frame later.
- **1.60**: at-rules obey cascade order (old stylesheets relying on the bug break).
- **2.0.0**: `el.style` standardization (computed `animation-delay` becomes `0s`, `steps(1)` form, non-standard `percent` property removed, `style.prop = null` clears).
- **2.2.0**: whitespace nodes join the DOM (`childNodes` indices shift), BODY scroll redirects to HTML per standard, `<img>` natural aspect ratio by default, `coh-scrgb` replaced by `color(srgb ...)`.
- **3.0.0**: compatibility-flags system introduced (per-game legacy toggles, removed at the next major); several deprecated font and view APIs removed.

## Worked example: Cities: Skylines II

CS2 embeds **Cohtml 1.64.0.7** (confirm with `game_status`).
Everything at or below 1.64 applies: Shadow DOM (1.61), `addEventListener` options (1.63), CSS Typed OM (1.51), `ResizeObserver` (1.47), `<template>` (1.43), proper `position: fixed` (1.56), CDP screenshots (1.50).
Absent, so design around them: inline `<script type="module">` bodies (1.65), WebP (1.67), `::part`/`exportparts` (1.67), flex `gap` (2.0/2.2), the `aspect-ratio` property (2.2), `@starting-style` and discrete transitions (2.2), whitespace nodes in `childNodes` (2.2), `box-sizing: content-box` (3.0), and flex auto margins (3.0).
