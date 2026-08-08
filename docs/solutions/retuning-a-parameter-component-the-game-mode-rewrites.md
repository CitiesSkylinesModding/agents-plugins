---
date: 2026-08-07
area: plugins/cs2-modding/skills (mod-lifecycle-and-ordering, and every mechanics topic that retunes balance)
symptoms:
  - "a balance retune is correct in every test city and absent or doubled in a player's"
  - 'a mod toggled off restores a number the player never had'
  - 'a claim that a parameter component is written once survives review and is wrong'
tags: [cs2, prefabs, game-modes, balance, unsolved, over-reach, review]
---

# Retuning a parameter component the game mode rewrites

**What ships on this is one trap.** The full passage this records was written during ticket 22's
review gate, failed fourteen rounds, and was cut from that commit; the citizens entry file now
carries a trap on it. Pick the general treatment up as its own ticket, from the discovery stage,
with this file as the starting point.

## Problem

A `cs2-modding` reference needed one paragraph on where a mod should place a write that overwrites a
prefab parameter component, and how to undo one. Fourteen review rounds produced fourteen versions,
each fixing the last and each wrong in a new way, at which point the paragraph was cut rather than
shipped. Every version traced a real mechanism and then stated a conclusion about it that had not
been checked against the case in hand.

## What didn't work

**Every general rule, in both directions.** "Written once and nothing writes it back"; "a floor with
no ceiling"; "re-apply on every load"; "restore from the authoring object"; "restore from a guarded
snapshot"; "read `Game.Prefabs.Modes` and the answer is there". Each was true of the case that
produced it and false of a neighbouring one.

**Cutting the recipe.** Twice. The second cut left only hazards and an instruction to read the mode
classes, and a review found the prescribed read establishes less than the sentence claimed.

**A verify stage.** The first refutation came from a finder, not a verifier — the verifier had been
handed the finder's search (`SetComponentData` near the component's type name) and inherited its
blind spot. See [a search taken for a census](empty-grep-read-as-proof-of-absence.md).

## Root cause

Three independent unknowns, two of them outside the decompile entirely.

- **Reach.** `GameModeSystem` runs `ModeSetting.ApplyMode` over the mode prefab list on the loaded
  save's `ModeSetting`. That list is authored asset data — live at 1.6.0f1, `NormalMode` carries zero
  entries and `EasyMode` carries 21. Whether a component is rewritten at all therefore depends on the
  player's chosen mode and is invisible to any code read.
- **Scope.** Of the mode classes, roughly half derive from `LocalModePrefab` and declare no entity
  query: their targets are an authored `PrefabBase` array, also asset data. So even for a class that
  names your component, whether it reaches _your_ prefabs is unreadable.
- **Shape.** Some classes assign, some multiply, and a component can be written by two or three in
  sequence — `CoverageData` is assigned by one class and then multiplied by another over the same
  entities. A write before the pass is scaled by it; a write after discards what it contributed.
  Six classes also snapshot the component before applying and hand that back on restore. Two of
  those fall back to the authoring object where no snapshot exists, one restores its main component
  from authoring and uses the snapshot only for a buffer row, and five have a branch that restores
  nothing at all — so "snapshot or authoring" is not even a per-class property. **No reason for the
  snapshotting is established**: the components have authoring counterparts, and every snapshot is
  taken before any mode class writes, so it cannot be protecting an earlier class's assignment. Two
  rationales were proposed and both were refuted; the decompile carries no comment. Record it as
  behaviour.

## Fix

**Split the write from the undo**, which is the line that survives — a settings-prefab versus
per-prefab split was tried first and leaks: `PoliceConfigurationMode` multiplies six of seven fields
on a settings-prefab singleton, and `ModeSettingData` is written by `ModeSetting` itself rather than
by any `*Mode` class, so enumerating the mode classes misses it.

**The write is solved.** `OnGameLoaded` fires after the whole `Deserialize` phase the pass runs in,
so a write there lands last whatever the pass did. That holds for both kinds of component.

**The undo is `prefabs-and-assets`' rule unchanged: write the authoring value back.** No parameter
component is ever snapshotted — only six per-prefab classes override `StoreDefaultData` — so reading
the authoring object is exactly what the game's own restore does for these. The single gap is a
contribution the player's mode made to the same component, which stays missing until the next load;
replaying the game's restore-and-apply would recover it but runs the whole mode prefab list, so it
re-applies every per-prefab class as a side effect and returns a job handle a settings callback
cannot place. Per-prefab components add sequencing, snapshot restores, restore-nothing branches and
unreadable reach on top, and that part is still open.

Two wrong verdicts on the way here, both worth the space: "unsolved", shipped after tracing the
teardown-then-re-apply pairing and never asking whether a mod could replay it; and then "replay it",
shipped after finding the methods public and never checking that the snapshots it rested on exist for
this kind of component. They do not.

The generalisation that cost the rounds ran in both directions: first from the parameter case to all
components, then — once the per-prefab classes were found — from those back onto the parameter case,
shipping "unsolved" for a write the code settles cleanly.

Also settled, and not worth re-deriving: prefab initialisation as the floor, over two hooks whose
split is a contract rather than a style; prefab entities living for the process, so one write stands
where nothing else writes the component; and a `GameSimulation` interval system having no per-load
edge and not running while the simulation is paused — which makes it unnecessary once `OnGameLoaded`
settles the write, rather than unusable.

## Prevention

**The shape to recognise: a rule whose truth depends on a value the pipeline cannot read.** For the
per-prefab case, reach and scope are both authored asset data. A pass that can only read code will
keep producing rules true of whatever case it examined, and each review round finds the next case
rather than the pattern — which is what the rounds cost.

**But test that before declaring anything unsolved.** "Unsolved" was shipped twice here and was wrong
both times: once for the write, which `OnGameLoaded` settles, and once for the undo, which the game's
own restore-and-re-apply is public enough for a mod to replay. Both were called unsolved because a
real mechanism had been traced and the next question — _is there a route out of it_ — had not been
asked. Giving up is a claim like any other and takes the same evidence.

The live game can settle reach for one save (`PrefabSystem.GetPrefab<ModeSetting>` on the
`GameModeSettingData` entities) and is the only thing that can — but a reading from one city is
evidence, not a rule, so it belongs in a research file rather than in shipped prose.

**A second shape showed up late and is cheaper to catch: the verified fact with an invented
_because_ attached.** Two rounds running, the fact and every count beside it re-derived clean and the
causal clause was wrong — which is exactly why it survived, since a reviewer checking the sentence
finds most of it true. Where a rationale was not itself verified, cut it: the instruction almost never
needs it, and it is the half a reader will generalise from.
