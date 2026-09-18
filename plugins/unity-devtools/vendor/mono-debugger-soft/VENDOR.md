# Vendored: Mono.Debugger.Soft

The Mono Soft Debugger client, copied from Unity's mono fork — verbatim but for the patches to `Connection.cs` recorded below.

| | |
|---|---|
| Upstream | <https://github.com/Unity-Technologies/mono> |
| Branch | `unity-6000.6-mbe` |
| Commit | `dd5ec1cae42eaf53889329fc09ab6870392d9204` |
| Path | `mcs/class/Mono.Debugger.Soft/Mono.Debugger.Soft/` |
| License | MIT, see `LICENSE` |

Unity's fork rather than upstream mono, because the debuggee is Unity's runtime: the two have diverged on the wire protocol, and only this fork's client speaks the version Unity ships.

## Copied, not submoduled

These files are committed directly. The tree they come from is a full mono checkout — over a hundred thousand files, some with paths past Windows' 260-character limit — so a submodule made the repository unclonable on Windows and cost every other platform a multi-hundred-megabyte fetch for 75 files nobody outside a build ever reads. Installed users get the MCP server from NuGet and never compile this at all.

## Local patches

`Connection.cs` is the one file that diverges from upstream. `scripts/update-vendored-sdb.ts` applies every patch as it fetches, so what the build compiles is what this directory holds and no build step has to reproduce it:

- The client dispatches an invoke reply through `cb.BeginInvoke (r, null, null)`, which throws on modern .NET. It becomes a `Task.Run`.
- The receiver thread reports a failed receive with `Console.WriteLine (ex)`. In the MCP server stdout carries JSON-RPC and nothing else, so a debuggee dying abruptly would write a stack trace into the protocol stream and break the session. The diagnostic is worth keeping, so it moves to `Console.Error`.
- `SendReceive` waits for a command's reply untimed, so a debuggee that takes a command and never answers it blocks its caller forever — and that caller holds the session's gate, so every later tool call queues behind it. Two anchors give the wait a 30-second deadline spanning the whole loop (one `Monitor.Wait` is not the wait: any other reply pulses the monitor and restarts it) and raise an `IOException`, which `UnitySession` already reads as a lost connection. The deadline is judged on a `Stopwatch` before each wait rather than on the wait's return value: a wall clock lets an NTP step expire a healthy command, and a reply landing as the wait times out is still inserted while `Monitor.Wait` reports false either way, so throwing on false would discard an answer the loop is about to find.

Each is anchored on the exact upstream text. An update that no longer finds one fails and writes nothing, so the divergence can never silently lapse.

## Updating

`mise vendor:unity:update` refetches the tip of the branch above and rewrites this directory; pass a ref (branch, tag or commit) to move the pin elsewhere. Either way the table above is rewritten with what was fetched, so the diff records the move. Restoring the committed state needs no task: `git checkout` this directory.

Expect the update to be a deliberate act with a review, not a routine bump: this is a debugger client whose failures show up as a hung session rather than a compile error, and the integration suite driving a real Mono debuggee is what actually clears a change to it.

The client's own limits, and which of them we work around, are in [`docs/solutions/sdb-vendored-client-limits.md`](../../../../docs/solutions/sdb-vendored-client-limits.md).

Never hand-edit a file in this directory; the next update reverts it silently. Fixes belong in `sdb/` alongside the other shims, or in the patch list above where the change must land inside a vendored file.
