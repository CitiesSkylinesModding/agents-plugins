---
name: cs2-trap-triage
description: 'Judges the trap reports on this repo''s tracker and ports the ones that survive into the `cs2-modding` skills. Use when trap issues wait on the tracker, or when the user asks to triage a reported trap.'
---

# Triaging trap reports

A trap report is a **claim** from an agent nobody watched: a misread, a fault in its own mod and the output of a sweep all arrive looking like a trap.
Prove each claim again here before a word of it reaches a skill, and expect to admit a minority.

The trap contract in [`plugins/cs2-modding/AGENTS.md`](../../../plugins/cs2-modding/AGENTS.md) says what a trap is and how its line is written.
Load `cs2-modding`, and `cs2-modding-ui` for a frontend claim: "Trap reports" holds the two tests of a candidate, "Which source answers what" the source that settles each kind of fact.

## 1. Collect

Take the issues the user named, else every open issue of `CitiesSkylinesModding/agents-plugins` reporting a trap.
An issue listing several traps is one candidate per trap.
Read the comments too: each story there, the kind of task and the turns it cost, is evidence for its candidate.

## 2. Judge each candidate

Apply these tests in order; the first one failed is a `reject`, except where it says `reroute`.

1. **Ours**: the fact is the game's, and no fault of the reporter's code. A fault in this repo's tools, or a fact of the engine under any Gameface or Unity game, is `reroute`.
2. **New**: nothing under the plugin's `skills/` states it, searched by its symbol and its nouns. A claim against a stated line is a correction, admitted once test 3 proves it.
3. **True**: you reproduce the behaviour yourself in the code the game runs, and in the running game where the code leaves it open. The issue's cites and its proposed line prove nothing, and a claim nothing on this machine can settle fails.
4. **A trap**: it passes _Silent_ and _Led there_ as you read the code, whatever the report says of them.
5. **Bites**: a story with turns lost, or a 👍 from a second agent, shows it met real work. With neither, it passes only where the scaffold, the wiki or the game's own names put an ordinary mod on the wrong move.
6. **Earns its lines**: where its body sits at its ceiling, it outranks that body's weakest trap on reach, the mods that walk into it, and on cost, the distance from the symptom to its cause.

A candidate passing all six is `admit`.

## 3. Port

- `admit`: write the trap from the code you read, never from the issue's wording, under the topic heading of the body whose domain it is, and remove the trap it displaces. A correction rewrites or retires the stated line instead.
- `reroute`: a tool fault becomes a `docs/ROADMAP.md` entry by its legend; an engine fact goes to the owning plugin's skill by that plugin's `AGENTS.md`.

Then verify each line you wrote, read as an agent meeting it cold: every clause holds in the code, the right move works in the running game where the game can show it, and the marker names the type whose code carries the behaviour.
A line that fails is fixed and verified again, or its port reverted and its candidate rejected.

Done when every line written is verified and `mise check:agents` is green.

## 4. Close

The closing message holds one table, a row per candidate: issue, claim in a clause, verdict, and the test that decided it or the line it became, with how that line was verified and the trap it displaced.
Under it, draft the tracker's side and apply it only on the user's yes:

- One comment per issue, giving each candidate's verdict, the test that decided it and what you found.
- Every issue closes: by the commit landing the port, which carries `Closes #<n>`, where a candidate of it was admitted, and as not planned otherwise.

End on the next step, for the user to type: `/review-gate --fix` over the working diff, then `/commit`.
