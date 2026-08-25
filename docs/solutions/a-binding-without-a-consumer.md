---
date: 2026-08-25
area: docs/research (the cs2-modding pipeline, at the C#↔frontend seam)
symptoms:
  - 'a UI claim verified against the decompile is false in the running game'
  - 'a C# binding exists and computes a value, but no control ever shows it'
tags: [research, decompile, frontend, bindings, verification]
---

# A binding without a consumer

## Problem

`ToolUISystem` computes `elevationUpDisabled`/`elevationDownDisabled` every frame and exports them
to mods, so a derivation read them as the elevation arrows' disabled state. Three finders and an
authoring pass accepted the claim from the C# read alone. The arrows' disabled state actually comes
from a different binding pair (`elevation` against `elevationRange`), and the two cited bindings are
read by no frontend component at all (`src-ui/source.js:46100-46101`, declared and re-exported,
consumed nowhere).

## Root cause

The C#↔frontend seam is a declaration boundary, and each side can declare what the other never
consumes. A C# binding proves only that a value is published; whether a control shows it is the
bundle's decision. The reverse holds too: the editor screen registers a `"Change Elevation"` action
handler (`source.js:132826`), which proves nothing about the input being bound or enabled — the
install's input asset decides that, and it is not in the decompile.

## Fix

Settle a player-facing claim at the consumer: the bundle component that reads the binding, or the
bound action in the input asset.
[A derivation stopped at the line that agreed with it](decompile-read-stopped-at-the-confirming-line.md)
is the same stop inside one source; this is the cross-seam form, where the rest of the method lives
in the other artifact.

## Prevention

For any claim shaped "the player sees / can do X", the citation must include the line that
consumes what the other side declares. A grep for the binding's name over the bundle is one
command; two declarations that never meet are the shape to distrust.
