# Verifier brief

The second agent a task goes through.
It gets each item without its `Reference` line, and the decompile root as a bare path, so nothing in its context says what the references claim.
An item ruled CONTRADICTED or UNSETTLED leaves the key; a contradicted one is also reported as a defect in the reference.
A cite corrected after authoring, by a review or otherwise, comes from a fresh run of this brief, never from an agent that may have read the references.
Fill the angle-bracket fields and send the whole text as the agent's prompt.
Read every verdict from its quoted evidence, never from its label: `docs/solutions/verifier-verdict-inverted-against-its-evidence.md` records labels flipping against their own quote even under a brief that pins the target.

---

You are verifying claims about how Cities: Skylines II behaves, from its decompiled source alone.
The decompile is at `<decompile root>`, laid out as `src/<Assembly>/<Namespace>/<Type>.cs`; `src/Game` is most of it.
Read nothing else: no other directory, no web, no documentation.
If a claim cannot be settled from the decompile, say so rather than reasoning from what Unity or .NET usually do.

Each item below describes a trap in mod code: a shape that misbehaves in this game (`Falls in`) and the shape that works (`Avoids`), with the line its author cited as proof.

<items: for each, "### <name>", then the Falls in, Avoids and Source lines>

## The work

For every item:

1. Open the cited line and read around it. Then find, in the decompile, the code that decides whether the `Falls in` shape misbehaves as described and the `Avoids` shape does not, which may sit elsewhere than the cite.
2. Rule on the item. **The verdict is on the item's claim**: CONFIRMED means the decompile shows the `Falls in` shape misbehaves and the `Avoids` shape works, as described; CONTRADICTED means the decompile shows otherwise, for either shape; UNSETTLED means the decompile cannot decide it. Done when you hold a quote that proves the verdict on its own.
3. Give the deciding cite as `src/...:line`, the one you would put on the item, whether or not it is the author's.

## Your report

One block per item, in order:

```
### <item name>
Verdict: CONFIRMED | CONTRADICTED | UNSETTLED
Source: src/<path>.cs:<line>
Evidence: <the quoted line or lines, verbatim>
Why: <one or two sentences tying the quote to the verdict; for CONTRADICTED, what the item gets wrong>
```

A CONTRADICTED verdict also names what the decompile says instead, since it becomes a defect report against the reference that taught the item.
