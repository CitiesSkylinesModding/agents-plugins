# AGENTS.md

## Plugin overview

`cs2-modding` teaches an agent to write **Cities: Skylines II code mods**.
It is knowledge only: skills and references, no MCP server, no runtime, no shipped code artifacts — no scaffolds, no templates, no generators.
It names the game throughout, a deliberate carve-out from the repository's genericity rule: that rule exists to keep the two _toolkit_ plugins application-agnostic, and a game-knowledge product cannot obey it and exist.

Scope is code mods on Windows.
Loading assets _from code_ belongs here; authoring meshes, textures, maps and editor content does not, and neither does developing on Linux — nobody does, though the build's own macOS and Linux outputs stay documented, since the toolchain emits them unprompted.

The plugin sits above the two toolkit plugins and may point at them, always softly: `unity-devtools` to settle a question against the running game, `coherent-gameface` for the UI engine underneath the frontend.
Every skill works unchanged when neither is installed.

Four disclosure tiers, in order of cost: skill descriptions, then `SKILL.md` bodies, then references, then the local decompile and mod corpus grepped on demand.
A fact earns its tier by how many readers need it, and a trunk body carries what belongs to no single reference — plus the handful a trunk reader cannot act without, which are stated there in a paragraph and developed in their reference.
What may never move down is a rule whose absence makes a reader **wrong** rather than shallower — a pointer carries depth, and a reference stays self-sufficient without following one.

## The shipped-prose contract

Everything under `skills/` is a deliverable held to these rules.

- **Self-sufficient.** An agent with no local decompile, no running game and no network still gets correct answers. Every other source is an accelerator.
- **First-party is ground truth, and the decompile is only half of it.** A claim from any other source is verified against the game itself before it ships, and where they disagree the game wins and the prose says so.
  The decompile answers for anything C# names. The user's own install answers for everything else — the compiled string tables, the packaged content, and the whole frontend, which ships as a plain JavaScript bundle. Some subjects are almost invisible from C#, so a grep of `src/` that comes back empty settles nothing on its own.
- **One sentence per line**, as the sibling plugins' skills do.
- **The mods corpus is input, never output.** It is where techniques and gotchas were learned, and knowledge prose states the technique on its own authority. The single place a mod is named is the setup skill's provisioning catalog, `skills/cs2-modding-setup/references/mod-catalog.md`, which is also the name list the content lint reads — so the entry shape `scripts/check-skill-content.ts` parses is a contract the catalog keeps, and an entry losing its `###` heading or its `Source:` line fails the check rather than escaping it.
  When the check names a word your subject genuinely owns, report the collision and stop: the fix is the maintainer adding that word to the lint's ordinary-word list, and never prose bent around it. A word already on that list warns instead of failing, so a green run still carries the collision.
  **An entry certifies what its own source shows.** Reading one repository establishes nothing about the others, so a comparative — _the only_, _the deepest_, _the fullest_ — is a claim about the whole corpus and ships only with the sweep that established it. A superlative is also the hardest kind of sentence to doubt on re-reading, which is why this gates writing one rather than cautioning you to weigh it.
- **Libraries stay unnamed, except Harmony.** Teach the mechanism so an agent can always write the code itself, rather than pointing it at a dependency whose current shape it cannot verify. This governs libraries a mod would _reference_ — community helpers, utility packages, UI toolkits. Components the game or the official toolchain already ships are named as plainly as any game type: an agent cannot write the code that is already there. So are the applications a user runs on their own machine — a decompiler, an editor, an IDE — because a procedure has to say which program to run.
  Harmony is the one referenced library this plugin names, because it is the ecosystem's only patching runtime and its API is the vocabulary any patching prose has to use — teaching the mechanism here would have everyone writing their own patcher, the one outcome that rule must not produce. Name it wherever the prose is about patching, the patching reference most of all and `skills/cs2-mod-project/SKILL.md` for the dependency, and teach its own prefix, postfix and injected-parameter vocabulary rather than the mechanism under it.
  Its package id and pinned version stay in `skills/cs2-mod-project/SKILL.md` and nowhere else, so that moving the pin is a one-place edit. Every mod ships its own copy of the library and the game collapses same-named assemblies into a single loaded winner, so a mod pinning a different version can become the copy every other mod patches through — which is why the version is agreed rather than chosen per project.
- **A count ships only where the count is the thing being taught.** A reader acts on _most adds are not generic_ and never on the two figures behind it, so a supporting figure is load that rots and that every version sweep has to re-earn. State the shape and drop the number.
  Where the number _is_ what a reader came for — a mechanics reference's own quantities, a phase set, a version string — it stays, and it carries the census discipline [a search taken for a census](../../docs/solutions/empty-grep-read-as-proof-of-absence.md) states. A wrong count reads as precision, which is how it survives a review that would have caught a vague sentence, so this too gates writing one.
- **Version baseline.** Every reference carries, once, a line reading `Verified against game version <version>.`, so a reader can judge its age against the installed game.
- **Volatility marker.** A claim that rots — component field names, system names, save-format versions, UI module paths, raycast mask combinations — carries `VOLATILE:` inline, naming what moves and where it lives: `(VOLATILE: the field names on this component — the component's own declaration.)`. That uppercase token is the only spelling, and durable architectural facts carry none, so `VOLATILE:` greps into the maintenance checklist for the next game version.
  **A marker is a label, and reads as one.** An imperative — _re-read this_, _check that_ — is an order an agent obeys on sight, spending a reader's context re-deriving claims that were right. Name the thing that moves, so the next version's sweep has its list, and name where it lives as a location rather than an errand — a type, a namespace, a region, or the game's own file where the claim is not a C# one, since the sweep has to be able to open what the marker names. The trunk `SKILL.md` owns the reason: it states the four triggers that make re-deriving worth it, once, for every marker in the plugin.
  **A marker nobody can close is noise on the checklist, so propose dropping it rather than rewording it.** The token earns its place by grepping into the next version's work, so a `VOLATILE:` claim a version sweep cannot clear — a mod's own source, anything outside the game — was never this token's to carry, and an `UNVERIFIED:` claim whose settling experiment is impractical buys a reader nothing but doubt. Offer the drop as the first option and let the maintainer keep it.
  **Where the experiment is cheap, run it instead of writing the marker.** A marker costs every future reader a little doubt and the next maintainer a sweep entry, so a question one minute against the running game can answer is not worth either. The answer also tends to beat the doubt, turning a sentence that would have taught nothing into a technique a reader acts on.
- **Evidence marker.** A claim the pipeline reached but could not confirm carries `UNVERIFIED:` inline, naming what went unconfirmed and what would settle it: `(UNVERIFIED: whether this is safe in a running city — nobody has run it.)`. That uppercase token is the only spelling, and the lint asserts it the same way it asserts the other.
  **It answers a different question from `VOLATILE:`.** A volatile claim was established and will rot; an unverified one was never established. Both grep into the next version's work — one into what to re-derive, the other into what to confirm — and a maintainer with a running game can only sweep for the second if it has a token.
  State the claim in the prose's own voice and attach the marker, rather than hedging the sentence around it: hedged phrasings — _ships as observed practice_, _is not established_ — are indistinguishable from one another and findable by no grep. Where a claim is not merely unconfirmed but genuinely unknowable from the sources, that is a `conflicts.md` entry and not a marker.

Every marker you add or remove, of either kind, goes in your closing message to the user, quoted, so they can rule on it there rather than by reading the diff.
The maintainer owns which claims count, and calls that look settled from inside one file are the ones they overturn.

## Fact-checking is its own pass

The agent that wrote a claim cannot audit it, so verifying it is `/review-gate`'s job rather than a private re-read at the end of authoring.

**Brief the finders on the sources, not on the prose.**
They re-derive each claim from the primary sources — the decompile first, then the installed toolchain and the game's own files — and return the line that proves or disproves it.
A finder told only to review the prose reads it for plausibility, which is how it was written in the first place.
Where a claim rests on a live-game run, name the captured logs as the source and say not to re-run it: "re-derive from the primary sources" reads as "reproduce the experiment" to a finder holding no other instruction, and it will rebuild a probe and reach for the game within minutes.
Say instead that an honest gap is the useful answer — where the captured evidence cannot settle a claim, the finder names what would settle it and moves on.
Where the prose prescribes a search, hand the finder the search and ask what it misses: a finder asked to re-derive claims checks whether sentences are true and never whether commands work, so the recipes pass untouched through a pass that re-verifies every count beside them.

**`/simplify` runs before the gate.**
Simplifying afterwards re-opens reviewed prose, which earns another gate: simplify-then-gate converges, gate-then-simplify loops.
Aim it at what authoring overproduces — how a conclusion was reached, a rule restated in the file that does not own it, a count or a date standing where an invariant belongs.

**A green `mise check:skill-content` says nothing about whether a reference is right.**
It checks marker spellings, the version baseline, link resolution, whether a disclosed file is linked at all, and catalogued mod names — the shapes a script can see.
Counts, guard conditions, failure modes and a claim contradicting a sibling all pass it untouched, so the gate is the only thing between a plausible sentence and a wrong one.

**Aim the pass at over-reach, because that is what authoring produces:** a mechanism inferred from one observation, a rule generalised from the cases that happened to be checked, a diagnostic mistaken for the thing it reports on.
Prose that has gone through a gate has been wrong on exactly these, and none of it read as doubtful.
Read these before re-deriving anything: [a search taken for a census](../../docs/solutions/empty-grep-read-as-proof-of-absence.md), and [a read that stopped where the code agreed with it](../../docs/solutions/decompile-read-stopped-at-the-confirming-line.md).
Over-reach usually enters one stage before the prose carrying it: a reference is written by an agent holding a research file and no source, so a guard, a condition or a scope dropped while the research was written down is copied into the shipped file faithfully, and reads as well there as it did in the research.
Aim finders at the research file's own citations as much as at the prose — open the line a claim rests on and read what surrounds it, rather than re-deriving only what the shipped sentence already says.

**A reference contradicting a shipped sibling is the normal output of partitioning one subject:** the slices overlap, and a topic that owns a mechanism reads its source more closely than one that merely mentions it.
Fix it in the pass that found it — re-derive the claim from the decompile, then correct every file carrying the wrong version, a reference whose own work is long finished included.
Deferring the correction to a follow-up drops it.

**Corrections earn another `/review-gate`.**
A rewritten passage is new prose, and the round that fixes the most is the round that introduces the most.
Derive a correction from the source, never from the sentence it replaces: reading the shipped line to write its fix reproduces whatever that line got wrong, now wearing a correction's authority.
Over-correction is how the round goes wrong — a vague rule rewritten into a precise one that is wrong, a permission narrowed to a whitelist tighter than what was ruled, a mechanism invented to justify a rule that was already justified — so prefer scoping or restoring the sentence you have over generalising it, and where a fix needs a claim the sources do not carry, drop the claim rather than the fix.
The shape of a fix predicts whether it holds: one that takes something away survives its own review, and so does a mechanical one applied uniformly across a file, while one that adds precision — a count, a mechanism, a rationale, a worked example — is what the next round finds wrong, so give those the scrutiny of the prose they replace.
A recipe stated twice takes a fix only when both copies change: a reference explains a search where the topic owns it and prescribes it again in the summary a reader executes from, so fixing the explanation alone leaves the file carrying its own counterexample.

**A claim that comes back wrong twice is a claim to delete rather than rephrase.**
Each round fixes the number and leaves the sentence, because the sentence reads as the thing the passage needs.
Ask what the surrounding prose loses if the claim simply goes; where the answer is nothing, that is the fix.
Where the answer is something, count the complement instead: a count that keeps coming back wrong usually counts a set with a contested edge, so every pass draws the boundary somewhere new, and the stable form is the other side of the same fact — a container tally that ran five, then three, then six settled as _exactly two of that library's types carry an asynchronous dispose_.

## Reference families

The trunk skill's references nest in two families, one directory each, because the sources decompose along two orthogonal axes and both are real.
Only the trunk splits this way; every other skill keeps a flat `references/`.

- **Technique** references, in `skills/cs2-modding/references/technique/`, teach mechanism reusable across subject matter.
- **Mechanics** references, in `skills/cs2-modding/references/mechanics/`, teach what the game simulates in one area, name the components and systems carrying it, and state the game's own numbers and relationships.

The boundary is the question a fact answers: _how do I do this at all_ is technique, _what does the game model here and where does it live_ is mechanics.
The bridge between them is the product — no other source connects "here is the ECS" to "here is how this part of the city works" with "therefore, to change X, modify Y" — so the two families cross-reference: a mechanics reference points at the techniques a change there needs, a technique reference points at the mechanics it serves.

**Every reference is a folder, and its entry file repeats the topic name.**
`references/technique/custom-tools/custom-tools.md` is the reference a bridge slug names and a pointer resolves to; anything else the topic discloses sits beside it, in that same folder, under its own name.
**The entry file links every sibling in its folder**, because it is the only place a reader can arrive from — a disclosed file nothing links to ships in every install and is read by nobody, and the lint asserts both halves.
A topic that discloses nothing keeps the folder anyway — it is one file in a directory of its own, and that is the point: **disclosing later is then a new file rather than a move**, so no pointer, slug or ticket path changes when a reference outgrows one file.

Both families work this way, and so does the UI skill's flat `references/`.
A sub-file is a reference like any other: it carries its own title and its own `Verified against game version <version>.` line, because it goes stale on its own, and the content lint already treats any `.md` below a `references/` directory as a reference at any depth.
Link to a sibling in the same folder by bare filename; a bridge to another _topic_ stays a backticked slug and never a link.

## Guarded local-source access

Every entry into a local source is conditional, so an agent never greps a path that does not exist.

- **The decompile, the mod corpus and the readable copy of the UI bundle**: the user chose where each of these lives, so no environment variable finds them and the record that `cs2-modding-setup` owns is the only thing that does. Read it before touching one; finding no root recorded, route the user to that skill rather than guessing a path. Its "The record" section is the single source for the file's location, format and rationale.
- **The installed game itself**: the string tables, the packaged content and the shipped UI bundle, all under the install root. The toolchain sets environment variables naming it and the paths beneath it, so a skill reads one rather than hardcoding a path, and treats a missing variable as the signal to ask rather than to guess. This is the source a topic reaches for when its subject matter ships as data or as JavaScript, and the one the decompile cannot stand in for. The bundle ships minified to a single line, so what a skill actually reads is the reformatted copy above; the shipped file is what that copy is made from and what a version check is run against.
- **The official toolchain**: its build targets, and the UI mod scaffold whose template declares every module a UI mod may import. Neither sits under the install root — the targets come down as a package and the scaffold is installed globally by the toolchain — so this is the one local source no install-relative path reaches. `skills/cs2-mod-project/references/build-pipeline.md` names the environment variables that locate what has one, and the scaffold has none: ask the user where it landed rather than searching the game folder for it.
- **The wiki**: capability-agnostic. Try a web-fetch tool, expect a plain HTTP fetch to come back with the site's JavaScript bot challenge instead of content, and ask the user as the last resort.
- **The running game**: available only when the sibling Unity plugin is installed and the user's game is patched for debugging, which is what the setup skill's debug patching provides.
