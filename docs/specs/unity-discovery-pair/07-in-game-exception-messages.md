# 07 — In-game exception messages on every tool

**What to build:** when an invoke throws inside the game, the tool that made it reports the game's
own exception type and message. Today only `eval` and `debug_evaluate` do.

The unwrapping already exists — `EvalInterpreter.Fail` walks the exception chain for an
`InvocationException`, reads the thrown mirror's type and `Message`, and reports both — but it lives
inside the evaluator and serves the evaluator alone. Every other tool goes through `ToolGuard`, which
forwards `ex.Message`, and `InvocationException` carries no message of its own. The agent gets
`Exception of type 'Mono.Debugger.Soft.InvocationException' was thrown.` and cannot tell a bug in
its own call from a bug in the plugin.

Measured against a live target during the entity-naming work: the same underlying
`NullReferenceException` read as `in-game exception: System.NullReferenceException` through `eval`
and as the opaque string above through `ecs_get_component`. The confusion cost real time, on a
session that was diagnosing the plugin's own new code.

The move is to lift the unwrap to where every tool reaches it — `Invoker` raising a typed failure
that carries the game-side type and message, or `ToolGuard` performing the same walk — and to leave
the evaluator's richer report (statement source, position, locals) built on top of it rather than
duplicating it.

**Blocked by:** None — can start immediately.

- [ ] An invoke that throws in the game reports the game's exception type and message, on every tool
      that invokes, not only the evaluator's.
- [ ] The message is read best-effort: a thrown object whose `Message` cannot be read still reports
      its type rather than failing the report.
- [ ] `eval` and `debug_evaluate` keep their existing richer failure shape (statement source,
      position, locals) and stop carrying their own copy of the unwrap.
- [ ] The unwrap walks the inner-exception chain, so a wrapped `InvocationException` is still found.
- [ ] Covered by the integration suite, which has a fixture type that throws on demand.
