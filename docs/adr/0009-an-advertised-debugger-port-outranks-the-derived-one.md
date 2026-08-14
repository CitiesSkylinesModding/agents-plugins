---
date: 2026-08-14
status: accepted
area: plugins/unity-devtools/sdb
---

# An advertised debugger port outranks the derived one

## Context

[0008](0008-discovery-reads-the-playerconnection-beacon.md) took the debugger port from `[Guid]`
through `56000 + (guid % 1000)`, evidenced by two recorded runs of the reference game. Unity's
published beacon format ends `[Id]` with an optional `:port`, and its manual prints a player whose
GUID derives 56029 against an advertised 56000, so the two disagree on a target Unity itself
documents.

Three options were live: keep deriving always, prefer the advertised port, or prefer it only inside
the 56000-56999 window the derivation can produce.

## Decision

The advertised port wins outright, the derivation staying as the fallback for a player that
publishes none, because Unity's own IDE integration reads the field the same way and applies no
range check of its own.

The window guard was rejected: 56000-56999 is exactly the derivation's range, so the guard fires
only where the advertised port is out of it, which is the one case the derivation is certainly
wrong about. It would replace a port the player named with one nothing bound.

## Consequences

A malformed suffix falls back rather than rejecting the beacon, so such a player is unhelpful
rather than unusable. Nothing bounds the advertised value, which is safe only because Unity's
reference parser matches `\[Id\] (?<id>[^:]+)(:(?<debuggerPort>\d+))?` — the id group forbids a
colon, so the only colon a well-formed id carries is this separator. A future reader who re-proposes
the range guard should read that regex first.
