---
name: gameface
description: "Coherent Gameface (Cohtml) domain knowledge for the game-UI middleware that renders HTML/CSS/JS inside games (Cities: Skylines II and many others). Use when writing or debugging UI that runs in a Gameface view, when HTML/CSS/JS behaves differently in the game than in a browser, when checking whether a web feature exists in a game's Cohtml version, or when working with data-bind-* attributes or the engine JS API. Also the domain reference for skills that drive a Gameface UI via the game_* tools."
---

# Gameface

Gameface renders game UI from HTML/CSS/JS.
It is powered by Cohtml (the HTML engine) and Renoir (the rendering engine), proprietary technology that is not WebKit, Chromium, or Gecko, and not a WebView.
JavaScript runs on V8.
Gameface implements a deliberate subset of HTML5/CSS3 chosen for game-UI performance: an unsupported HTML element is not an error, it lays out as a generic unstyled flex box with no semantics, and unsupported CSS is silently ignored.
(The older Coherent UI product was Chromium-based; search results about it do not describe Gameface.) This skill was written against docs v3.0.3.1 and verified against Cities: Skylines II (CS2), the worked example throughout.

## The map and the territory

The docs are the map; the running game is the territory.
Three rules keep them straight:

1. **The docs describe the latest Gameface only.** Support tables carry no "since version" annotations.
   A YES in today's docs is an upper bound, not a fact about your game.
2. **Version-gate every feature claim.** A game embeds a Cohtml version frozen at ship time (CS2: 1.64.0.7, while the docs describe 3.0.x).
   A feature exists in the game iff the changelog introduced it at or below the game's version.
   [references/version-gating.md](references/version-gating.md) has the lookup procedure, a baked version-to-feature timeline, and the list of features that never existed at all.
3. **Probe the territory.** Two games on the same Cohtml version can still differ: per-game compatibility flags and embedder choices gate complex-selector styling, WebSockets, localization, text-transform, and more.
   When a game is reachable, `game_status` reports the engine version (the CDP endpoint answers `Browser: "Cohtml/x.y.z"`), and `game_eval` settles support questions in one probe: `typeof ResizeObserver` for APIs, a style round-trip for CSS (`el.style.setProperty('gap', '4px')` then read it back; the parser rejects unsupported values, so they read back empty).
   `CSS.supports` does not exist (the `CSS` global is only unit factories like `CSS.px`).
   Page JS has no version global; feature-detect, never UA-sniff.

A support claim is settled only once it is version-gated, and probed when a game is connected.

Probing produces hard-won facts the docs lack: an undocumented quirk, a version boundary, a per-game flag setting.
Store engine facts you discovered yourself in your auto memory, tagged with the game and Cohtml version they were verified on; they outlive the session and are expensive to rediscover.

## Not a browser: the facts that bite first

Layout and styling:

- Every element defaults to `display: flex` (column direction) with `box-sizing: border-box`.
  `block` and `inline` are simulated with flex.
  `flex-shrink` defaults to 0.
  `<p>` and `<span>` default to flex row.
- `min-width`/`min-height: auto` means 0.
  Percentages on absolutely-positioned elements resolve against the direct parent, not the first positioned ancestor.
  `<html>` is 100vw by 100vh.
- Flexbox is the only layout system.
  CSS Grid, floats, table layout, list markers, `position: sticky`, and `display: inline`/`inline-block`/`contents` have never existed in any version.
- Native form controls stop at text/password inputs, textarea, and button-as-styled-element.
  `<select>`, radio, checkbox, range, `<form>`, and `<ul>`/`<ol>` rendering come from official polyfills; tables, lists, `<details>`/`<summary>`, `<progress>`/`<meter>`, and `<hr>` lay out as plain boxes.
  Overflow scrolls but draws no scrollbar; scrollbars are built or polyfilled.
- Stylesheet combinators (`>`, `+`, `~`, descendant space) only match when the game enables complex-selector styling (the per-game `EnableComplexCSSSelectorsStyling` flag): probe before relying on them.
  `:not()`, `::placeholder`, and `:nth-of-type()` are unsupported; `::before`/`::after` exist since 1.19.
- `user-select` defaults to `none`; text selection is opt-in.
- CSS variables work, except inside `@keyframes` and as `var()` fallback values.
  `calc()` cannot mix `%` with other units.
  `currentcolor` does not exist.
  Variables inside color functions are the root cause of most Tailwind utility failures.
- Media types do not exist (`screen` is implied); write feature-only queries: `@media (min-width: 1280px)`.

Timing (the game loop is the UI clock):

- Timers, requestAnimationFrame, CSS animations, and event dispatch tick inside the game's per-frame `View::Advance` call.
  Effective rAF rate equals the game frame rate; a game that stops advancing the view freezes page JS entirely.
- Layout runs once per frame.
  Geometry and styles read from JS are one frame stale; a node measured in the same tick it was inserted reports 0x0; `getComputedStyle` settles 2-3 frames after a change.
  Idiom: wrap reads in `requestAnimationFrame`, nested twice for computed styles.
- Hover state and `mouseenter/over/leave/out` only update when the game forwards mouse-move or scroll input.

JS and DOM:

- The global `engine` (defined by cohtml.js; gate startup on `engine.whenReady`) is the only JS-to-game bridge: `engine.call(name, ...)` returns a Promise from the single C++ handler, `engine.trigger`/`engine.on` are N-handler events, and models flow through `engine.createJSModel` / `engine.updateWholeModel` / `engine.synchronizeModels` plus `data-bind-*` attributes.
- `fetch()`, `IntersectionObserver`, Web Workers, iframes, `<audio>`, `<dialog>`, and `contenteditable` have never existed.
  XHR and `localStorage` exist (`sessionStorage` is absent), served by the game's resource layer (`coui://` scheme) rather than a network stack.
  WebSocket exists only when the game wired a socket transport.
  V8 is 9.4 (ES2021) since Cohtml 1.26, with no later bump recorded.
- `event.target`/`currentTarget` are null once the dispatch call stack unwinds.
  `DOMContentLoaded` exists only since 1.27; `load` fires after fonts load.
- `document.evaluate` (XPath), `createTreeWalker`, and `innerText` do not exist, and `document.title` is undefined (verified on CS2).
  Find elements by scanning `querySelectorAll` results and filtering on `textContent`.
- `HTMLElement.click()` does not exist, and `PointerEvent`/`InputEvent` constructors are missing.
  Simulate input by dispatching bubbling `MouseEvent`, `KeyboardEvent`, and `Event('input')` events.

## Looking things up

Docs base: `https://docs.coherent-labs.com/cpp-gameface/`.
Summarizing fetch tools (WebFetch and similar) fail on this site: every page front-loads hundreds of KB of minified navigation, so summarizers never reach the content.
Fetch pages with the shipped extractor instead (paths relative to this skill's directory):

```
node scripts/fetch-doc.mjs <url>       # page content as markdown, tables intact
node scripts/fetch-doc.mjs sitemap     # list every page URL of the docs site
```

Fallback without node: `curl -s <url>`, then read the HTML between `<main` and `</main>`.

Key pages (paths under the base):

| Page                                                                    | Path                                                                                                                                                |
| ----------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Differences to traditional browsers (prose, the quirks)                 | `what_is_gfp/htmlfeaturesupport/`                                                                                                                   |
| Supported-features hub                                                  | `content_development/supported_features_tables/`                                                                                                    |
| HTML / CSS properties / CSS selectors / JS events / canvas / SVG tables | `content_development/supported_features_tables/` + `htmlelements/`, `cssproperties/`, `cssselectors/`, `jsevents/`, `canvassupport/`, `svgsupport/` |
| Feature changelog (all versions, one page)                              | `changelog/feature/`                                                                                                                                |
| LTS changelog (post-branch fixes)                                       | `changelog/lts/`                                                                                                                                    |
| Content development (guides, tooling)                                   | `content_development/`                                                                                                                              |
| UI scripting (engine API, data binding)                                 | `integration/ui_scripting/`                                                                                                                         |

Reading the support tables:

1. Find the exact row.
   JS events repeat per specification: match name AND specification (`load` under DOM Level 3 is YES; `load` under XHR is not).
2. An empty status cell means unsupported.
   The tables never write NO, except the canvas page, which does.
3. PARTIAL, or any Notes cell, enumerates the exact supported subset; read it verbatim.
4. CSS rows carry an Animatable column (YES / NO / DISCRETE / empty).
   Selector rows use footnotes: `*` needs the complex-selectors flag, `**` warns about structural pseudo-class performance.
5. The "Differences to traditional browsers" prose page holds behavioral quirks the tables omit; check it when a supported feature still behaves oddly.

## Going deeper

- Version-gating a feature, "does the game have X", what changed between versions, the CS2 feature ceiling: [references/version-gating.md](references/version-gating.md).
- Layout modes, default-style deltas, media queries, UI scaling, fonts, text, emoji, filters, animation, SVG/GIF/Lottie/video: [references/layout-styling-text.md](references/layout-styling-text.md).
- The `engine` API surface, `data-bind-*` vocabulary, model lifecycle, mock game data, simulating input, DOM/JS quirks: [references/scripting-data-binding.md](references/scripting-data-binding.md).
- UI is slow, janky, or memory-hungry; the frame pipeline; profiling with DevTools: [references/performance.md](references/performance.md).
- Dev workflow: the Player, DevTools panels, React/Preact/Solid/Svelte/Tailwind, polyfills, linters, TypeScript typings: [references/tooling-workflow.md](references/tooling-workflow.md).
