---
name: cs2-modding-ui
description: 'The Cities: Skylines II mod UI discipline: the C#-to-JavaScript binding layer and the game''s React frontend. Use when a mod needs a panel, button or overlay in the game''s UI, when exposing C# state to the frontend or invoking C# from it, when injecting into the game''s own component tree, when a UI mod''s bundle does not load or its styles do not apply, or when checking what the game''s frontend already provides.'
---

# Modding the Cities: Skylines II UI

The game's interface is a web page: React components rendered by Coherent Gameface, shipped on disk as one JavaScript bundle beside one stylesheet.
A UI mod is a second ES module the page loads and hands the module registry, through which it overrides, extends or appends to the game's own components.
Its C# half publishes state and receives calls over the binding layer, where every binding is a `group.name` path.

Work by the `cs2-modding` skill's working loop: find how the game's own UI does the thing, in the bundle, before writing.
A trap you meet here that this skill misses goes through that skill's trap reports.

## Which source answers what

- The installed bundle, `Cities2_Data/Content/Game/UI/index.js` with `index.css` beside it, answers for the whole frontend: the module registry and its paths, the component tree, the formatters, the wire format a binding arrives in.
  It ships minified to one line, so read the reformatted copy the setup record names; where `UI bundle copy` is absent or `(none)`, route the user to `cs2-modding-setup`.
- The decompile answers only for the C# half: the binding types in `Colossal.UI.Binding`, and the `Game.UI.*` systems that register the game's own bindings.
  A C# grep that finds nothing about a module, an export or a class name settles nothing.
- The official UI scaffold under the install's `.ModdingToolchain` answers for the modules a UI mod may import and for the game's TypeScript declarations.

The sibling `coherent-gameface` plugin tells whether a CSS or JS feature exists in the game's Cohtml version, and drives the live UI over the debugging port `cs2-modding-setup`'s launch options open.
It is optional; everything here works without it.

## Traps

- A throw in a mod's registrar skips every registrar after it, other mods' included, and after a UI reload leaves the whole UI unrendered: wrap the registrar's body in `try`/`catch`. [symbol: none]
- A C# answer to a frontend request comes back on the promise of `call`, from a `CallBinding` with a result type, rather than through a trigger and a separate value binding. [symbol: Colossal.UI.Binding.CallBinding]
- A literal `font-size` ignores the player's text-scale setting: use the game's `--fontSize*` tokens, or `calc(<n>rem * var(--fontScale))`. [symbol: none]

## Building and iterating

[The UI build and the dev loop](references/ui-build-and-devloop.md) covers what the build produces and where it lands, the externals that keep the game's modules out of the bundle, refreshing type declarations after a game update, and iterating against a running game.
Creating the UI project and hooking its build into the mod's `dotnet build` is `cs2-mod-project`'s.
