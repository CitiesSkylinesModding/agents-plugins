---
date: 2026-08-13
status: accepted
area: plugins/unity-devtools/sdb
---

# Discovery reads the PlayerConnection beacon

## Context

The Mono soft-debugger agent picks its port dynamically, so it moves every launch and discovery has
to find it from outside. The first implementation listed the game process's Windows listen ports and
picked one by a 56000-56999 convention. That range is a convention rather than a reservation, so
arbitrary programs hold ports in it: discovery answered with a candidate list, every acting tool
refused until the ambiguity was resolved, and `UNITY_MCP_PORT` and `UNITY_MCP_PROCESS` existed to
resolve it by hand. It also bound the plugin to Windows, `netstat` being the source.

Three options were live: keep the scan and narrow it further, probe each candidate to tell an SDB
agent from noise, or read the endpoint off something the game already publishes. Unity players
multicast a PlayerConnection target-info beacon about once a second carrying `[Guid]`, from which
the debugger port derives as `56000 + (guid % 1000)`, and `[Debug] 1`, which says whether the
managed debugger is even enabled.

## Decision

Discovery listens for the beacon and reads the address out of it, because a published address is
knowledge rather than inference: no candidate list means nothing to disambiguate, so the port
window, the process-name matching, the ambiguity error, the probe and the entire environment
configuration surface all go, and `attach` with an explicit port remains as the one in-band escape
hatch.

## Consequences

The server has no configuration surface at all, and `status` can distinguish "no game is running"
from "the game is running without `player-connection-debug=1`" — a question the scan could not ask.
Nothing stores a resolved port, so a game restart costs nothing.

The obligations are new. Discovery now depends on IPv4 multicast reaching the server, which a
firewall or a VPN interface can prevent, so the group is joined on every interface and `attach`
carries the recovery. A listener that cannot come up must still leave a working server, since the
escape hatch ships inside it. One advertising game is assumed: a second beacon is not modelled, and
whichever the listener holds is the target. `netstat` is gone, which removes the Windows-only cause
without making the plugin cross-platform: the server-lifetime watchdogs still are.
