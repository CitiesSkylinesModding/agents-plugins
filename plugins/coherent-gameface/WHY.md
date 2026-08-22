<div align="center">

# Why this project exists

**`gameface-devtools-mcp` drives the game you already have running.**
No SDK, no licence, no `Player.exe`, no version floor.

</div>

---

In August 2026, Coherent Labs published their own
[Gameface MCP server](https://github.com/CoherentLabs/Gameface-MCP). People keep asking which one to
use, so here is the honest answer: they solve different problems. The difference isn't a feature
list. It's what each tool was built and documented to point at.

- **Theirs is Player-shaped.** Its workflow launches a Gameface **Player** from a licensed SDK
  install and drives the UI you're authoring in it. The README calls the SDK a hard prerequisite,
  and the tools assume Cohtml 3.1.2+ behaviour throughout.
- **Ours is game-shaped.** It attaches to a **game that's already running**, shipped and retail and
  most likely someone else's, then drives the UI on screen right now, on whatever engine that game
  froze at ship time.

To be fair about it: their `connect_browser` will technically attach to a retail game. It takes a
host and a port, and the guard only checks that the page reports `cohtml`. What it hasn't been
built or tested for is what it finds once it gets there, which is a 1.x engine, `nodeId` addressing
on a DOM domain their own README documents as partly broken, and an interaction layer resting on
CDP input.

## The short version

|  | **gameface-devtools-mcp** (this project) | Coherent Labs' Gameface MCP |
| --- | --- | --- |
| **What it connects to** | Any reachable Gameface CDP endpoint: a retail game, a dev build, a Player | A `Player.exe` it launches, or a running Player it attaches to |
| **Needs the Gameface SDK?** | **No** | **Yes.** Their README states it cannot ship one |
| **Needs a Gameface licence?** | **No** | Yes, in practice |
| **Engine versions** | Any. Field-verified down to **Cohtml 1.64** (on Cities: Skylines II) | Targets **Cohtml 3.1.2+** |
| **JS debugger** | **Yes.** Breakpoints, conditionals, stepping, frame locals, source search | No |
| **Waits for the UI to settle** | **Yes.** `game_wait` on a selector, a predicate, or a view reload | Navigation only |
| **Survives a UI reload** | **Yes.** Reload tracking, race-free `sinceReloads` | Not tracked |
| **Layout assertions** | Not yet, but [planned](https://github.com/CitiesSkylinesModding/agents-plugins/blob/main/docs/ROADMAP.md#layout-assertions) | **Yes**, three of them |
| **Performance tooling** | Knowledge only, in the `gameface` skill | **Yes.** A static lint and a calibrated frame-timing baseline |
| **Install** | **One command.** Plugin install, or `npx`. No build step | `git clone`, `npm install`, `npm run build`, then hand-write a client config |
| **Skills included** | **Two**, installed with the plugin into any project you work in | One, living in their repository |
| **On npm** | **Yes** | No, GitHub clone only |
| **First published** | **2026-07-14** (npm) | 2026-08-19 (GitHub) |

<sub>Their column reflects <a href="https://github.com/CoherentLabs/Gameface-MCP">CoherentLabs/Gameface-MCP</a> at commit <code>e3bc2a6</code>, read 2026-08-22. They ship from <code>main</code> with no tagged releases, so it moves without notice.</sub>

## What ours does that theirs can't

### Run without a Player

This is the whole thing, really. Theirs is written around a Player you own. `launch_browser` spawns
`Player.exe` from a path you configure, the README calls a local SDK install a prerequisite it
can't ship, and the performance baseline is calibrated against a Player at a fixed resolution.
That's a sound design if you're a UI developer at a studio, iterating in a sandbox before the code
ever reaches the game.

A modder has none of it. No SDK, no licence, and the UI you care about only exists inside a game
that's already running. Ours finds that game's CDP endpoint, resolves its page target, and starts
driving, with nothing installed but Node.

### Work against an engine from 2021

A game freezes its Cohtml version at ship time, and then it stays frozen. Cities: Skylines II, our
reference target, runs 1.64.0.7, while the current Gameface docs describe a 3.x release. Their
tooling and their documentation corpus are built for 3.x, and say so.

We treat that gap as the core problem rather than a footnote. The `gameface` skill ships a
[version-gating reference](skills/gameface/references/version-gating.md) with a lookup procedure, a
milestone timeline, feature-detection probes and the versions they discriminate, and a trick for
telling an engine API apart from a game's own polyfill. A feature claim isn't settled here until
it's been gated against the version the game actually has.

### Debug someone else's minified UI

You didn't write the code you're debugging, there's no source map, and the whole bundle is one
line. So the plugin ships a **JS debugger**: breakpoints (conditional ones too, and by resolved
column, because a line breakpoint on a minified bundle binds to module-evaluation code that runs
once on load and never again), stepping, frame locals, source search, and an auto-resume safety net
so a pause can never brick the game.

Their server has no debugger.

### Keep up with a game that won't hold still

A Player renders a page and waits for you. A game runs a simulation, reloads its UI when a mod
rebuilds, and wipes the JS context out from under your next tool call.

- **`game_wait`** composes three phases. It waits for a view reload, waits out a quiescence window,
  then polls a selector or predicate in the fresh context.
- **`sinceReloads`** makes that race-free. Pass the baseline count and the wait is satisfied even
  when the reload already landed before you asked.
- **`game_console`** interleaves reload markers, so you can place a log line on the right side of a
  context reset.

Their only wait is `navigate`'s `waitUntil: documentUpdated`. Nothing waits on a selector, a
predicate, a quiescence window or a reload, so every other check is a bare poll the agent has to
orchestrate itself.

### Read a console you can trust

Objects are expanded to a fixed depth **at capture time**, so re-reading the same entries at
greater depth never re-runs the code that logged them. Truncation is always marked. Timestamps come
out normalised, even though Cohtml sends milliseconds-since-boot where CDP specifies epoch
milliseconds.

### Tell you *why* your selector was rejected

Gameface's query APIs accept a short list of pseudo-classes and throw `Invalid CSS selector` on
everything else. Every selector-taking tool here names the construct it suspects and offers you a
rewrite, straight from the server, so the diagnosis reaches you even from an MCP client that loads
no skill.

## One command, and nothing to build

Ours installs as a **plugin**:

```
/plugin marketplace add CitiesSkylinesModding/agents-plugins
/plugin install coherent-gameface@csmodding
```

That's the whole procedure, on Claude Code or Codex CLI. The MCP server autoloads from a
self-contained bundle committed to the plugin, so there's no `npm install`, no compile step, it
works offline, and it stays version-locked to the plugin you installed. Both skills come along with
it, into whatever project you're working in.

On another MCP client? It's on npm and runs straight from `npx`, still with nothing to build:

```json
{ "command": "npx", "args": ["-y", "@csmodding/gameface-devtools-mcp@latest"] }
```

**Theirs is a repository you assemble.** Clone it, `npm install`, `npm run build` to compile the
TypeScript into `build/`, then hand-write a client config pointing an absolute path at
`build/index.js`, plus the Player path, either as a `--browser-executable` argument there or in a
`~/.gameface-mcp/config.json`. It isn't published to npm, so there's no `npx` path and no version
to pin beyond whichever commit you happened to clone. Their skill sits in that repository's
`.claude/skills/`, so it applies while you're working inside their repo. Ours travels with the
plugin into your own project.

## Where theirs is ahead

Credit where it's due. Four things their server has that ours doesn't:

- **Layout assertions.** `assert_text_fits`, `assert_no_overlap` and `assert_within_parent` turn
  "does this look right?" into a machine-checkable result. A genuinely good idea, and
  [on our roadmap](https://github.com/CitiesSkylinesModding/agents-plugins/blob/main/docs/ROADMAP.md#layout-assertions).
- **Performance tooling.** A static lint for expensive layout shapes, plus a frame-timing tool
  measured against a calibrated, committed noise floor. Our
  [`performance` reference](skills/gameface/references/performance.md) carries the cost model behind
  three of its six lint rules, and no tool at all.
- **Computed styles as a tool.** Their `get_computed_styles` returns an element's resolved styles in
  one call. Here that's a hand-written `getComputedStyle` through `game_eval`.
- **A large documentation corpus.** Roughly 6,400 lines distilled from the Gameface docs,
  searchable, exposed as MCP resources. Ours is a smaller curated set plus a live docs extractor,
  which is a deliberate trade of density for freshness: their corpus describes 3.x with no version
  gating anywhere, and would mislead badly against a 1.x game.

## Which should you use?

- **Modding a shipped game?** Retail build, no SDK, old engine, minified UI. Use this one. Theirs
  isn't built for it.
- **Building new UI at a studio,** with the SDK, in the Player, on a current engine? Theirs is
  designed for exactly that, and its assertions and performance tooling are real advantages. Ours
  still works, since you can point it at any CDP endpoint including the Player's, and it adds the
  debugger and the wait primitives. Worth knowing that our input and `:hover` findings were
  established on 1.64 and aren't gated for 3.x yet.
- **Both** is fine. They register under different names and don't conflict.

## Provenance

This project didn't start as a response to theirs. The repository's first commit lands 2026-06-25,
`@csmodding/gameface-devtools-mcp` 0.1.0 went to npm on 2026-07-14, and 1.0.0 followed on
2026-08-14. Coherent Labs published their initial public commit on 2026-08-19.

So ours was publicly installable about five weeks before theirs existed. That's a fact about
publication dates and nothing more. It isn't a claim about who thought of it first, and Coherent
Labs may well have been building theirs internally for far longer. They are, after all, the people
who make the engine.

What's genuinely reassuring is that the two projects reached some of the same conclusions
independently. Both codebases discovered that Gameface echoes the request path into
`webSocketDebuggerUrl`, producing a URL like `ws://host:port/json/list/devtools/page/0`, and both
work around it by resolving the target themselves. That quirk is now confirmed on the 1.x and 3.x
lines alike.

Their server is MIT-licensed and worth reading. Where their ideas beat ours, we intend to say so
and adopt them.

---

**Next:** [plugin README](README.md) · [MCP tool reference](mcp/README.md) ·
[repository](https://github.com/CitiesSkylinesModding/agents-plugins)
