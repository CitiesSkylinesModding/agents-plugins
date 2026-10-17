---
date: 2026-10-17
status: accepted
area: plugins/unity-devtools/sdb/Eval
---

# The `eval` grammar grows by an admission rule

## Context

The plugin's `AGENTS.md` declared the `eval` grammar frozen and sent new evaluator effort to
enforcing its contract, so that the grammar would stop growing one convenience at a time while
its semantics were still being made exact.

The freeze outlived that purpose. The roadmap collected forms agents write by reflex and the
grammar rejects (`string.Join`, `ref hd`, `$"{x:F1}"`, `default(T)`), each with recorded hits and
each costing a rewrite or an abandoned call. Every one translates to a construct the evaluator
already runs. With no rule to admit them, each was held by the freeze, and arguing one through
would have reopened the question per review.

## Decision

The grammar is no longer frozen. A form is admitted when all three hold:

- agents write it by reflex, with a roadmap entry or a recorded hit as evidence;
- it translates to a construct the evaluator already runs;
- it evaluates exactly as C# would, under a contract test in the integration suite.

Constructs needing debuggee-side execution (lambdas, LINQ, loops, control flow) stay rejected at
parse time and belong to the injected-helper tier, since the client-side walker cannot run them
without becoming an interpreter of C# control flow.

## Consequences

The plugin's `AGENTS.md` states the grammar as it stands and the admission rule; this record
keeps the why. A request to grow the grammar is judged against the rule, not reargued.

The rule keeps the semantic contract intact: a form enters only with a test proving it matches
C#, so growth adds no silent divergence. Where an admitted form must diverge, as a format clause
does on culture, the divergence joins the contract's documented list.

A form one case away in the parser is not admitted by proximity: interpolation alignment, `in`
arguments and the bare `default` literal had no recorded evidence when the rule was set, and stay
rejected until they do.
