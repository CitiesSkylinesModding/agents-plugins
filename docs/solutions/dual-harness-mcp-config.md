---
date: 2026-07-29
area: plugins/*/(.claude-plugin|.codex-plugin)
symptoms:
  - '`${CLAUDE_PLUGIN_ROOT}` reaches the MCP child verbatim on Codex CLI'
  - 'Error loading config.toml: invalid transport'
  - 'plugin server starts in the wrong working directory on Claude Code'
tags: [mcp, plugin-manifest, codex-cli, claude-code]
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
  `codex-security` plugin uses), no env block. A plugin server must therefore fall back to its own
  built-in defaults, since Codex passes nothing.

Codex's plugin.json schema is a superset of Claude's; its component pointers are `./`-relative paths.
Its marketplace file is `.agents/plugins/marketplace.json` with object-form `source`.

## Prevention

`scripts/check-plugin-sync.ts` (`mise check:plugin-sync`, wired into `mise check` and the pre-commit)
enforces that the shared fields of the two plugin.json files stay identical and that each
`.codex-plugin/mcp.json` points at an artifact that exists.

Watch openai/codex#19582: if `${VAR}` interpolation lands, the two configs can converge into one.
