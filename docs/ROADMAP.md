# Roadmap

`coherent-gameface` (in the `agents-plugins` marketplace) is a generic toolkit for driving a running
Coherent Gameface UI over CDP.
The MCP server (`gameface-devtools-mcp`, published on npm as `@csmodding/gameface-devtools-mcp`) is
the first facet; skills and richer instrumentation are planned.
The plugin is developed and verified against Cities: Skylines II's Gameface UI, but stays
application-agnostic.

## Planned/Explore

### Selector `index` parity across tools

`game_click` takes an `index` to pick among selector matches; `game_hover` and `game_screenshot`
(selector clipping) do not, forcing a manual tag-the-node workaround when no unique selector
exists. Add `index` to both.

### Keyboard input (`game_key`)

No tool sends key presses to the UI: CDP `Input` is ignored, and `game_type` only feeds characters
into a focused field (mutating its value). Escape-to-close a dialog, Enter-to-confirm, Tab between
fields, and arrow-key list navigation have no path (surfaced live: closing a settings screen needed
a manual find-and-click of the back arrow because Escape could not be sent). Add `game_key`
dispatching real bubbling `KeyboardEvent`s (keydown/keyup; `KeyboardEvent` exists in Cohtml) for a
named key to `document` or a selector, mirroring how `game_click` dispatches mouse events. First
verify per-game whether keys like Escape route through a JS `onKeyDown` handler (a dispatched event
reaches React) or at the native/engine level (it will not); document the caveat like the click one.

### Non-text element discovery

`game_find` matches on `textContent` only, so icon controls with no text (a back arrow, a close X)
cannot be located by it, forcing a `game_eval` scan by attribute or bounding-box position (surfaced
live locating a settings back arrow). Add an attribute-match mode to `game_find` (find by
`aria-label` / `data-tooltip` / `title` / any attribute); the driving skill already targets
`[data-tooltip="..."]` for clicks, so those anchors exist. `querySelectorAll` + `getAttribute` makes
it trivial.

### Reload awareness

A UI view reload (mod hot-reload, `location.reload()`) resets the JS context while the CDP
connection survives; agents currently detect it with a sentinel global, which queued reloads can
false-positive. The server should detect reloads passively from a CDP event instead: expose a
reload counter/timestamp in `game_status` and let `game_wait` wait for the next reload. First step
is verifying which event Gameface actually emits on view reload (candidates:
`Runtime.executionContextsCleared`, `Runtime.executionContextCreated`, `Page.frameNavigated`); none
was probed. Fallback if none fires: poll a server-planted context marker.

### Debugger ergonomics

Three sharp edges found while field-testing the debugger against a live game:

- `game_screenshot` hangs for the full call timeout while the UI is paused (`Page.captureScreenshot`
  needs the frozen frame loop); the server knows the pause state and should fail fast with "paused;
  resume first". Only gate frame-dependent commands: `Runtime.evaluate` keeps working while paused
  (verified), so `game_eval`, `game_dom`, and `game_wait` must stay usable.
- The debugger only sees scripts parsed after it attaches (Gameface does not replay
  `scriptParsed`), so a late attach lists nothing; `game_debug_scripts` should say so and suggest
  triggering a UI reload.
- On minified one-line bundles, a line breakpoint resolves to column 0 (module evaluation) and
  never hits during normal interaction. `game_debug_set_breakpoint` should report the resolved
  column and warn on single-line scripts; a `game_debug_search_source` (find string, return
  line:column candidates) would make column targeting practical.

### Richer console output

`game_console` shows console args via their RemoteObject description, so objects
render as "Object". Use `Runtime.getProperties` / object previews to expand them.
Entries also carry no timestamps, which makes correlating logs with actions and reloads
guesswork; capture and print one per entry (`Runtime.consoleAPICalled` and `Log.entryAdded` carry
a `timestamp` field in standard CDP; verify Gameface populates it).

### Network inspection

Gameface implements the `Network` domain (observe + `getResponseBody` + cookies), but `Fetch` is
missing (no request interception). Surface request/response observation as tools.

When these land they become standard plugin components: `commands/`, `agents/`, and `skills/`
directories auto-discovered by the plugin manifest (`skills/` already ships the `gameface` and
`gameface-driving` skills).
