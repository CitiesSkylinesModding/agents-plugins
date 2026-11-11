# AGENTS.md

## Plugin overview

`cs2-modding` teaches an agent to write **Cities: Skylines II code mods**.
It ships skills and the `cs2-dev` MCP server over the game's lifecycle; no scaffolds, no templates, no generators.
It names the game throughout, a deliberate carve-out from the repository's genericity rule: that rule exists to keep the two _toolkit_ plugins application-agnostic, and a plugin about one game cannot obey it and exist.

Scope is code mods, developed on Windows or on Linux, where the game runs under Proton and the toolchain is set up by hand.
Loading assets _from code_ belongs here; authoring meshes, textures, maps and editor content does not.
Windows stays the default voice of a procedure, and a Linux difference is stated where it bites; a Linux procedure longer than a few lines goes in a sibling file of its own, as `linux-toolchain.md` and `debug-patching-linux.md` do.

The plugin sits above the two toolkit plugins and may point at them, always softly: `unity-devtools` to settle a question against the running game, `coherent-gameface` for the UI engine underneath the frontend.
Every skill works unchanged when neither is installed.
The pointing is one-way by design: the toolkits stay game-agnostic, so neither ever points back here.

Each skill body holds a working loop, procedure, and the traps of its domain; a few references hold the toolchain, publishing, debug patching, the mod catalog and the UI build.
Everything else about the game is looked up fresh in the decompile and the install.

## The `cs2-dev` server

`bun build --target=node` bundles `mcp/` with its dependencies into two committed entries, `dist/server.mjs` and `dist/monitor.mjs`, so an install has no step and the shipped runtime is Node 22.4+; rebuild with `mise build:cs2-dev`.

The monitor is Claude Code's alone, started by `monitors/monitors.json`, and delivers while the agent drives other tools.
Every result's hint carries the same news, so a session without a monitor loses only the timing.
Its `when` holds the skill's namespaced name, `on-skill-invoke:cs2-modding:cs2-modding`: the harness compares it exactly, so a bare name passes `claude plugin validate` and never fires.

A `Player.log` parsing rule ships once checked on four captures, the stock game and the debug-patched one, each with a PDB and without: a rule read from fewer rests on what those captures share, passes review, and fails live.

Harness wiring: Claude Code's `.mcp.json` hands the server its own environment.
Codex's holds `tool_timeout_sec` above every tool's ceiling, a bound a test enforces, and approves the tools itself.

## The trap contract

A skill states a game fact only as a **trap**: a fact an agent holding the decompile and the live game still gets wrong by default, because the right code misleads it or a confident prior stops it from looking.
Procedure — how to search, build, restart, verify and diagnose — is admitted on its own terms.

- A trap is the wrong default plus the right move, in two lines at most, under a topic heading in the body of the skill whose domain it is; no trap sits in a file read on demand.
- It names a symbol at most: no line cite, count, type listing, closed list, or account of what the code does or why.
- A player-facing word that names nothing searchable in the code enters as a trap.
- Each body has a line ceiling, `bodyCeilings` in `scripts/check-skill-content.ts`. Raise one in a commit of its own; a new trap past it displaces a weaker one.
- Candidates arrive through the trunk skill's trap reports, and the maintainer admits them.
- No maintained file carries a line number into the decompile; the content lint fails a source file name followed by one.

## Symbol markers

A claim resting on a game type carries `[symbol: <fully qualified type>]`, naming the type whose code carries the behaviour, traced from the game's own call site and never picked by name, since type names repeat across the game's assemblies, and one that rests on no type carries `[symbol: none]`: in a skill body on the trap's line, in `mcp/src/` in the comment over each log line a game type prints, since the tests spell that line too and a reword turns none of them red.
`mise skills:trap-check <decompile root>` names each marked claim whose type's file changed or vanished since the commit in `scripts/trap-check/baseline`.

## Writing the skills

Load the `writing-for-agents` skill before writing or editing anything under `skills/`; the rules below are what this plugin adds.

- **One sentence per line**, as the sibling plugins' skills do.
- **The mods corpus is input, never output.** It is where techniques and gotchas were learned, and knowledge prose states the technique on its own authority. The single place a mod is named is the setup skill's provisioning catalog, `skills/cs2-modding-setup/references/mod-catalog.md`, which is also the name list the content lint reads — so the entry shape `scripts/check-skill-content.ts` parses is a contract the catalog keeps, and an entry losing its `###` heading or its `Source:` line fails the check rather than escaping it.
  When the check names a word your subject genuinely owns, report the collision and stop: the fix is the maintainer adding that word to the lint's ordinary-word list, and never prose bent around it. A word already on that list warns instead of failing, so a green run still carries the collision.
  **An entry certifies what its own source shows.** Reading one repository establishes nothing about the others, so a comparative — _the only_, _the deepest_, _the fullest_ — is a claim about the whole corpus and ships only with the sweep that established it. A superlative is also the hardest kind of sentence to doubt on re-reading, which is why this gates writing one rather than cautioning you to weigh it.
  **The corpus leaks in as an evidentiary clause tacked onto a true rule**, and that shape is what to grep for: _no mod read takes_, _across the repositories read_, _the practice is_, _and nobody does it_. The rule survives the cut, so cut it — the clause only dates the sentence to one reading of one corpus, and every reader after that reads a claim nobody can re-derive.
- **A mod author's design decisions are theirs.** Where the choice belongs to the mod author — replace another mod's data, cooperate with it, leave it alone — ship every option with its consequences rather than licensing one behind a condition. A condition reads as a permission, and an agent that satisfies it acts on this plugin's authority instead of its author's.
- **Libraries stay unnamed, except Harmony and PolySharp.** Teach the mechanism so an agent can always write the code itself, rather than pointing it at a dependency whose current shape it cannot verify. This governs libraries a mod would _reference_ — community helpers, utility packages, UI toolkits. Components the game or the official toolchain already ships are named as plainly as any game type: an agent cannot write the code that is already there. So are the applications a user runs on their own machine — a decompiler, an editor, an IDE — because a procedure has to say which program to run.
  Harmony, the ecosystem's only patching runtime, whose API is the vocabulary patching prose has to use: teach its prefix, postfix and injected-parameter vocabulary rather than the mechanism under it. PolySharp, which generates the framework types `net48` lacks and a raised `<LangVersion>` demands.
  Each carries the bare name wherever the prose is about its subject. Harmony's package reference lives in `skills/cs2-mod-project/SKILL.md` and nowhere else, with the version agreed for the shared-fate reason that file states; PolySharp contributes source and no assembly, so its version binds nobody. A third name passes the test these two pass or it does not ship: the mechanism stated alongside and usable as written, so an agent that cannot resolve the package still writes working code.

## Guarded local-source access

Every entry into a local source is conditional, so an agent never greps a path that does not exist.

- **The decompile, the mod corpus and the readable copy of the UI bundle**: the user chose where each of these lives, so no environment variable finds them and the record that `cs2-modding-setup` owns is the only thing that does. Read it before touching one; finding no root recorded, route the user to that skill rather than guessing a path. Its "The record" section is the single source for the file's location, format and rationale.
- **The installed game itself**: the string tables, the packaged content and the shipped UI bundle, all under the install root. The toolchain sets environment variables naming it and the paths beneath it, so a skill reads one rather than hardcoding a path, and treats a missing variable as the signal to ask rather than to guess. The bundle ships minified to a single line, so a skill reads the reformatted copy above, made from it.
- **The official toolchain**: its build targets, the Unity mod project whose package cache holds the Entities, Collections and Burst packages as C# source, and the UI mod scaffold whose template declares every module a UI mod may import. The game ships all three under the install, in `Cities2_Data/Content/Game/.ModdingToolchain/`, the Unity project as a zip; the installer copies the targets to `CSII_TOOLPATH` and unpacks the project to `CSII_UNITYMODPROJECTPATH`, and those per-user copies are what a build reads — the package cache exists only once an editor has opened the project — so `skills/cs2-mod-project/references/build-pipeline.md` names the environment variables that locate them. The scaffold is used in place instead: the toolchain installs it by linking the npm-global entry to `Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`, so the install root reaches it directly and its files version with the game — a global entry that is a plain directory rather than that link is a stale copy, not the source.
- **The wiki**: capability-agnostic. Try a web-fetch tool, expect a plain HTTP fetch to come back with the site's JavaScript bot challenge instead of content, and ask the user as the last resort.
- **The running game**: reading its state takes the sibling Unity plugin and a game patched for debugging, which is what the setup skill's debug patching provides.
