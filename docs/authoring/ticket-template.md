# The ticket template

The body shape a ticket takes, whatever the feature.
`docs/agents/cantrips-loop.md` fixes the storage, and this fixes what goes inside.

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

> **What to build:** a small script that, given two commits of the local decompile, names each marked claim as changed, missing, unchecked or clean, with the plugin server's source as its first real input.
> No skill body is edited and nothing is deleted: this ticket fixes the marker's spelling and proves the check on markers that already exist.

The second sentence is the load-bearing one — a scope bound stated in the ticket is what stands between "build the check" and an agent rewriting the skills the check will read.

**Blocked by / Blocks** are prose, and every edge carries its reason — an edge whose reason travels survives renumbering and re-planning, where a bare number gets re-derived or dropped:

> **Blocked by:** None (can start immediately). **Blocks:** NN — the lean bodies, whose traps are written with the marker this ticket fixes; and through it NN — the purge.

The `Blocked by` side is the record the loop config reads; a `Blocks` line is derived from it, and a renumbering re-derives both.

**Read with this ticket** names the standing docs the work runs under, most specific first, each with why it binds — and says which of their obligations this ticket switches off, since silence licenses all of them:

> **Read with this ticket:** the spec beside it, sections "The trap check", "Where traps live" and "Test Seams". The decompile's location is in the setup record; its history holds re-decompile commits between game versions, and a diff never spans one.

**The work** is numbered outcomes rather than steps — what to produce, each verifiable on its own. Where an item rests on a measurement the agent cannot cheaply redo, hand the measurement over and say the sweep is still theirs:

> 5. **The server's markers.** Every version marker in the plugin server's source takes the new form; one naming a namespace or a bare word, where no type can be named, is marked as resting on no symbol. The settling run of the check listed eleven, across five files, but sweep the folder, since another ticket may edit the same source first.

**Acceptance criteria** are the contract: each checkable by a reader who did not do the work, and exhaustive — quantified over a set, never "improved". A criterion that rests on a judgment call routes the judgment's record into the closing message, so the maintainer rules on it there rather than by reading the diff:

> - [ ] Run over a pair of decompile commits that differ by a game update alone, the check's changed list equals the hand-made intersection of the decompile's own name diff with the files the server's markers resolve to; the closing message shows both lists.
> - [ ] No old-form version marker remains in the server's source, and every server marker is either resolved or reported unchecked, with the unchecked ones listed in the closing message.
> - [ ] `mise check:agents` passes.

The last one names the project's checks by command; a "tests pass" that names no command gates nothing.
A criterion on a hand-off walks from the trigger to the step it promises: "the message names the skill" passes a pointer whose target stops the run before that step.

**The trap** section is optional and earns its place where the spec already knows the likely failure — name the temptation, then the rule that beats it:

> **Growing the script into an audit.** A check that learns one more special case per sweep never stops growing. This one resolves a symbol, tests that it exists, and tests it against a name diff; a case it cannot decide is reported as unchecked, never handled.

Two rules the examples above encode without a section of their own:

- **State rules, not snapshots.** A live count in the body goes stale before the ticket resolves; state the invariant, and pin any figure to the measurement that produced it — as the server-markers example does.
- **Shared obligations live once.** A rule every ticket of a run must satisfy goes in a standing doc the tickets point at, never stamped into each; the ticket carries one criterion ticking that list, so the pointer binds.
