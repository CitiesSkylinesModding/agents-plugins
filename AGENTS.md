<!-- Version: 1.0.0 -->

# AGENTS.md

## Project overview

`agents-plugins` is the CS Modding marketplace (`csmodding`) of agent plugins for Claude Code and OpenAI Codex CLI.

- `plugins/coherent-gameface/`: flagship plugin. Drives a running Coherent Gameface UI (Cohtml) over a direct CDP WebSocket; TypeScript MCP server (npm `@csmodding/gameface-devtools-mcp`) + skills.
- `plugins/unity-devtools/`: drives a running Unity Mono development build over the Mono Soft Debugger protocol (SDB); C# MCP server + SDB class library.

Both are generic toolkits, developed and verified against **Cities: Skylines II** as the reference target.
Each plugin documents its own architecture and gotchas in its `AGENTS.md` (with a `CLAUDE.md` symlink).

Everything a plugin ships MUST live inside its `plugins/<name>/` directory: marketplace installs copy only that subtree.
New plugins get a sibling directory and an entry in both marketplace files.

## Tech stack

- [mise-en-place](https://mise.jdx.dev): dev tools, env vars and tasks.
- Bun workspaces: the root `package.json` carries lint/format tooling (`oxfmt.config.ts`, `oxlint.config.ts`) and lefthook; the gameface `mcp/` is the only workspace package, and `bun.lock` lives at the root.
- .NET 10 SDK: the unity-devtools C# projects, grouped by `agents-plugins.slnx` at the repo root.

## Repository structure

- `.claude-plugin/marketplace.json` and `.agents/plugins/marketplace.json`: the Claude Code and Codex CLI marketplace files. Both list every plugin.
- `.mcp.json` (root): LOCAL DEV ONLY, wiring both MCP servers for sessions in this repo (gameface from its committed bundle, unity from sources via `dotnet run`). Installed users get each plugin's own `.mcp.json`; keep them in sync when changing server wiring.
- `scripts/`: `check-plugin-sync.ts` (manifest consistency) and `check-skill-content.ts` (the `cs2-modding` shipped-prose rules), both part of `mise check`; `check-skill-changelog.ts` (`mise skills:check-changelog`, network-dependent, not in CI). `hook-skill-content.ts` is a PostToolUse hook spawning the shipped-prose lint on any edit under that plugin's `skills/`, so a broken warning block, link or marker fails at the edit rather than at the commit; it adds no rule of its own.
- `.agents/hooks/check-line-length.ts`: PostToolUse hook reporting `.ts`/`.cs` lines over 100 characters. Synced verbatim from the `scrolls` repo, which is why oxlint and oxfmt ignore `.agents`. Markdown is deliberately out of scope: these docs are agent-facing and unwrapped by design.
- `docs/ROADMAP.md`: planned facets. `docs/solutions/`: one file per hard-won problem, linked from where it bites. `docs/adr/`: numbered decision records.
- `docs/mechanics-reference-shape.md`: the form every `cs2-modding` mechanics reference takes, disclosed out of the plugin's `AGENTS.md` because only an authoring pass reaches it. `check-skill-content.ts` enforces its prose-line budget.
- `docs/SOURCES.md`: every source the `cs2-modding` pipeline may read, what each settles, and how to locate it. Other files point at it; keep it pointing at as few as possible.
- `docs/research/`: the `cs2-modding` pipeline's cited stage, sitting outside `plugins/` so none of it ships. Its `README.md` holds the conventions a research file satisfies; nothing under `plugins/` may reference it or `SOURCES.md`, and `check-skill-content.ts` fails any shipped link resolving outside the plugin directory, since existence alone passes one that works here and dead-ends for every installed user.

## Commands

- `mise check:agents`: read-only type check, lint, format and plugin-sync, output tuned for agents. `mise fix` applies auto-fixes; C# formatting is `mise fix:cs` (write-only, no read-only counterpart).
- `mise test`: the .NET test suite.
- `mise build:gameface`: rebuild the shipped gameface bundle (commit the result).

Run `mise tasks` to see the full shortcut list; append arguments freely, mise passes them through (ex. `mise some:task --some-arg`).
Do NOT use npx to run commands; prefer mise shortcuts, or bun/bunx when no shortcut exists.

Always run the appropriate check/test commands after changes, at the end of the editing session rather than mid-flight.

## Dual-harness plugin architecture

Every plugin ships two manifests, one per harness: `.claude-plugin/plugin.json` + `.mcp.json` for Claude Code, `.codex-plugin/plugin.json` + `.codex-plugin/mcp.json` for Codex CLI.
The two MCP configs cannot be merged, and a plugin server must work with no env passed at all — read [`docs/solutions/dual-harness-mcp-config.md`](docs/solutions/dual-harness-mcp-config.md) before touching any of these files.

SYNC RULE: the shared fields of a plugin's two plugin.json files (name, version, description, author, homepage, repository, license, keywords) must stay identical. Edit BOTH; `mise check:plugin-sync` enforces it in `mise check`, CI and the pre-commit.

## Versioning and releases

release-please maintains a rolling release PR on `main` from Conventional Commits.
Each plugin has two release units (the plugin and its mcp) joined by `linked-versions`, so they always share a version; the two plugins version independently and there is no root unit (root-only changes never release).

Never hand-edit a version: each unit's number lives in a private `package.json` anchor, and release-please syncs it into the plugin manifests, the unity csproj `<Version>`, and the `dotnet dnx` version pins.

- Any releasable commit under a plugin's directory bumps BOTH of that plugin's units.
- Pre-1.0: `feat` bumps minor, `fix` patch. 1.0.0 only via a deliberate `Release-As:` footer.
- Publishing stays MANUAL (`mise publish` for npm, `mise publish:unity` for NuGet, run by the user). No CI publish job; do not add one.
- CI runs `mise check:agents` + `mise build:gameface` with `git diff --exit-code` (catching a stale committed bundle), then builds the .NET solution and runs the tests. The pre-commit rebuilds and stages the gameface bundle, and runs `dotnet test` on staged C# changes.

## Boundaries

Never:

- Create a git branch, stage files, or commit work yourself unless the user expressly told you so.
- Commit secrets, tokens, `.env` files, dumps or credentials.
- Modify generated files unless the generation command was run.
- Change public API behavior without calling it out.
- Reference Cities: Skylines 2 in `coherent-gameface` or `unity-devtools` — code, documentation and skills alike, those two stay application-agnostic. `cs2-modding` is the carve-out: a knowledge product about the game that names it throughout, with the reasoning in its own `AGENTS.md`.
- Reference the coherent-gameface project in the unity-devtools one, and vice versa.

Ask first before:

- Adding a dependency.
- Reworking architecture.
- Performing destructive file or data operations.
- Acting on the user's running game — launching it, quitting it, loading or reloading a save, changing a setting in it. Say what you need done and why, then wait: only they can see what the game is actually on, and a launch costs minutes of their machine. Building, deploying and reading logs need no such permission.

## Where knowledge goes

Five stores, checked in this order when writing something down:

1. **Code comments**: anything one file owns. If `Invoker.Retrying` implements the retry, the explanation lives there.
2. **A plugin's `skills/`**: how to drive the tools at runtime. Shipped, agent-facing, generic.
3. **`docs/solutions/`**: expensive investigations with dead ends, one file per problem, loaded on demand through a pointer placed where the problem bites.
4. **`docs/adr/`**: why a choice was made, one record per decision.
5. **`AGENTS.md`**: what no single site owns — the map, the conventions, the invariants spanning files. Plugin-specific facts go in that plugin's file, repo-wide facts at the root.
   An `AGENTS.md` may disclose a contract only one branch of work reaches into a `docs/` file of its own, pointed at from the rule it elaborates — `docs/mechanics-reference-shape.md` is the one that does.

An `AGENTS.md` line that restates a comment, a tool description, or plainly readable code is dead weight: delete it.

Propose updates whenever you detect drift.

`.scratch/` is working material and gitignored, so nothing tracked may cite a path inside it — a pointer from `docs/` or from a plugin into a scratch file dangles the moment the feature closes and its folder goes. Move the fact into one of the five stores instead.
For the same reason a repo-wide sweep — renaming a term, retiring a rule — has to name `.scratch/` explicitly, since the search tools honour `.gitignore` and skip it by default.
Such a sweep edits what it finds there: a live spec and its tickets are the instructions the next authoring pass runs on, so one still teaching a rule a decision has retired is a defect like any other, and a review that parks it as out of scope leaves the sweep half done.
A sweep correcting shipped `cs2-modding` prose covers `docs/research/` too: those files are the next authoring pass's inputs, and a retired teaching surviving there walks straight back into the reference.

Agent-facing prose is a deliverable here, not documentation of one: a plugin's `skills/`, every `AGENTS.md`, the rules files, and an MCP tool's or parameter's `[Description]` (agent-facing despite living in `.cs`).
Load the `writing-for-agents` skill and hold the edit to it before writing any of them.
In the toolkit plugins, its examples name placeholder types (`MyGame.Citizens.Citizen`) rather than the reference target's own, and claim verification against no named game: an example teaches a shape, while a real name invites an agent to try it on a game that never had it.
Cite a checklist item by quoting its phrase, never its ordinal — an insertion silently renumbers every positional cite.

## Preferred agent behavior

- Start by inspecting existing patterns.
- Prefer LSP over Grep/Glob/Read for code navigation.
- Make the smallest safe change, but speak up when a refactor is overdue.
- Prefer editing existing files over creating parallel abstractions.
- When uncertain, state the assumption and proceed conservatively.
- Actively propose updates to `AGENTS.md`, comments, or other docs when you detect drift.
- The gameface and unity tools show their gaps in use: when you work around a missing tool, or a missing mode of one that exists, propose an entry in [`docs/ROADMAP.md`](docs/ROADMAP.md), with the workaround you used as its evidence.
- Formatters reformat the whole project, not your diff. Never revert what they touch: fold drift in a file your change already edits into that commit, and land drift in files it does not into a commit of its own.
