---
paths:
  - "**/*"
version: 1.2.0
---

# General Code Style

These rules are general guidelines applying to every language.

## Formatting

- Let the code breathe: use ample line breaks (but never consecutive) to improve readability, separating logical blocks of code, variable assignment from usage, etc.
- Never inline `{}` blocks, always break.
- Passing more than 4 params in a function is discouraged (use an object instead), but keep symmetry above this rule.
- Never use em dashes (—) in comments, docblocks, and docs, when you see one, remove it. In place, use commas, semicolons, colons, or find a better/simpler formulation.
- For source files in general, enforce a strict 100-character line length limit, comments included (include docblock formatting in the count).
  Exceptions:
    - One-line lint warning suppression comments.
    - Long strings that don't make sense to split into multiple lines.
    - AGENTS.md and other Markdown documents intended for agents (ex. skills, rules, etc.)
    - Any file where this is not applicable/desirable.

## Comments and Docblocks

- Comment anything that isn't self-explanatory within a few adjacent lines, but make each comment earn its place: prefer a few high-value comments over blanket coverage.
- Comments explain intent and the non-obvious why, not what the code plainly does.
- Pitch comments at the durable altitude: capture the rule or invariant that stays true, not transient specifics (measured values, one-off observations, counts, dates) that rot into misleading noise. Don't over-describe, and don't over-cross-reference other files.
- Comments and docblocks should describe the code as it is now, not narrate code that was deleted or changed (no "this used to…", "the old X is gone", etc.). Only reference removed code when there is a solid, lasting reason the present code cannot stand without it: e.g., a wire- or API-compat. constraint, or a non-obvious gotcha the removal left behind, and then state the constraint, not the chronology.
- Write in the active voice.
- In docblocks and comments, always end sentences with a period.
- Use Oxford commas.
- When writing docblocks, wrap sentences to facilitate legibility between logical components. Ex:
    - Bad:
      The cow is white. The (lf)
      dog is brown.
    - Good:
      The cow is white. (lf)
      The dog is brown.
- When not using Markdown and actual links, reference links to files in the project using wikilink; bad: `document.md`, good: [[document.md]].
