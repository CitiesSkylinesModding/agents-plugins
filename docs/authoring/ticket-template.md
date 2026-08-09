# The ticket template

The body shape a ticket takes, whatever the feature.
`docs/agents/cantrips-loop.md` fixes the storage, and this fixes what goes inside.
A `cs2-modding` reference ticket adds the obligations [the reference-ticket protocol](reference-ticket-protocol.md) states on top.

```markdown
# NN — <the work, named as an outcome>

**What to build:** …

**Blocked by:** … **Blocks:** …

**Read with this ticket:** …

## The work

1. …

## Acceptance criteria

- [ ] …

## The trap this ticket is most likely to fall into

**<the temptation, named>.** …
```

Each piece, with an example in the shape that has worked here:

**What to build** states what exists after the ticket that did not before, and the boundary that keeps the work honest:

> **What to build:** shape conformance across every shipped file under `references/technique/`, per the family's shape doc.
> No new claims and no re-verification of existing ones: this ticket adds the anchors and the disclosure the shape requires of prose that already passed its gates.

The second sentence is the load-bearing one — a scope bound stated in the ticket is what stands between "conform the files" and an agent improving prose nobody asked it to touch.

**Blocked by / Blocks** are prose, and every edge carries its reason — an edge whose reason travels survives renumbering and re-planning, where a bare number gets re-derived or dropped:

> **Blocked by:** NN — the inspection resweep, and NN — the docs resweep: they edit the same files, and conforming before them would re-open their diffs. **Blocks:** NN — the trunk-coherence pass, as any ticket that edits shipped references does.

The `Blocked by` side is the record the loop config reads; a `Blocks` line is derived from it, and a renumbering re-derives both.

**Read with this ticket** names the standing docs the work runs under, most specific first, each with why it binds — and says which of their obligations this ticket switches off, since silence licenses all of them:

> **Read with this ticket:** the family shape doc first, then the reference-ticket protocol for the orchestrator conventions. This is not a two-stage reference ticket — no discovery pass runs, because the research files already hold the citations this work consumes.

**The work** is numbered outcomes rather than steps — what to produce, each verifiable on its own. Where an item rests on a measurement the agent cannot cheaply redo, hand the measurement over and say the sweep is still theirs:

> 4. **The missing closing sections.** Every entry file closing without `## What this reference hands to others` gains one, assembled from what the file already states rather than from new claims. The settling measurement found three — `mod-compatibility`, `patching`, `performance-and-memory` — but sweep the folder, since earlier tickets edit the same tree first.

**Acceptance criteria** are the contract: each checkable by a reader who did not do the work, and exhaustive — quantified over a set, never "improved". A criterion that rests on a judgment call routes the judgment's record into the closing message, so the maintainer rules on it there rather than by reading the diff:

> - [ ] Every shipped technique file was read whole, and every trap in it either carries a `Source:` line, carries `UNVERIFIED:`, or was judged a pattern owing no source — with the judged-pattern cases listed in the closing message.
> - [ ] No `Source:` path was written from memory or from prose: each came from a research-file citation or a fresh derivation, and the closing message says which files needed fresh derivations.
> - [ ] `mise check` passes.

The last one names the project's checks by command; a "tests pass" that names no command gates nothing.

**The trap** section is optional and earns its place where the spec already knows the likely failure — name the temptation, then the rule that beats it:

> **Upgrading prose while anchoring it.** The claims being anchored have passed review; the temptation is to sharpen a sentence while adding its `Source:` line, and a sharpened claim is new prose wearing a reviewed claim's authority. Add the anchor to the sentence as it stands.

Two rules the examples above encode without a section of their own:

- **State rules, not snapshots.** A live count in the body goes stale before the ticket resolves; state the invariant, and pin any figure to the measurement that produced it — as the closing-sections example does.
- **Shared obligations live once.** A rule every ticket of a run must satisfy goes in a standing doc the tickets point at, never stamped into each; the ticket carries one criterion ticking that list, so the pointer binds.
