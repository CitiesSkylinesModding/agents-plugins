# The mechanics reference shape

The form every reference under `plugins/cs2-modding/skills/cs2-modding/references/mechanics/` takes.
Read it before authoring or editing one; [ADR 0005](../adr/0005-every-reference-is-read-beside-the-decompile.md) is why the family has a shape of its own, and the plugin's `AGENTS.md` carries the rules this one sits under — the prefab-value rule most of all, which decides what a map row must state.

A mechanics reference **orients**: it maps what the game models in one area onto the components, fields and systems carrying it, and routes the reader to the code deciding the rest.
What a system does across its branches is a read and the reader has the code, so the prose that over-produces is the sketch and the traps, and a sentence doing neither of their jobs is cut.

## Every file in the folder

Each carries its title, its own version baseline, one blank line, and then the three-line decompile warning the plugin's `AGENTS.md` fixes. Its middle line follows each family's own rule elsewhere; here it resolves to one sentence, because it is true of every file in the family, and `scripts/check-skill-content.ts` asserts that exact sentence on any file in this folder and is authoritative where this block and the script disagree:

```markdown
**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.
```

**Over sixty prose lines per file warns and over a hundred fails**, over every file in the folder, siblings included — `scripts/check-skill-content.ts` holds the live thresholds and is authoritative where a run disagrees with this line.
A prose line is one that is not blank, not a heading, not a table row and not inside a fence.
A topic that needs the room spends sibling files rather than a longer one.

## The entry file

The entry file carries these sections after its warning, in this order:

- **A sketch** — what the game tracks here and how the pieces connect, in a paragraph.
- **`## The map`** — concept to component, field and system, in one table or several where the concepts group, each row carrying the access shape the prefab-value rule requires.
  A table's preamble may state the default read its rows share; a row then states its own only where it differs.
  Before a row names a `*Data` component for a value the simulation consumes, read [a prefab value read where the simulation reads an instance](../solutions/prefab-data-read-where-the-simulation-reads-an-instance.md): the prefab twin and the instance twin share a name, and the row that names the wrong one sends every reader after it to a number the citizens never receive.
  And before a trap or a row treats a parameter component as written once, read [retuning a parameter component the game mode rewrites](../solutions/retuning-a-parameter-component-the-game-mode-rewrites.md): whether the loaded game mode rebuilds it on load is authored asset data, invisible to any code read.
  A prefab class's own field initializer is a Unity-serialized default the shipped asset overrides, not a C# constant: it ships as the field, never as the figure (ruled in `docs/research/conflicts.md`; the test is what consumes the value).
  The access-shape cell carries the read, never a writer roster.
  A row whose value another topic owns carries "belongs to `<topic>`" in that cell, in place of the read or beside it where only part of the row routes — that fixed phrase is the only routing the column admits, so a lint or reader can tell the two apart.
- **`## Traps`** — what a reader gets wrong by opening the file the map sends them to.
- **`## Formulas`** — the expressions, transcribed from the C#. A topic whose expressions all live in siblings omits the section rather than duplicating one.
- **`## Mechanisms`** — the vanilla system owning each, and a link to the sibling holding its listing, or "below" where the entry file itself carries it.
- **`## Bridges`** — the techniques a change here needs, and the adjacent mechanics topics.

A section that outgrows the entry file discloses into a sibling and links it from its own heading, which is what keeps every sibling reachable.

## Traps

**A trap is a bolded claim, at most two lines of why, and a `Source:` line naming the game files that prove it:**

```markdown
**A stale `m_Workplace` earns nothing, not even the unemployment fallback.**
Being paid requires the citizen to appear in the workplace's `Employee` buffer, not merely to hold a `Worker` pointing at it.
Source: `src/Game/Game.Simulation/PayWageSystem.cs`.
```

Write one only where an agent that opened the type the map sent it to would not find it by itself.
In a sibling, a trap sits beside the listing it traps on; `## Traps` groups only those attached to no one listing.
A trap whose sources span a listing and code outside it counts as attached to that listing.
The `Source:` line is what a fact-checking pass opens, so it names files rather than a mechanism — a trap that explains itself and points at nothing openable reads as evidence while carrying none.

## The mechanical pass comes first

**Before the reference is read as prose, someone other than its author opens every `Source:` line to confirm its claim and diffs every listing against the method it models — authoring cannot audit its own claims.**
Those have right answers and converge in one pass, so the prose review that follows spends its rounds on the sketch and the traps rather than on what a decompile read settles.

## Listings

**A listing is pseudo-code in a language-less fence**: the shape of the code, the real symbols, every branch that changes the result, and plain English only behind a placeholder saying so.
The branches transcription loses are the default arms, the guards placed after the value they appear to gate, and the debug overrides that write last — check those before handing over.
It names its source files above it and lives in its own sibling file, and a mechanism earns one where piecing it together means opening three or more of them.
Pseudo-code rather than the decompiled C#: shipping the game's own code is the publisher's call rather than this plugin's.
A formula two listings both model is transcribed in both: a listing models its method whole, and neither reader should need the other sibling.

## Markers

**One `VOLATILE:` marker covers a file's names** — the map's components and fields, the symbols its traps and formulas name, and the `Source:` paths with them — stating the declarations they all come from.
Every name in a mechanics file rots at the same rate and closes on the same sweep, so a marker per section restates one fact until it reads as wallpaper.
A sibling carries its own, and `UNVERIFIED:` stays per claim.
An edit that names a type from a new namespace is a marker edit: re-close the marker's location list against everything the file now names, in every file the pass touched.
