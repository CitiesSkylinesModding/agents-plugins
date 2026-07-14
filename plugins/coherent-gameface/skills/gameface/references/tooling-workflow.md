# Gameface dev workflow: Player, DevTools, frameworks, polyfills, linters

Doc paths below are relative to `https://docs.coherent-labs.com/cpp-gameface/`.
The docs pin exact tool versions (React 18.2.0, Preact CLI v10, ...); read every pin as "verified at that version", and check the live page for current pins before scaffolding.

## The Player (`quick_start/player/player/`)

A standalone desktop app hosting Gameface, for developing UI without the game.
Load pages by drag-drop or `Player.exe --url=http://localhost:8080/` (dev servers with live reload work).
Useful flags: `--root <dir>` (sets the `coui://` root), `--width/--height` (default 1280x720), `--debugger-port` (default 9444, -1 disables), `--config Config.toml` (recent SDKs).
Shortcuts: F5 reload, F12 open DevTools in Chrome, F3 dirty-region overlay.
Pair with mock game data (see `scripting-data-binding.md`) to emulate the game side.

## DevTools (`content_development/devtools_js/`)

- The game (or Player) serves a Chrome DevTools endpoint when the integration enables the debugger (conventional port 9444).
  Connect from Google Chrome; every View is a target.
  The same endpoint is what the `game_*` MCP tools speak CDP to.
- Panel support grows per release and "anything not mentioned is not yet supported": Elements, Console, and Sources (JS debugging) are solid on desktop; the Performance tab is partial; the Network tab is partial; Data Binding tabs and the Models panel exist in newer SDKs only.
  A 1.x-era game exposes less than the current docs describe.
- After a page reload or navigation the Sources panel goes stale; refresh the DevTools window.
- Source maps work only when INLINED in the bundle (the inspector cannot fetch `coui://`).
- VS Code attaches as a Chrome debugger (`"type": "chrome"`, `"port": 9444`, `content_development/debuggingwithvscode/`).

## Frameworks

- **React** (`content_development/reactsupport/`): stock `react-dom` works, including `createRoot` and normal synthetic events.
  Blessed setups: Coherent's CRA forks (`react-scripts-cohtml`, `cra-template-cohtml`) or plain Webpack + Babel.
  Dev servers need the environment shims (postMessage, fetch).
  CSS Modules work via css-loader config; styled-components and React-JSS are unsupported.
  React DevTools works as the standalone app plus a script tag.
  For hot lists, the docs recommend hybrid rendering: React renders structure, `data-bind-*` attributes (emitted from JSX) handle per-item updates.
- **Preact** (`content_development/preactsupport/`): Preact CLI v10, "Simple" template only, build with `--no-prerender`, plus the same shims.
- **SolidJS** (`content_development/solidjssupport/`): Solid + Vite.
  Mandatory compiler flags in vite-plugin-solid: `omitLastClosingTag: false`, `omitNestedClosingTags: false`, `omitQuotes: false` (Gameface requires strictly valid HTML).
  Coherent's `vite-gameface` plugin fixes hydration markers and empty text nodes.
  The official Solid router is incompatible; Coherent's GamefaceUI component kit provides routing and widgets.
- **Svelte**: officially supported with Svelte 5 since 2.2.0, where whitespace nodes joined the DOM.
  On older engines (CS2's 1.64), fine-grained-reactivity frameworks that index into `childNodes` hit the shared-whitespace-node hazard (whitespace text nodes are absent from `childNodes`, so indices shift against browser expectations): probe carefully before committing to one.
  Known issue: reactive variables directly setting text content of SVG/HTML elements.
- **Tailwind** (`content_development/tailwindsupport/`): a per-utility compat table exists, but the root causes predict it: color utilities fail (CSS variables inside color functions, and `currentcolor`, are unsupported), `space-*`/`divide-*` fail (`:not()`), the grid category fails (`display: grid`), responsive `sm:`/`md:` prefixes fail (media-query form), `ring-*`/`shadow-*` fail (box-shadow variables).
  Spacing, sizing, flex, transforms, transitions, and gradient utilities work.
  Configure plain hex colors to reclaim the palette.
  Beyond Tailwind, the same root cause motivates pre-resolving color math at build time (SASS `color.change`/`color.adjust`) so the engine only ever sees literal colors.

## Polyfills and components

Official polyfills live in the SDK (`Samples/uiresources`, `content_development/pages_guides/js_polyfills/`) and replace unsupported native controls with custom elements: `<select>`, `<ul>`/`<ol>`, `<input type="radio">`, `<input type="range">`, plus separate forms, tabindex, and scrollbar libraries.
Two rules when using them:

- The polyfill REPLACES the element (`<select>` becomes `<custom-select>`), so references captured before init go stale; re-query after initialization.
- Tab order: `HTMLElement.tabIndex` (the property) does not exist; test with `el.hasAttribute('tabindex')`, and sequential Tab focus needs the tabindex polyfill.

Beyond polyfills, Coherent's open-source GameUIComponents library ships restylable game widgets (dropdown, slider, modal, grid, scrollable container, ...) plus an interaction manager (keyboard/gamepad spatial navigation, drag and drop).
Shadow DOM (1.61+) covers `customElements.define`, `<template>`, `attachShadow`, slots, `:host` (simple selectors), and `::slotted`; `::part` arrives in 1.67.

## Linters and type checking

- CSS: a Stylelint config with Gameface rules (prefixed `[GFP]`) flags unsupported CSS at lint time (`content_development/csslinting/`).
- HTML: an HTMLHint config validates `data-bind-*` syntax and model property paths against example model JSON files (`content_development/htmllint/`).
- TypeScript (`content_development/typescript/`): the SDK ships its own DOM typings (`cohtml.lib.dom.d.ts`, `cohtml.d.ts`); configure `lib` WITHOUT the standard `DOM` library so the compiler rejects APIs Gameface lacks, and let ESLint know about the `engine` global.
