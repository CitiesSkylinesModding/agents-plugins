---
date: 2026-08-18
status: accepted
area: plugins/coherent-gameface/mcp
---

# A rejected selector is diagnosed after the call, not screened before it

## Context

Cohtml's JS query APIs answer a short set of pseudo-classes and reject the rest with a bare
`SyntaxError: Invalid CSS selector (<sel>) in QuerySelector!`, naming neither the offending
construct nor a way out. Nine selector-taking tools surfaced that text behind their own prefix, so
a caller tended to retry the same selector unchanged. What the engine rejects was recorded only in
the skills, which a standalone MCP client never loads.

Two shapes were live. **Screen before the call:** inspect the selector server-side and refuse or
warn before the CDP round trip. **Diagnose after it:** let the call go, and enrich the error the
engine returns.

Screening carries a constraint it cannot discharge. The supported set is read off a running
engine, so it describes one build; a screen that refuses a selector a newer build would answer
breaks a caller for no reason. The escape routes are a version gate or a warning that lets the
call through anyway. `game_status` reports the engine version, but best-effort — it can be absent
— so the gate is unreliable exactly where it matters, and a warning that does not block is a
screen that has given up its only advantage.

## Decision

The diagnosis runs only on an error the engine already returned. A tool catches the exception,
and where it carries the invalid-selector marker the message gains the name of the suspect
construct and a one-sentence rewrite; any other error passes through untouched.

This dissolves the constraint rather than managing it. A diagnosis that never runs unless the
engine has already refused the call cannot refuse one the engine would have accepted, whatever
version is in front of it, so no version gate is needed and none is used.

Detection is whitelist-complement: any pseudo token outside the verified set is the suspect, which
names a culprit even for constructs nobody probed. Because that set records what was verified to
work rather than what the parser accepts, the message names a **suspect** rather than a verdict
and always leaves the syntax-slip exit open.

## Consequences

A caller pays one round trip to learn its selector is unsupported. That cost is what buys the
version safety, and it is the cost of the call it was going to make anyway.

The engine facts live in the server as well as in the two skills, since the diagnosis has to
answer a client that loads neither. Re-probing a new engine version updates all three, which
`plugins/coherent-gameface/AGENTS.md` records; a test binds the module's two copies to each other,
and nothing binds the skills.

Any tool that can surface an engine exception to its caller inherits the diagnosis by routing
through `explainException` rather than `formatException`. That reach is wider than the selector
tools: `game_eval` and `game_debug_evaluate` run caller-supplied JS and get it too, reading the
selector out of the engine's own echo since they hold none.

The roadmap entry proposing the screen is retired. Reviving pre-flight means reopening the version
question this record closed.
