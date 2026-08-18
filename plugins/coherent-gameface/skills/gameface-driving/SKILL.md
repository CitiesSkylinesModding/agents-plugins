---
name: gameface-driving
description: 'Operating manual for driving a live Gameface UI with the game_* MCP tools: procedures and traps. Load before first use of the input tools (game_click, game_fill, game_type, game_key, game_hover) or of any game_debug_* tool. Also use when verifying UI changes against a running game, when waiting for a mod rebuild to go live in-game, or when a game_* call fails or returns puzzling results. Engine support questions (does this CSS/JS/layout feature exist) belong to the gameface skill.'
---

# Driving a Gameface UI

This skill records the procedure for the `game_*` tools: the facts the tool schemas cannot tell you.
Facts that may be game-specific are labeled; the reference target is Cities: Skylines II (CS2, Cohtml 1.64.0.7).
For what the engine itself supports (layout, events, missing platform APIs), load the `gameface` skill; this one stays operational.

## Session start and triage

Screenshot first and orient before acting: menus, dialogs, or a loading screen change what is safe to click.
A capture holds the UI view alone: the application's own rendering never reaches it, and every transparent region comes back flat white, so an expanse of white is the normal backdrop of a HUD rather than a blank or broken UI.
Read the application's state from what its UI displays, never from the scene behind it.
When any tool fails, run `game_status` before retrying; it settles whether the endpoint is reachable and which engine and page answered.
Read the page identity from `target.url` (for example `assetdb://gameui/index.html`).
A dead endpoint mid-session usually means the game crashed or was closed.
Report it and wait for the developer to relaunch the game; retry-looping cannot help, and testing that was interrupted mid-action may have left the UI in a state worth re-checking with a screenshot once the game is back.
No reconnect ritual exists or is needed: the server re-resolves the page target on the next call after the game returns.

## Finding elements

`game_query` is the built-in element search; the schema covers its parameters, so what follows is how to read and use it.
Text is the durable anchor where the UI shows it and an attribute selector is the one where it does not; `attributes: true` on a match reveals which attribute the next query should anchor on.
Its three counts cascade: `unprunedTotal` (what the selector and text filter produced), `total` (after `deepest` pruning), and `returned` (after `limit`); `total` above `returned` means the `limit` truncated, so narrow the query, while `unprunedTotal` above `total` just shows how many ancestors `deepest` pruned.
`deepest` exists because an element's `textContent` includes its descendants': a panel and its title button both match the title, and `deepest` prunes the panel; pass `deepest: false` when you want the enclosing container (finding a panel by its title), and note a query no text drives — a selector alone, or `text: ''` — has no such bleed to correct.
`tag: true` handles die on the next tagging call and on any view reload; re-tag rather than reusing a stale handle.
When a selector is non-unique but the match order is known, the input tools' `index` parameter is a lighter alternative to tagging; each tool reports `Matches: N` so you can tell whether the selector was ambiguous, and an out-of-range `index` reports the match count so you can correct it.
Narrow with `game_query`, then read the one element you settled on with `game_dom`.
For a predicate no selector can express here (computed state, or picking a parent by what its children are, since `:has()` throws), scan manually from `game_eval`: `[...document.querySelectorAll('button')].find(el => ...)`, then tag the node with `el.setAttribute('data-probe', '1')` and target `[data-probe]` when you need a unique selector, removing it after.
There is no XPath, no TreeWalker, and no `innerText` to lean on (engine gaps; details in the `gameface` skill).
The JS query APIs answer a short set of pseudo-classes and throw "Invalid CSS selector" on the rest: combinators, `[attr*=]`, `:first-child`, `:last-child`, `:only-child`, `:nth-child()`, `:root`, `:hover`, `:focus`, `:active`, `::before` and `::after` are what is verified to work on CS2, and `:not()`, `:has()`, `:is()`, `:where()`, the of-type family, `:nth-last-child()`, `:empty`, `:checked` and `:disabled` are verified to throw; treat anything in neither list as untested rather than as supported.
`:nth-child()` itself takes an integer, `even`, `odd`, or a bare `an` step; an `an+b` offset (`n+2`, `-n+3`) throws like an unsupported pseudo-class.
Every selector-taking tool names the construct it suspects and a rewrite when it hits the rejection, `game_eval` and `game_debug_evaluate` included, falling back to the supported set when it can pin no construct: act on what it names rather than retrying the selector.
`game_console` is the exception, streaming page exceptions as the engine wrote them, so a rejection surfacing there arrives bare.

## Act, then verify

Input calls report that events were dispatched, not that the UI reacted; confirm the effect you care about before building on it.
The cheap confirmations: `game_wait` on a predicate or selector, `game_dom` on the region that should have changed, a screenshot clipped to that region with `game_screenshot`'s `selector`, and `game_console` for exceptions a silent failure left behind.
Re-reading `game_console` at a higher `depth` renders the tree captured with the entry, so it never re-runs the code that logged the object.
Its stamps are local wall-clock times, exact where the engine sends the epoch milliseconds CDP specifies and server receive times otherwise (Cohtml sends milliseconds since engine boot), so treat them as accurate to the socket hop.
Clipping is what makes a screenshot cheap: pass the selector of the region you are verifying, and keep the full viewport for orienting, where you do not yet know what to clip to.
`game_click` returns after dispatching the event sequence, before any async handler work; pair it with a wait on the expected outcome.

## Keyboard: `game_key` and native-handled keys

The `game_key` schema covers the mechanics (`keydown`+`keyup` only, no default action, `game_type` for text entry); what it cannot tell you is where a dispatched key actually lands.
`game_key` is for keys a handler interprets: Enter to confirm a dialog, arrows for in-UI list navigation, a UI's own `onKeyDown` shortcut.
A dispatched key reaches the UI's JS `onKeyDown` handlers, but not any input the application consumes at the native/engine level, and the two are easy to confuse.
Games commonly route global navigation and hotkeys (Escape/back, closing a menu or settings screen, tool cancel) through the host's own input system rather than the DOM, so a dispatched key such as `Escape` fires page listeners yet has no effect on that native handling; test per application before relying on a key doing more than reaching a DOM handler.
The result's `preventDefault` flag is a hint, not proof: confirm the observable effect you care about, not the dispatch.
Do not hand-roll a `KeyboardEvent` in `game_eval` and expect `.key` to read back: Cohtml derives `key` from the event's `keyCode` and ignores the constructor's `key`, so a handler sees the wrong key unless you `Object.defineProperty` it; `game_key` already forces `key`/`code`/`keyCode`/`which` on every event, so reach for it instead of rolling your own.
When a key is handled natively and has no DOM path, do not try to fake it: invoke the action the UI's own JS runs for that key instead.
If the application drives its UI through Gameface's data-binding bridge, that action is usually a binding you can call from `game_eval` (`engine.trigger('<group>.<name>', ...)` or `engine.call`), for example the binding a screen uses to close or navigate.
Binding names are per-application; discover them by searching the UI's JS bundles or the application's source, and note there is no JS seam to simulate a raw key/input action or inject a trusted engine key event.

## The dev loop: rebuild to live

Gameface ships no file watcher (its docs' "Live Reload" page is a webpack-dev-server recipe for pages served from a dev server), so how a UI reloads is per-application wiring: an application-side file watcher calling the native view reload, a dev-server client or key handler calling `location.reload()` from inside the page, or nothing at all.
Hot reload is typically a developer-mode feature; when a rebuild produces no reload, suspect the gate (a launch flag, how the UI was installed) before suspecting the build, and `game_eval` of `location.reload()` is the manual fallback.
However triggered, a reload is a full view reload: the JS context resets (all globals wiped), the document rebuilds, and every script re-parses.
The CDP connection survives the reload transparently, and an in-flight `game_wait` keeps polling across the context reset and can resolve on the other side.
The server detects reloads passively: `game_status` arms the tracking and reports a `reloads` block (monotonic `count`, `lastReloadAt`, the context's `uniqueId`), and `game_wait` takes `reload: true` to wait for one.
The deterministic idiom to get new code live: rebuild, read the baseline `count` from `game_status`, `game_eval` of `location.reload()`, then one `game_wait({reload: true, sinceReloads: <baseline>, selector: <app root>})`.
`sinceReloads` matters because `location.reload()` lands in ~200ms (CS2-verified), faster than your next tool call; with the baseline, the wait is satisfied even when the reload already happened, so there is no race to lose.
After the reload phase, `game_wait` holds until no further context swap for `quiescentMs` (default 1000ms), then runs the selector/predicate phase in the fresh context, so one call covers "reloaded and UI ready"; its success text reports the final reload count, the baseline for the next iteration without another `game_status`.
The count is a lower bound: reloads while the server is disconnected collapse into one, and a game restart surfaces as one reload at reconnect.
`game_console` interleaves a `view reloaded (#N)` entry per reload, which correlates log lines with context resets.
CS2-verified watcher behavior: the mod-file watcher is a slow coalescing mtime poll (~15-20s period, jittery); all file changes since its last tick coalesce into ONE reload trigger, and each trigger fires exactly two context swaps ~530ms apart (absorbed by the default quiescence window, counted as +2).
So on CS2, prefer the `location.reload()` idiom for determinism; when relying on the watcher, raise the wait budget (`game_wait({reload: true, timeoutMs: 45000, ...})`), since the trigger latency alone can approach 20s before the first swap.
Reloads can still queue or coalesce outside your control; when exactness matters, confirm the new code by an observable it introduces.

## Debugging without freezing the session away

The debugger only sees scripts parsed since it attached; Gameface does not replay `scriptParsed` for already-loaded code (Chrome does), so a fresh session lists no game scripts at all.
Attach first (any `game_debug_*` call), then get the code re-parsed by triggering a UI reload (see the dev loop above); every bundle then appears under its real `coui://` or `assetdb://` URL.
A pause freezes the UI thread until resume; while frozen:

- `game_debug_evaluate` reads frame locals, and `game_eval` still works too (global scope, DOM reads included).
- `game_console` keeps capturing and expanding, so a `game_eval` that logs an object reads back in full without resuming first.
- `game_screenshot` is the one tool that refuses, since the capture needs the frame loop the pause has frozen.
- `game_wait` stays usable, and its timeout names the pause, so a frozen UI never reads as an unreachable condition.
- Input, rendering, and timers are dead, so nothing new happens until resume.

Minified bundles are one giant line: a line breakpoint binds to the first breakable location on it, which is module-evaluation code that runs on load and never again during interaction.
Read the `url:line:column` the breakpoint resolved to and you can see that happen; to break inside a real function instead, find its position with `game_debug_search_source` and pass that column, or take one from a `game_console` stack trace or a pause location.
Breakpoints survive a view reload, the engine re-binding its own registrations to the re-parsed script, so a reload does not disarm your session — and one sitting on module-evaluation code re-hits on every reload.
The safe cycle: set a conditional breakpoint, trigger it with an input tool, inspect with `game_debug_pause_state` and `game_debug_evaluate`, resume promptly, and remove all breakpoints before moving on.
The safety net: if the server's connection drops while paused, the engine auto-resumes, so a wedged pause cannot brick the game.
