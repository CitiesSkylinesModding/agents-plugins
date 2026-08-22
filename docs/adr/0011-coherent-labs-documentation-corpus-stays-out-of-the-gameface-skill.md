---
date: 2026-08-22
status: accepted
area: plugins/coherent-gameface/skills/gameface
---

# Coherent Labs' documentation corpus stays out of the gameface skill

## Context

Coherent Labs published their own Gameface MCP server in August 2026 under MIT. It ships a
documentation corpus of roughly 6,400 lines across twenty topic files, distilled from the Gameface
docs by the people who build the engine, and exposed as MCP resources with a search tool over it.

Nothing stops us importing it. The licence permits it, the prose is competent, and it covers ground
`skills/gameface/` does not, including localization and custom effects. Against our five curated
reference files it is an order of magnitude more material for the cost of a copy, which is why the
question will keep coming back.

Two things make it the wrong material for this plugin.

It describes 3.x and gates nothing. `14-animations.md` teaches `@starting-style` and
`03-scalability.md` states that "Gameface supports CSS nesting", both true of a current engine and
false of the 1.x builds shipped games are frozen on. A reader cannot tell which claims apply to the
engine in front of them, because the corpus never asks. That is the failure
`skills/gameface/references/version-gating.md` exists to prevent, and importing an ungated body of
claims into the skill that enforces gating would hollow out the rule at its own source.

Its provenance is also thinner than it looks. Eight of the twenty files ship marked
`STATUS: partial`, and the fourteen carrying a `LAST EXTRACTED` marker leave it blank, so most of
the corpus records no date for what it was read against.

## Decision

The corpus stays out of `skills/gameface/`. We keep the smaller curated set plus the live docs
extractor (`scripts/fetch-doc.mjs`), trading density for freshness and for the guarantee that every
claim in the skill has been gated against a version.

Individual facts remain fair game, one at a time, each gated on the way in and each cited to the
file it came from. What is refused is the body as a whole, and any import that would bring ungated
claims along with the gated ones.

The same reasoning governs their `search_gameface_docs` tool: retrieval over a frozen snapshot ages
in a way a live extractor does not, so we do not build one either.

## Consequences

`skills/gameface/` stays small, and gaps stay gaps until someone gates the material to close them.
Localization and custom effects are the two that a mod shipping translated UI or a styled panel
would plausibly want, and both remain unwritten; a roadmap entry tracks scoping them.

Anyone weighing the import again should start here rather than re-deriving it. The user-facing
version of the same trade is stated in the plugin's `WHY.md`, under "Where theirs is ahead", where
it is framed as a deliberate choice rather than a missing feature.
