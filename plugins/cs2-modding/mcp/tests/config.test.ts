import { describe, expect, test } from 'bun:test';
import { resolveUserDataPath } from '../src/config';

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
