---
date: 2026-11-02
status: accepted
area: plugins/cs2-modding
---

# `cs2-dev` learns of errors from the game's logs

## Context

`cs2-dev` tells the agent about errors as they happen. An exception Unity logged reaches
`Player.log` alone, with no stamp, level or tag, so a parser has to tell it from the engine output
around it.

An in-game component was the live alternative: a mod, or a Doorstop loader, subscribing to
`Application.logMessageReceivedThreaded` or the game's `CustomLogHandler.OnException`. It would
hand over the real exception type and the block's edges, which the log leaves a parser to infer.

## Decision

`cs2-dev` reads errors from the game's logs, with no component in the game for them.

Unity's handler line marks every exception it logged but a Burst job's, whose own sentence marks it,
so a hook adds only the head and edges of shapes no capture holds. Offsets, kinds, the notification
gate and ignores stay whatever the source, as a hook still writes where the tools read, and
`Logs/*.log` and tagged entries need reading regardless. A Burst job's exception goes through
native code and aborts the game, so parsing stays as its fallback and a hook would leave two paths.

## Consequences

Error reporting works on any install, stock or patched, with no step, and adds nothing to the game
it observes.

The parser's limits stay: a block whose first line is not exception-shaped is not taken, and the
log lines it reads move with the game, which the `[symbol: …]` markers in `mcp/src/` track.

Reverse this when a real error is lost to a shape the parser cannot read, or when an in-game
component loads in every session anyway, which leaves a hook little to cost.
