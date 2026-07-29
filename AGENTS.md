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
- `scripts/`: `check-plugin-sync.ts` (manifest consistency, part of `mise check`) and `check-skill-changelog.ts` (`mise skills:check-changelog`, network-dependent, not in CI).
- `.agents/hooks/check-line-length.ts`: PostToolUse hook reporting `.ts`/`.cs` lines over 100 characters. Synced verbatim from the `scrolls` repo, which is why oxlint and oxfmt ignore `.agents`. Markdown is deliberately out of scope: these docs are agent-facing and unwrapped by design.
- `docs/ROADMAP.md`: planned facets. `docs/solutions/`: one file per hard-won problem, linked from where it bites.

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
- Reference Cities: Skylines 2 in tools' code, documentation and skills.
- Reference the coherent-gameface project in the unity-devtools one, and vice versa.

Ask first before:

- Adding a dependency.
- Reworking architecture.
- Performing destructive file or data operations.

## Where knowledge goes

Four stores, checked in this order when writing something down:

1. **Code comments**: anything one file owns. If `Invoker.Retrying` implements the retry, the explanation lives there.
2. **A plugin's `skills/`**: how to drive the tools at runtime. Shipped, agent-facing, generic.
3. **`docs/solutions/`**: expensive investigations with dead ends, one file per problem, loaded on demand through a pointer placed where the problem bites.
4. **`AGENTS.md`**: what no single site owns — the map, the conventions, the invariants spanning files. Plugin-specific facts go in that plugin's file, repo-wide facts at the root.

An `AGENTS.md` line that restates a comment, a tool description, or plainly readable code is dead weight: delete it.
Propose updates whenever you detect drift.

## Preferred agent behavior

- Start by inspecting existing patterns.
- Prefer LSP over Grep/Glob/Read for code navigation.
- Make the smallest safe change, but speak up when a refactor is overdue.
- Prefer editing existing files over creating parallel abstractions.
- When uncertain, state the assumption and proceed conservatively.
- Actively propose updates to `AGENTS.md`, comments, or other docs when you detect drift.
