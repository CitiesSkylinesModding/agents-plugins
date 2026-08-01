# AGENTS.md

## Plugin overview

`cs2-modding` teaches an agent to write **Cities: Skylines II code mods**.
It is knowledge only: skills and references, no MCP server, no runtime, no shipped code artifacts — no scaffolds, no templates, no generators.
It is also the one plugin here that names a game on purpose, a deliberate carve-out from the repository's genericity rule, which exists to keep the two _toolkit_ plugins application-agnostic. A game-knowledge product cannot obey it and exist.

Scope is code mods. Loading assets _from code_ belongs here; authoring meshes, textures, maps and editor content does not.

The plugin sits above the two toolkit plugins and may point at them, always softly: `unity-devtools` to settle a question against the running game, `coherent-gameface` for the UI engine underneath the frontend.
Every skill works unchanged when neither is installed.

Four disclosure tiers, in order of cost: skill descriptions, then `SKILL.md` bodies, then references, then the local decompile and mod corpus grepped on demand.
A fact earns its tier by how many readers need it, and the trunk skills carry only what belongs to no single reference.

## The shipped-prose contract

Everything under `skills/` is a deliverable held to these rules. Load the `writing-great-skills` skill before writing any of it.

- **Self-sufficient.** An agent with no local decompile, no running game and no network still gets correct answers. Every other source is an accelerator.
- **The decompile is ground truth.** A claim from any other source is verified against it before it ships; where they disagree, the decompile wins and the prose says so.
- **One sentence per line**, as the sibling plugins' skills do.
- **The mods corpus is input, never output.** It is where techniques and gotchas were learned, and knowledge prose states the technique on its own authority. The single place a mod is named is the setup skill's provisioning catalog, `skills/cs2-modding-setup/references/mod-catalog.md`, which is also the only name list the content lint reads; that file documents the entry shape the lint parses.
- **Libraries stay unnamed.** Teach the mechanism so an agent can always write the code itself, rather than pointing it at a dependency whose current shape it cannot verify. This governs libraries a mod would _reference_ — community helpers, utility packages, UI toolkits. Components the game or the official toolchain already ships are named as plainly as any game type: an agent cannot write the code that is already there. So are the applications a user runs on their own machine — a decompiler, an editor, an IDE — because a procedure has to say which program to run.
- **Version baseline.** Every reference carries, once, a line reading `Verified against game version <version>.`, so a reader can judge its age against the installed game.
- **Volatility marker.** A claim that rots — component field names, system names, save-format versions, UI module paths, raycast mask combinations — carries `VOLATILE:` inline, naming what to re-check and how: `(VOLATILE: the field names on this component — re-read it in the decompile.)`. That uppercase token is the only spelling, and durable architectural facts carry none, so `VOLATILE:` greps into the maintenance checklist for the next game version.

## Reference families

References nest under the trunk in two families, because the sources decompose along two orthogonal axes and both are real.

- **Technique** references teach mechanism reusable across subject matter: lifecycle and loading, ECS as this game uses it, prefabs and assets, tools, settings and localization and input, serialization and save migration, patching, performance and memory, diagnostics, navigating the decompile.
- **Domain** references teach what the game simulates in one area, name the components and systems carrying it, and state the game's own numbers and relationships.

The boundary is the question a fact answers: _how do I do this at all_ is technique, _what does the game model here and where does it live_ is domain.
The bridge between them is the product — no other source connects "here is the ECS" to "here is how this part of the city works" with "therefore, to change X, modify Y" — so the two families cross-reference: a domain reference points at the techniques a change there needs, a technique reference points at the domains that exercise it.

## Guarded local-source access

Every entry into a local source is conditional, so an agent never greps a path that does not exist.

- **The decompile and the mod corpus**: both roots live in the record that `cs2-modding-setup` owns and every other skill reads before touching a local source. Finding no root recorded, route the user to that skill rather than guessing a path. Its "The record" section is the single source for the file's location, format and rationale.
- **The wiki**: capability-agnostic. Try a web-fetch tool, expect a plain HTTP fetch to come back with the site's JavaScript bot challenge instead of content, and ask the user as the last resort.
- **The running game**: available only when the sibling Unity plugin is installed and the user's game is patched for debugging, which is what the setup skill's debug patching provides.
