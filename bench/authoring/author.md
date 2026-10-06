# Author brief

The first of the three agents a task goes through, dispatched one topic at a time so no agent holds two keys.
Fill the angle-bracket fields and send the whole text as the agent's prompt.

---

You are authoring one task for the Cities: Skylines II skill-efficacy benchmark in `<repo root>`.
The benchmark gives a modder's request to an agent, with and without the `cs2-*` skills installed, and a judge rules the produced code against a checklist of traps the skills teach.
Your task tests what a modder gets wrong on **<topic>**, as a **<feature request | bug report>**, in the folder `bench/tasks/<slug>/`.

Read first, in this order:

1. `bench/README.md`, section "The task format": the folder layout, the `task.md` shape and every rule the validator enforces.
2. `bench/tests/fixtures/daily-blackout-log/`: a complete task, the shape yours takes.
3. `<reference path(s)>`: the shipped reference your task tests. Read it whole.

## The work

1. **Pick three to six traps.** Each is one thing the reference teaches that shows in the code's text: a call made or not made, a type used or avoided, an attribute present or absent, a value of one shape rather than another. A judge rules from the code alone, never compiling or running it, so a trap only visible at runtime is no item. Skip any claim under an `UNVERIFIED:` marker. Prefer traps a competent Unity or .NET developer walks into on priors, where the game diverges from the standard shape, and among those the ones the decompile leaves unmarked where an agent would look: an absence (a system never updated, a check compiled out), state stripped on load, a decision made in a file far from the type it concerns. A trap the obvious type answers on sight, a getter beside the field or a constant one grep away, saturates in both arms and measures cost alone. Done when each trap you keep has a code shape for falling in and one for avoiding, each statable in one sentence.
2. **Write the request** as a modder would type it, from what they want built, with no sign anything is tricky. Decide what the modder wants first, then check the request forces every trap: an agent answering it cannot avoid meeting each item. For a bug report, state the visible symptom and the fact that the seeded code exists, and nothing about what is wrong. The harness appends where the sources are and the closing instructions, so the request carries neither. Done when the request names no type, member, symptom or mechanism that belongs to a trap, beyond the visible symptom a bug report states, and mentions no skills, documentation, web or judge.
3. **Write the checklist.** One item per trap, each with the four fields. `Falls in` and `Avoids` name the code shapes from **Pick three to six traps**, concretely enough that a judge reading a file it has never seen can tell which it is looking at. `Reference` is the shipped file that teaches the trap, as a path under `plugins/cs2-modding/skills/`. `Source` is the decompile file and line that proves the trap is real, found under `<decompile root>`; cite the line that decides it, since a verifier re-derives every item from the decompile and corrects your cite.
4. **Write the two samples.** `falls/` answers the request and falls into every item; `avoids/` answers it and avoids every one. Each is a complete mod, `Mod.cs` included, that reads as compilable against the game's assemblies. Every item must be **met** by both samples: a sample that answers by a route an item does not concern earns a not-applicable ruling, which fails calibration. For a bug report, `falls/` is the code the modder has, so write it as a plausible first attempt, and skip the `seed/` folder.
5. **Set the roots.** `decompile` always; `install` or `ui-bundle` only when the request itself touches the installed game or the frontend bundle.
6. **Run `mise bench:validate`** from `<repo root>` and fix what it names.

## Your report

Return, for each item: its name, the trap in one sentence, the reference line that teaches it, and your `Source` cite.
Then the request, verbatim.
Then anything you had to assume, and any item you dropped for failing **Pick three to six traps** and why.

## The trap this work falls into

A request that describes its trap: naming the type or symptom a modder only knows after falling in.
Write the request from what the modder wants built; a reviewer who sees only the request will try to name the trap, and a request it can read the trap off is reworded.
