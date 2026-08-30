---
name: cs2-modding-ui
description: 'The Cities: Skylines II mod UI discipline: the C#-to-JavaScript binding layer and the game''s React frontend. Use when a mod needs a panel, button or overlay in the game''s UI, when exposing C# state to the frontend or invoking C# from it, when injecting into the game''s own component tree, when a UI mod''s bundle does not load or its styles do not apply, or when checking what the game''s frontend already provides.'
---

# Modding the Cities: Skylines II UI

The game's interface is a web page: React components rendered by Coherent Gameface, shipped on disk as one JavaScript bundle beside one stylesheet.
A UI mod is a second ES module the page loads at startup and hands the module registry, through which it overrides, extends or appends to the game's own components.
Its C# half publishes state and receives calls over the binding layer: every binding is a `group.name` path, pushed, triggered or called across the wire.

Verified against game version 1.6.0f1.
Each reference below states its own baseline.
**IMPORTANT: follow this skill's references on anything they own — or at the very least grep them before acting on a familiar shape, because the frontend diverges from standard web shapes exactly where a prior feels safest, and the guess tends to fail silently.**

## Which source answers what

The installed bundle — `Cities2_Data/Content/Game/UI/index.js`, with `index.css` beside it — is first-party and authoritative for the whole frontend: the module registry and its paths, the component tree, every formatter, the wire format a binding arrives in.
The decompile answers only for the C# half: the binding types in `Colossal.UI.Binding`, and the `Game.UI.*` systems that register them.
The official UI scaffold, which the install carries under `.ModdingToolchain`, answers for the modules a UI mod may import and for the game's TypeScript declarations.
A C# grep that finds nothing about a module, an export or a class name settles nothing — that question was never in C#.
The decompile's root lives in the record `cs2-modding-setup` keeps — read it rather than guessing a path.

## Reaching the bundle

The shipped bundle is minified to a single line, so reading around a match or citing a line needs the reformatted copy `cs2-modding-setup` records.
Read that record first; where `UI bundle copy` is absent or `(none)`, route the user to that skill rather than reformatting ad hoc, and check the recorded line count before trusting any line number cited against it.
The record tracks the script's copy alone; the stylesheet reads the same way only once reformatted beside it, a copy the record does not yet track.
The shipped file itself sits under the install root, which the toolchain's `CSII_*` environment variables locate.

## Verifying against the running game

The sibling `coherent-gameface` plugin is the domain reference for the engine underneath the page — whether a CSS or JS feature exists in the game's Cohtml version — and its tools drive the live UI over the debugging port `cs2-modding-setup`'s launch options open.
It is optional; everything here works unchanged without it.
Reading or invoking a binding on the live page takes the exact wire verbs [the binding layer](references/binding-layer/binding-layer.md) states — a guessed path or verb fails silently, the trigger returning as if it worked.

## Reading a marked claim

A `VOLATILE:` or `UNVERIFIED:` marker in these references follows the plugin-wide policy the `cs2-modding` trunk skill states: a label naming what moves or what went unconfirmed, with unmarked prose holding as architecture.

## Going deeper

- [The binding layer](references/binding-layer/binding-layer.md) — carrying a value, a call or an event between a mod's C# and the frontend: the push, trigger and call kinds, registration and teardown, how a type crosses the wire, and the request/response escape hatch.
- [The frontend as source](references/frontend-and-injection/frontend-and-injection.md) — how a mod's JavaScript gets onto the page and what it can do there: the loader and its failure modes, the module registry's operations, the append anchors, the proven extension points, the game's own React reached through the registry, styling and image serving.
- [The UI build and the dev loop](references/ui-build-and-devloop/ui-build-and-devloop.md) — what the build produces and where it lands, the shared module host, the externals contract that keeps the game's modules out of the bundle, the manifest, refreshing type declarations after a game update, and iterating against a running game.

Creating the UI project and hooking its build into the mod's `dotnet build` is `cs2-mod-project`'s; the game architecture under the C# half — systems, phases, components — is the `cs2-modding` skill.
A mod's options page is built from C# alone — [settings and input](../cs2-modding/references/technique/settings-and-input/settings-and-input.md) owns it, the page-data override for an unusual control included — and only a fully custom panel comes back through [the frontend reference](references/frontend-and-injection/frontend-and-injection.md).
