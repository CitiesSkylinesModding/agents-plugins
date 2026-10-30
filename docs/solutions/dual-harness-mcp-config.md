---
date: 2026-07-29
area: plugins/*/(.claude-plugin|.codex-plugin)
symptoms:
  - '`${CLAUDE_PLUGIN_ROOT}` reaches the MCP child verbatim on Codex CLI'
  - 'Error loading config.toml: invalid transport'
  - 'plugin server starts in the wrong working directory on Claude Code'
  - 'Failed to reconnect to plugin:<plugin>:<server>: -32000'
tags: [mcp, plugin-manifest, codex-cli, claude-code]
updated: 2026-10-30
---

# One MCP config cannot serve both harnesses

## Problem

Every plugin here ships to Claude Code and Codex CLI. The obvious move is one `mcp.json` referenced
by both manifests. It cannot work: each harness lacks the mechanism the other relies on to locate
the plugin's own artifacts.

## What didn't work

- **`${CLAUDE_PLUGIN_ROOT}` everywhere.** Codex does not interpolate `${VAR}` in MCP `command`/`args`
  (openai/codex#19582) and injects almost no env into the MCP child; `PLUGIN_ROOT` /
  `CLAUDE_PLUGIN_ROOT` exist for **hooks only**.
- **A `:-` default on `${CLAUDE_PLUGIN_ROOT}`.** Claude Code replaces the exact token in a pass that
  runs before `${VAR:-default}` interpolation and has no `:-` grammar, so the `:-` form skips it and
  reaches the env pass, which reads the harness's own environment and takes the default. The variable
  is real, but only in the spawned server's environment, set after `command`/`args` were expanded.
  Nothing fails at config load: the server dies on MODULE_NOT_FOUND against a path resolved from the
  project cwd, and the harness reports `-32000 Connection closed`. Keep `:-` for real env vars only.
- **Relative `cwd` everywhere.** Claude Code ignores `cwd` in `.mcp.json` (anthropics/claude-code#17565).
- **Overriding a plugin server from `~/.codex/config.toml`.** Codex refuses to load the config at all:
  `Error loading config.toml: invalid transport`. The only override path on Codex is `codex mcp add`,
  which replaces the plugin's server under the same name.

## Root cause

Two independent upstream gaps, one per harness, on the two mechanisms that can resolve an install
path. Neither harness supports both.

## Fix

One config per harness, each using that harness's working mechanism:

- Claude Code — `plugins/<name>/.mcp.json`: `${VAR:-default}` interpolation (Claude-Code-specific
  syntax), `${CLAUDE_PLUGIN_ROOT}` for artifact paths, env block with defaults.
- Codex CLI — `plugins/<name>/.codex-plugin/mcp.json`: relative `"cwd"` resolved against the installed
  plugin root (verified in codex-rs `plugin_config.rs`; the same pattern OpenAI's first-party
  `codex-security` plugin uses), no env block. `env_vars` names the variables Codex forwards, so a
  plugin server must still fall back to its own built-in defaults for one the user left unset.
  `tool_timeout_sec` raises the 300 s tool limit, and `default_tools_approval_mode` approves tools
  that `codex exec` would otherwise fail by having to ask. All three hold from a plugin's file
  (measured on Codex 0.164.0, in code mode only).

Codex's plugin.json schema is a superset of Claude's; its component pointers are `./`-relative paths.
Its marketplace file is `.agents/plugins/marketplace.json` with object-form `source`.

Those component pointers are optional where a default exists. No plugin here declares `"skills"`, and
none needs to: `plugin_skill_roots` (codex-rs `core-plugins/src/loader.rs`) falls back to
`<plugin root>/skills` when the key is absent, and a declared path REPLACES that default rather than
adding to it. First-party OpenAI plugins declare one anyway; that is their style, not a requirement.

## Prevention

`scripts/check-plugin-sync.ts` (`mise check:plugin-sync`, wired into `mise check` and the pre-commit)
enforces that the shared fields of the two plugin.json files stay identical, and that a plugin
declaring a server in its Codex manifest points at an artifact that exists and ships the Claude
`.mcp.json` beside it. A plugin with no server declares none and ships neither config, which the
check also enforces, so a dropped `mcpServers` pointer cannot leave a config behind and silently
switch the server assertions off.

A local marketplace install cannot prove a plugin is self-contained: `claude mcp list` showed Claude
Code running the server from the repo path rather than a cached copy, so a file outside
`plugins/<name>/` would still resolve. Prove that from a copy of the plugin outside the repo.

Watch openai/codex#19582: if `${VAR}` interpolation lands, the two configs can converge into one.
