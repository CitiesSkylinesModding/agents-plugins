---
name: gameface-driving
description: 'Operating manual for driving a live Gameface UI with the game_* MCP tools: procedures and traps verified on a real game. Load before first use of the input tools (game_click, game_fill, game_type, game_hover) or of any game_debug_* tool. Also use when verifying UI changes against a running game, when waiting for a mod rebuild to go live in-game, or when a game_* call fails or returns puzzling results. Engine support questions (does this CSS/JS/layout feature exist) belong to the gameface skill.'
---

# Driving a Gameface UI

This skill records field-verified procedure for the `game_*` tools: the facts the tool schemas cannot tell you.
Everything here was verified live against Cities: Skylines II (CS2, Cohtml 1.64.0.7); facts that may be game-specific are labeled.
For what the engine itself supports (layout, events, missing platform APIs), load the `gameface` skill; this one stays operational.

## Session start and triage

Screenshot first (jpeg, quality around 60) and orient before acting: menus, dialogs, or a loading screen change what is safe to click.
When any tool fails, run `game_status` before retrying; it settles whether the endpoint is reachable and which engine and page answered.
Read the page identity from `target.url` (for example `assetdb://gameui/index.html`).
A dead endpoint mid-session usually means the game crashed or was closed.
Report it and wait for the developer to relaunch the game; retry-looping cannot help, and testing that was interrupted mid-action may have left the UI in a state worth re-checking with a screenshot once the game is back.
No reconnect ritual exists or is needed: the server re-resolves the page target on the next call after the game returns.

## Finding elements

Text is a durable anchor, and `game_find` is the built-in text search: it scans `querySelectorAll` matches and filters on trimmed `textContent`, taking a `text` plus a `match` mode (`equals` / `contains` / `regex`, case-insensitive by default) and an optional `selector` to scope the scan.
It returns tag, id, classes, and rect per match, plus three counts that cascade: `unprunedTotal` (raw text matches), `total` (after `deepest` pruning), and `returned` (after `limit`); `total` above `returned` means the `limit` truncated, so narrow the query, while `unprunedTotal` above `total` just shows how many ancestors `deepest` pruned.
By default it keeps only the innermost match (`deepest`): an element's `textContent` includes its descendants', so a panel and its title button both match the title, and `deepest` prunes the panel; pass `deepest: false` to get the full ancestor chain when you want the enclosing container (finding a panel by its title).
Set `tag: true` and `game_find` stamps each match with a `data-gf-find` handle and returns ready-to-use `[data-gf-find="N"]` selectors for `game_click` / `game_hover` / `game_screenshot`, which closes the discovery-to-action gap when class names are build-hashed and no unique selector exists.
Tagging clears every prior `data-gf-find` first, so handles from an earlier `game_find` die on the next tagging call (and on any view reload); re-tag rather than reusing a stale handle.
For a predicate `game_find` cannot express (matching on an attribute, a sibling relation, or computed state), scan manually from `game_eval`: `[...document.querySelectorAll('button')].find(el => ...)`, then tag the node with `el.setAttribute('data-probe', '1')` and target `[data-probe]` when you need a unique selector, removing it after.
There is no XPath, no TreeWalker, and no `innerText` to lean on (engine gaps; details in the `gameface` skill).
In the JS query APIs, combinators, `:nth-child`, and `[attr*=]` all match, but `:not()` and `:first-of-type` throw "Invalid CSS selector" (verified on CS2); a selector-taking tool erroring that way needs a rewritten selector, not a retry.

## Act, then verify

Input calls report that events were dispatched, not that the UI reacted; confirm the effect you care about before building on it.
The cheap confirmations: `game_wait` on a predicate or selector, `game_dom` on the region that should have changed, a clipped screenshot, and `game_console` for exceptions a silent failure left behind.
`game_click` returns after dispatching the event sequence, before any async handler work; pair it with a wait on the expected outcome.
`game_hover` fires the JS hover handlers but never sets the CSS `:hover` state, which only real game-forwarded mouse input can set; verify a hover by its DOM effect, never by styling.

## The dev loop: rebuild to live

Gameface ships no file watcher (its docs' "Live Reload" page is a webpack-dev-server recipe for pages served from a dev server), so how a UI reloads is per-application wiring: an application-side file watcher calling the native view reload, a dev-server client or key handler calling `location.reload()` from inside the page, or nothing at all.
Hot reload is typically a developer-mode feature; when a rebuild produces no reload, suspect the gate (a launch flag, how the UI was installed) before suspecting the build, and `game_eval` of `location.reload()` is the manual fallback.
However triggered, a reload is a full view reload: the JS context resets (all globals wiped), the document rebuilds, and every script re-parses.
The CDP connection survives the reload transparently, and an in-flight `game_wait` keeps polling across the context reset and can resolve on the other side.
To detect "my new code is live" without editing the source: plant `globalThis.__sentinel = 1` via `game_eval` before the rebuild, `game_wait` on `!globalThis.__sentinel` (it fires the moment the context resets), then wait for the app's root selector to be visible again.
Reloads can queue up, so a sentinel wipe can come from a stale reload rather than your build; when exactness matters, confirm the new code by an observable it introduces.

## Debugging without freezing the session away

The debugger only sees scripts parsed after it attaches; Gameface does not replay `scriptParsed` for already-loaded code (Chrome does), so a fresh session lists no game scripts at all.
Attach first (any `game_debug_*` call), then get the code re-parsed by triggering a UI reload (see the dev loop above); every bundle then appears under its real `coui://` or `assetdb://` URL.
A pause freezes the UI thread until resume; while frozen:

- `game_debug_evaluate` reads frame locals, and `game_eval` still works too (global scope, DOM reads included).
- `game_screenshot` hangs until the call timeout; never screenshot while paused.
- Input, rendering, and timers are dead, so nothing new happens until resume.

Minified bundles are one giant line: a line breakpoint resolves to the first breakable location on it (column 0), which is module-evaluation code that only runs during a reload.
To break inside a real function, take the exact column from a stack trace (`game_console` shows them) or from a pause location, or pause and step from there.
The safe cycle: set a conditional breakpoint, trigger it with an input tool, inspect with `game_debug_pause_state` and `game_debug_evaluate`, resume promptly, and remove all breakpoints before moving on.
The safety net: if the server's connection drops while paused, the engine auto-resumes, so a wedged pause cannot brick the game.
