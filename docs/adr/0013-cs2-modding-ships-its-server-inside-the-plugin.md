---
date: 2026-10-30
status: accepted
area: plugins/cs2-modding
---

# `cs2-modding` ships its server inside the plugin

## Context

The plugin was stated to be knowledge only, on its manifests, its READMEs and the repo's
`AGENTS.md`: skills and references, no MCP server, no executable content.

That left the game's cycle out of reach. No reference said how to restart the game after a build
and know when it was ready, so each mod session worked it out again. The mod template had a working
version as a Bun CLI, a log reader and an in-game test runner, but only mods made from the template
got it.

Three shapes were live: teach the cycle in a reference and leave the tooling with the template, ship
a separate plugin for the server, or ship the server from `cs2-modding` itself.

## Decision

`cs2-modding` ships the `cs2-dev` MCP server, a private workspace package bundled into a committed
artifact, with no release unit of its own and the plugin's version.

A reference alone leaves every mod without the template to write the tooling itself. A separate
plugin lets a tool and the reference teaching it drift apart, where one plugin moves them together
and makes every tool available from one install.

## Consequences

The plugin's identity changes: an install now runs code, with Node 22.4+ as its runtime, and that
code can stop or kill the user's running game without asking. The plugin's README discloses this
before install.

The plugin takes on what a server plugin owes: both harness configs, the bundle kept current by the
pre-commit and CI, and a release of either the tool or a reference being a release of both.

The mod template drops its copy and points at the plugin, so a template user has no game-driving
scripts to maintain and receives updates with the plugin.
