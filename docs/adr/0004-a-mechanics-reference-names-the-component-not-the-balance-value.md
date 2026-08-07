---
date: 2026-08-06
status: accepted
area: plugins/cs2-modding
---

# A mechanics reference names the component and the field, never the balance value

## Context

Every reference the `cs2-modding` plugin had shipped stated its numbers from artifacts a reader can open with the game closed: decompiled C#, the install's own files, the toolchain's targets. The `citizens-and-households` pass was the first whose load-bearing numbers are none of those.

The balance of that whole area lives in components on settings prefabs rather than in code — wages and benefits, every happiness magnitude, birth and divorce rates, school-entry probabilities, trip priorities, school fees. The decompiled C# declares each field and never its value. The only cheap route to a value is a component read against a running game, and a catalogued mod rewrites several of those components at load, so what is in a reader's memory need not be what the game shipped.

Six other mechanics topics sit on the same kind of data, so whichever form was chosen became the house style for every balance number the plugin ever ships. Three options were live: state the values plainly as the plugin states a C# constant; state none of them and name only the component and field; or state them with a read recipe attached.

## Decision

**A shipped reference states no prefab value.** It names the component and the field, and that is the whole of what it says about the magnitude.

The third option — a value with a read recipe beside it — fails on the same ground and adds one of its own: a reader acts on the number that is printed, and the recipe is followed only by whoever was going to do the read anyway. What survives of that option is the access shape in the map below, which belongs to the component rather than to any one value.

The ruling was made as "prefab-singleton" against the topic that produced it, and widened the same day to prefab values generally. Balance also lives on per-prefab components — a school's capacity, a hospital's treatment bonus, a crime's probability — which rot at the same rate and which a mod overwrites as easily, and the topic that produced the ruling had already withheld those figures too. The narrow word would have licensed the next services or zoning reference to bake per-building balance while forbidding the same figure one level up.

The ground is rot rate rather than re-checkability. Balance is the fastest-moving thing in this game, so a baked balance figure is the claim in this plugin most likely to be wrong first — and wrong silently, since nothing distinguishes a figure read at one version from a current one. Mutability by a neighbouring mod compounds that rather than founding it.

**It is a substitution, not a subtraction**, and three things are untouched:

- **C# constants ship, as numbers.** A value compiled into the decompiled source is first-party, offline-checkable and citable to a line.
- **Formulas ship whole.** The expression a system evaluates, its baseline, its step functions and the shape of its random walk are invariant structure rather than balance.
- **The map ships, and it is the constructive half.** Which component owns which family of numbers is what makes the read possible at all, so a reference that names a field without routing it to a component has taken the number away and given nothing back.

Two consequences follow directly. A ratio derived over such values is the same magnitude and goes with them, an adverb carrying it included — _far more_ is a ratio in prose. And a non-numeric prefab value is still a prefab value: a `bool` deciding whether a fee is player-adjustable ships as the field to check, with the reason, and never as a fact about that fee.

## Consequences

The plugin commits every remaining mechanics pass to one of three routes for any number it wants to verify: a running game, a Unity serialized-file parser, or the packaged content directly where the prefab is a content pack's. That third route is the cheap one and belongs first — a `.cok` is a plain stored zip whose prefab entries are self-describing, field names inline, so a small reader returns a value by name without a schema. Only a base-game prefab needs the parser, and only because its own file carries type names and no field names, which makes reading it a derivation off the decompiled class's field order. This is the same bill the sibling ruling on wiki stat tables accepted, and the two are one decision: that one says a wiki table is never a shipped citation, this one says what a reference does instead.

The reasoning was made in `docs/research/conflicts.md`, which nothing under `plugins/` may reference — the shipped-prose contract in `plugins/cs2-modding/AGENTS.md` carries the rule and points here.

Naming the component is no longer enough on its own. A parameter singleton is one call; a buffer on a singleton entity, an enableable-gated per-prefab component and a lookup through an instance's `PrefabRef` are three others, and a reader cannot write any of them from a field name. So the map a reference ships has to carry the access shape beside the component, which is a line of prose per row that the narrow ruling never asked for.
