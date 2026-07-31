---
date: 2026-07-31
status: accepted
area: plugins/unity-devtools
---

# Read the target's own constants once, decode client-side

## Context

Every fact a debugger tool reads out of a running game costs a round-trip to a frozen process, and
some facts are bits packed into a value the wire already carried. Unity Entities encodes a
component type's whole storage kind — buffer, shared, chunk, managed, zero-sized, enableable — in
flag bits of the `TypeIndex` that arrives free with every `ComponentType`. Asking the game to
un-pack them means one property invoke per fact per component, and a listing that spends a read per
component multiplies that by the archetype's size.

The obvious alternative, hardcoding the bit positions, is what the standing analysis rejected: the
positions are engine internals and they move between versions, so a plugin that bakes them in reads
a wrong kind against a target that moved one, and a wrong kind routes a caller to an accessor that
reads memory the entity does not own.

## Decision

Neither ask per fact nor hardcode the layout: **read the layout's own constants off the target,
once per attach, then decode client-side.**

The target states its own layout, and the plugin follows it. A version that moves a bit moves the
constant with it, so the decode moves too, with nothing to update.

The tradeoff is only acceptable with all three of its mitigations, which are part of the decision
rather than notes on it:

- **The constants come from the target.** Nothing about the layout is written down on the client
  side, so following a version that moved a bit needs no release.
- **An absent constant abandons the decode wholesale.** A partial mask set decodes some facts and
  silently mis-decodes the rest, so a missing constant falls back to asking the game per property
  for the whole attach. An unfamiliar version is slower, never wrong, and the caller is told
  nothing, because the answer is identical either way.
- **Every such decode is cross-checked against the target's own accessor once, at implementation
  time.** The check is a gate on the change landing, not shipped calibration: the decode and the
  accessor it replaces must agree on real values covering every branch, verified against a running
  target. It leaves no code behind.

## Consequences

Any state the target packs into a value already on the wire is a candidate for the same treatment,
under the same three mitigations. That it is a candidate is not a licence: applying this to a new
piece of state is its own decision each time, because the cross-check has to be performed for that
state and the fallback has to be shown to exist.

Where the constants are not exposed at all, the technique does not apply, and the per-fact invoke
stays the correct answer rather than a defeat.

The mechanics of any one application — which constants, which masks, what the ladder is — belong in
`docs/solutions/`, not here. This record owns the policy and its price.
