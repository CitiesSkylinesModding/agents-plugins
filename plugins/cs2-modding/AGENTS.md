# AGENTS.md

## Plugin overview

`cs2-modding` teaches an agent to write **Cities: Skylines II code mods**.
It is knowledge only: skills and references, no MCP server, no runtime, no shipped code artifacts — no scaffolds, no templates, no generators.
It is also the one plugin here that names a game on purpose, a deliberate carve-out from the repository's genericity rule, which exists to keep the two _toolkit_ plugins application-agnostic. A game-knowledge product cannot obey it and exist.

Scope is code mods on Windows. Loading assets _from code_ belongs here; authoring meshes, textures, maps and editor content does not, and neither does developing on Linux — nobody does, though the build's own macOS and Linux outputs stay documented, since the toolchain emits them unprompted.

The plugin sits above the two toolkit plugins and may point at them, always softly: `unity-devtools` to settle a question against the running game, `coherent-gameface` for the UI engine underneath the frontend.
Every skill works unchanged when neither is installed.

Four disclosure tiers, in order of cost: skill descriptions, then `SKILL.md` bodies, then references, then the local decompile and mod corpus grepped on demand.
A fact earns its tier by how many readers need it, and a trunk body carries what belongs to no single reference — plus the handful a trunk reader cannot act without, which are stated there in a paragraph and developed in their reference.

## The shipped-prose contract

Everything under `skills/` is a deliverable held to these rules. Load the `writing-great-skills` skill before writing any of it.

- **Self-sufficient.** An agent with no local decompile, no running game and no network still gets correct answers. Every other source is an accelerator.
- **The decompile is ground truth.** A claim from any other source is verified against it before it ships; where they disagree, the decompile wins and the prose says so.
- **One sentence per line**, as the sibling plugins' skills do.
- **The mods corpus is input, never output.** It is where techniques and gotchas were learned, and knowledge prose states the technique on its own authority. The single place a mod is named is the setup skill's provisioning catalog, `skills/cs2-modding-setup/references/mod-catalog.md`, which is also the only name list the content lint reads. That catalog ships to an agent provisioning a corpus and says nothing about the lint: the entry shape the lint parses is a contract `scripts/check-skill-content.ts` states and the catalog only has to keep, so an entry losing its `###` heading or its `Source:` line fails the check rather than escaping it. The lint matches both spellings — the display name and the `owner/repo` slug — whole-word and case-sensitively, at two strengths, because a third of the display names are ordinary words and a mechanics reference heading `## Traffic` trips the match while citing nothing. A slug is a citation and nothing else, and so is a display name by default, so both fail the check. A display name on the lint's ordinary-word list is instead reported as a question naming the file and line, and does not fail: a word the game's own subject matter owns cannot credit anybody, because no reader takes it for a name. When the check fires on a word your subject owns, report the collision and stop — the fix is the maintainer adding that word to the list, and never prose bent around it.
- **Libraries stay unnamed, except Harmony.** Teach the mechanism so an agent can always write the code itself, rather than pointing it at a dependency whose current shape it cannot verify. This governs libraries a mod would _reference_ — community helpers, utility packages, UI toolkits. Components the game or the official toolchain already ships are named as plainly as any game type: an agent cannot write the code that is already there. So are the applications a user runs on their own machine — a decompiler, an editor, an IDE — because a procedure has to say which program to run.
  Harmony is the one referenced library this plugin names, because it is the ecosystem's only patching runtime and its API is the vocabulary any patching prose has to use. Name it wherever the prose is about patching — the patching reference most of all, and `skills/cs2-mod-project/SKILL.md` for the dependency — and teach its own prefix, postfix and injected-parameter vocabulary rather than the mechanism under it. This is the one place where teaching the mechanism would defeat the rule's purpose: the rule exists so an agent can always write the code itself, and here everyone writing their own is precisely the outcome to avoid.
  Its package id and pinned version stay in `skills/cs2-mod-project/SKILL.md` and nowhere else, so that moving the pin is a one-place edit. Every mod ships its own copy of the library and the game collapses same-named assemblies into a single loaded winner, so a mod pinning a different version can become the copy every other mod patches through — which is why the version is agreed rather than chosen per project.
- **Version baseline.** Every reference carries, once, a line reading `Verified against game version <version>.`, so a reader can judge its age against the installed game.
- **Volatility marker.** A claim that rots — component field names, system names, save-format versions, UI module paths, raycast mask combinations — carries `VOLATILE:` inline, naming what moves and where it lives: `(VOLATILE: the field names on this component — the component's own declaration.)`. That uppercase token is the only spelling, and durable architectural facts carry none, so `VOLATILE:` greps into the maintenance checklist for the next game version.
  **A marker is a label, and reads as one.** An imperative — _re-read this_, _check that_ — is an order an agent obeys on sight, so a reference carrying nine of them spends a reader's context re-deriving claims that were right. Name the thing that moves, so the next version's sweep has its list, and name where it lives as a location rather than an errand — the decompile is this plugin's ground truth everywhere, so a marker naming a type, a namespace or a region has already said where. The trunk `SKILL.md` owns the reason: it states the four triggers that make re-deriving worth it, once, for every marker in the plugin.
  Every marker you add or remove goes in your closing message to the user, quoted, so they can rule on it there rather than by reading the diff. The maintainer owns which claims count as volatile, and calls that look settled from inside one file are the ones they overturn.

## Fact-checking is its own pass

The agent that wrote a claim cannot audit it, so verifying it is `/review-gate`'s job rather than a private re-read at the end of authoring.
The gate's finders re-derive each claim from the primary sources — the decompile first, then the installed toolchain and the game's own files — and return the line that proves or disproves it.
Point them at those sources and at the claims to re-derive; a finder told only to review the prose reads it for plausibility, which is how it was written in the first place.

Aim the pass at over-reach, because that is what authoring produces: a mechanism inferred from one observation, a rule generalised from the cases that happened to be checked, a diagnostic mistaken for the thing it reports on.
Prose that has gone through a gate has been wrong on exactly these, and none of it read as doubtful.

A reference contradicting a shipped sibling is the normal output of partitioning one subject: the slices overlap, and a topic that owns a mechanism reads its source more closely than one that merely mentions it.
Fix it in the pass that found it — re-derive the claim from the decompile, then correct every file carrying the wrong version, a reference whose own work is long finished included.
Deferring the correction to a follow-up drops it.

Corrections earn another `/review-gate`. A rewritten passage is new prose, and the round that fixes the most is the round that introduces the most.
The way a correction goes wrong is over-correction: a vague rule rewritten into a precise one that is wrong, a permission narrowed to a whitelist tighter than what was ruled, a mechanism invented to justify a rule that was already justified.
Prefer scoping or restoring the sentence you have over generalising it, and where a fix needs a claim the sources do not carry, drop the claim rather than the fix.

## Reference families

The trunk skill's references nest in two families, one directory each, because the sources decompose along two orthogonal axes and both are real.
Only the trunk splits this way; every other skill keeps a flat `references/`.

- **Technique** references, in `skills/cs2-modding/references/technique/`, teach mechanism reusable across subject matter.
- **Mechanics** references, in `skills/cs2-modding/references/mechanics/`, teach what the game simulates in one area, name the components and systems carrying it, and state the game's own numbers and relationships.

The boundary is the question a fact answers: _how do I do this at all_ is technique, _what does the game model here and where does it live_ is mechanics.
The bridge between them is the product — no other source connects "here is the ECS" to "here is how this part of the city works" with "therefore, to change X, modify Y" — so the two families cross-reference: a mechanics reference points at the techniques a change there needs, a technique reference points at the mechanics it serves.

## Guarded local-source access

Every entry into a local source is conditional, so an agent never greps a path that does not exist.

- **The decompile and the mod corpus**: both roots live in the record that `cs2-modding-setup` owns and every other skill reads before touching a local source. Finding no root recorded, route the user to that skill rather than guessing a path. Its "The record" section is the single source for the file's location, format and rationale.
- **The wiki**: capability-agnostic. Try a web-fetch tool, expect a plain HTTP fetch to come back with the site's JavaScript bot challenge instead of content, and ask the user as the last resort.
- **The running game**: available only when the sibling Unity plugin is installed and the user's game is patched for debugging, which is what the setup skill's debug patching provides.
