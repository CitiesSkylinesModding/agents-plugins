import { describe, expect, test } from 'bun:test';
import { readFile } from 'node:fs/promises';
import { resolveUserDataPath } from '../src/config';
import {
  READ_CEILING_SECONDS,
  RESTART_CEILING_SECONDS,
  STOP_CEILING_SECONDS,
  TEST_CEILING_SECONDS,
  WAIT_LOAD_CEILING_SECONDS
} from '../src/cycle';
import { HINT_CEILING_SECONDS } from '../src/tool-run';

describe('resolveUserDataPath', () => {
  test(`takes the configured folder`, () => {
    expect(resolveUserDataPath('/games/cs2', 'linux', '/home/a')).toBe('/games/cs2');
  });

  test(`falls back to the default Steam library's prefix on Linux`, () => {
    expect(resolveUserDataPath(undefined, 'linux', '/home/a')).toBe(
      '/home/a/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/' +
        'AppData/LocalLow/Colossal Order/Cities Skylines II'
    );
  });

  test(`takes an empty variable for an absent one`, () => {
    expect(resolveUserDataPath('', 'linux', '/home/a')).toContain('/home/a/.local/share/Steam');
  });
});

describe('the Codex config', () => {
  test(`outlasts every tool's ceiling, as a Codex timeout cancels nothing`, async () => {
    const config = JSON.parse(
      await readFile(new URL('../../.codex-plugin/mcp.json', import.meta.url), 'utf8')
    ) as { mcpServers: { 'cs2-dev': { tool_timeout_sec: number } } };

    const longest = Math.max(
      READ_CEILING_SECONDS,
      RESTART_CEILING_SECONDS,
      STOP_CEILING_SECONDS,
      TEST_CEILING_SECONDS,
      WAIT_LOAD_CEILING_SECONDS
    );

    expect(config.mcpServers['cs2-dev'].tool_timeout_sec).toBeGreaterThan(
      longest + HINT_CEILING_SECONDS
    );
  });
});
