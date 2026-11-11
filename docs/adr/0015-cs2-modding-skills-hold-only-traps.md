---
date: 2026-11-11
status: accepted
area: plugins/cs2-modding
---

# The `cs2-modding` skills hold only the traps an agent cannot guess

## Context

ADRs 0003 to 0007 built a reference contract: two families of reference files retelling the game's decompiled code, each claim re-derived from research files with line cites.
Every game update, minor ones included, cost hours of re-deriving cites and counts.
Each new reader found fresh mistakes, many older than the update at hand, and scripting the cite checks cut less of that cost than hoped.
The benchmark that justified them measured one-shot answers on tasks built to trick agents, which is not how a mod gets written, and even there an agent holding only the game's own sources passed 87% of the checklist, against 96% with the skills.

## Decision

The skills hold a working loop and a short list of traps, and the game itself is read fresh from the decompile each time.
A trap is a game fact an agent with the decompile and the live game still gets wrong; it is marked with the type whose code carries it, and `mise skills:trap-check` names the traps a game update touched.
The contract's rules live in `plugins/cs2-modding/AGENTS.md`, "The trap contract".
This supersedes ADRs 0003 to 0007 as a block.

## Consequences

A game update costs one script run and one read per trap it names.
The references, the research files, the benchmark, the cite audit and their contract documents are deleted; commit `e63398d` is the last that holds them.
New traps arrive only through the trap reports an agent writes when it falls into one, so the list grows from real use rather than from a sweep.
