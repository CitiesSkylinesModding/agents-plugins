# Roadmap

`coherent-gameface` (in the `agents-plugins` marketplace) is a generic toolkit for driving a running Coherent Gameface UI
over CDP. The MCP server (`gameface-devtools-mcp`, published on npm as
`@csmodding/gameface-devtools-mcp`) is the first facet; skills and richer instrumentation are
planned. The plugin is developed and verified against Cities: Skylines II's Gameface UI, but stays
application-agnostic.

## Shipped

### Gameface UI instrumentation (MCP server)

Direct Chrome DevTools Protocol control of a running Gameface UI. Tools: `game_status`,
`game_eval`, `game_screenshot` (with selector clipping), `game_dom`, `game_wait`,
`game_click`, `game_fill`, `game_type`, `game_hover`, `game_console`. See `mcp/README.md`
and `mcp/`. Input tools dispatch DOM events (CDP `Input` is ignored by Gameface).

### JS debugging (MCP server)

Source-level debugging of the UI via the V8 `Debugger` domain: `game_debug_status`,
`game_debug_scripts`, `game_debug_source`, `game_debug_set_breakpoint`,
`game_debug_remove_breakpoint`, `game_debug_pause_state`, `game_debug_evaluate`,
`game_debug_step`. Breakpoints/pauses freeze the UI until resume; closing the
connection auto-resumes.

### Gameface domain-knowledge skill (`gameface`)

Model-invoked skill teaching agents what Gameface is and is not: the not-a-browser deltas, the
version-gating protocol (docs describe the latest engine; games embed a frozen Cohtml), a baked
version-to-feature timeline (`references/version-gating.md`, freshness-checked by
`mise skills:check-changelog`), layout/scripting/performance/tooling references, and a
`fetch-doc.mjs` extractor for the docs site (which defeats summarizing fetch tools). Generic
across games, with Cities: Skylines II as the labeled worked example. See `plugins/coherent-gameface/skills/gameface/`.

### Gameface driving skill (`gameface-driving`)

Model-invoked skill teaching agents how to operate the `game_*` tools against a live game:
session triage and crash conduct, element discovery under hashed class names and missing lookup
APIs, the act-then-verify loop, the rebuild-to-live reload cycle (full view reload, sentinel
detection), and safe source-level debugging (attach-before-parse script
visibility, the pause freeze matrix, minified-bundle breakpoint placement). Verified live against
Cities: Skylines II; points at the `gameface` skill for engine domain facts. See
`plugins/coherent-gameface/skills/gameface-driving/`.

## Planned

### Text-based element discovery (`game_find`)

Cohtml has no XPath, TreeWalker, or `innerText`, and app class names are often build-hashed, so
agents locate elements by scanning `querySelectorAll` results and filtering on `textContent`
through `game_eval`. Promote that idiom to a `game_find` tool: CSS selector plus a text
equals/contains filter, returning tag/classes/rect per match. Ideally it also solves the
discovery-to-action handoff by tagging matches with an auto attribute (e.g. `data-gf-h="3"`) and
returning those as ready-to-use selectors for the input tools.

### Selector `index` parity across tools

`game_click` takes an `index` to pick among selector matches; `game_hover` and `game_screenshot`
(selector clipping) do not, forcing a manual tag-the-node workaround when no unique selector
exists. Add `index` to both.

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
