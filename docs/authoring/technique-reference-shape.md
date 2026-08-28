# The technique reference shape

The form every reference under `plugins/cs2-modding/skills/cs2-modding/references/technique/` takes, and every one under the UI skill's flat `cs2-modding-ui/references/`, which the content lint holds to the same budget.
Read it before authoring or editing one; [ADR 0007](../adr/0007-the-technique-family-takes-a-light-shape.md) is why this family's shape is not the mechanics one, and the plugin's `AGENTS.md` carries the rules this one sits under.

A technique reference **teaches mechanism the game's own files never state**: the procedure, the order to try alternatives in, the contract a mod's code must satisfy, the trap that makes the obvious move wrong.
Its prose interleaves premise and consequence deliberately — a line about what the code does earns its place welded to the move a reader makes because of it, and a passage narrating the code with no consequence attached is the one to disclose or cut.

## Every file in the folder

Each carries its title, its own version baseline, one blank line, and the three-line warning the plugin's `AGENTS.md` fixes, with the middle line written from the file — no lint-asserted sentence like the mechanics family's.
The family's default middle line is `The technique holds without one, but every game symbol named below is checkable only there.` — true wherever the decompile is only the check on the symbols the file names, which is most of the family.
A file it is not true of — one that is wholly a read of the tree, or grounded in the install or the frontend bundle — writes its own; carrying the default there is the borrowing the plugin's `AGENTS.md` forbids.

**Over three hundred prose lines per file warns and over four hundred fails**, over every file in the folder, siblings included — `scripts/check-skill-content.ts` holds the live thresholds and is authoritative where a run disagrees with this line.
A prose line is one that is not blank, not a heading, not a table row, not a `Source:` anchor and not inside a fence.
Anchors are excluded for the reason headings are: the budget measures what an author over-produces, and an anchor is mandated one per trap rather than chosen.
The budget guards growth rather than prescribing cuts: it sits above everything the settling measurement cleared as healthy, so a file crossing the warn line is a question about what grew — and where the growth is real, the answer is the disclosure rule below, not cuts.

## Sections

**There is no fixed section list**: the headings are the topic's own, because technique topics do different jobs — a navigation guide, a patching manual, a diagnosis procedure — where every mechanics topic does the same one.
Two pieces are fixed.
The header block above, and an entry file closing on `## What this reference hands to others`.
That section carries the bridges; a sentence naming what a reader leaves with is welcome and optional, and most of the family closes on bridges alone.

## Traps

A trap here is a bolded claim standing beside the context that arms it, not a corralled section — detaching it from its premise is what would make it miss.

**A trap whose claim rests on game behaviour carries a `Source:` line naming the first-party artifact that proves it:**

```markdown
**A `Loaded` line on its own proves nothing.**
The timer wrapping the load reports from a `finally`, so a mod that threw produces the same success-shaped line.
Source: `src/Game/Game.Modding/ModManager.cs`.
```

The anchor is whatever first-party artifact the claim is checkable against: a decompile path, an install artifact, the frontend bundle, the official toolchain's shipped files, or Harmony's own source — the one nameable library.
Where the decompile's half is an `extern` — engine semantics native code owns — the anchor adds the engine's own documentation at a version-pinned URL beside the decompile path that shows the delegation, the pin matching the version the game ships — the engine's for engine manuals, the package's for package docs.
A parenthetical on the `Source:` line may state what each artifact proves, and nothing else — model content a reader acts on belongs in the body.
**The anchor is per trap even where a run of traps shares one artifact**, so a section resting on a single file repeats that file's path under each of its traps; that repetition is deliberate, because the mechanical pass below opens anchors one at a time and a trap that borrows its neighbour's is a trap with nothing to open.
**A claim settled against a running game, with no first-party artifact that shows it, carries neither anchor.**
It names the run in the body instead — "in a captured run", or whatever names the observation — because a reader checks it by running the same thing, not by opening a file, and there is no third marker.
`UNVERIFIED:` is the wrong word for it: that marker means _not settled_, and this claim was.
Where such a claim sits beside one the decompile does show, the `Source:` line anchors that half and the body carries the run for the other.
A claim about mod-to-mod behaviour anchors to the mediating game surface — the delegate field both mods write, the shared resource host, the manager that merges — which is game code even when the collision is between mods.
**A trap provable only by running the game, and not yet run, carries `UNVERIFIED:` in place of the `Source:` line**; that is that marker's job, and the two never appear together as a trap's anchor — a per-claim `UNVERIFIED:` on one unsettled sub-claim may sit in a paragraph whose other claims a `Source:` line anchors.
A bolded rule whose truth is the pattern itself — a design discipline the game contains no code for — owes no source, because there is nothing to open.

## Disclosure

**A self-contained account a reader consults rather than reads through — a census, an engine walk, a catalog — discloses into a sibling file, and the entry file keeps the consequence and the route to it.**
The bound on what may move stays the plugin `AGENTS.md`'s: a claim whose absence leaves the reader wrong rather than shallower stays in the entry file.

## The mechanical pass comes first

**Before the reference is read as prose, someone other than its author opens every `Source:` line to confirm its claim — authoring cannot audit its own claims.**
This family has no listings to diff, so the pass is the `Source:` lines alone.

## Markers

**`VOLATILE:` marks per claim, naming the location its names come from** — a type, a namespace, a region; a version sweep works from a location, and a file here names symbols from many places that rot at different rates, which is why the mechanics family's one-marker-per-file rule does not transfer.
`UNVERIFIED:` stays per claim, and doubles as the trap anchor above.
