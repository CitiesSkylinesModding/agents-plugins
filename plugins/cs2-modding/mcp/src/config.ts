// The server's environment boundary. The server starts with none at all, as a plugin's must.

import os from 'node:os';
import path from 'node:path';
import process from 'node:process';

export const STEAM_APP_ID = 949_230;

/**
 * The game's user data folder, under the default Steam library's prefix on Linux.
 */
export function loadUserDataPath(): string {
  return resolveUserDataPath(process.env.CSII_USERDATAPATH, process.platform, os.homedir());
}

export function resolveUserDataPath(
  configured: string | undefined,
  platform: NodeJS.Platform,
  home: string
): string {
  if (configured) {
    return configured;
  }

  const profile =
    platform == 'win32'
      ? home
      : path.join(
          home,
          `.local/share/Steam/steamapps/compatdata/${STEAM_APP_ID}/pfx/drive_c/users/steamuser`
        );

  return path.join(profile, 'AppData/LocalLow/Colossal Order/Cities Skylines II');
}

/**
 * The environment `systemd-run --user` runs in: it exits 1 with neither `XDG_RUNTIME_DIR` nor
 * `DBUS_SESSION_BUS_ADDRESS` to find the user's service manager, and a harness may pass neither.
 */
export function userManagerEnvironment(): NodeJS.ProcessEnv {
  const { env } = process;

  return env.XDG_RUNTIME_DIR || env.DBUS_SESSION_BUS_ADDRESS
    ? env
    : { ...env, XDG_RUNTIME_DIR: `/run/user/${os.userInfo().uid}` };
}

/**
 * The environment Proton's `wine` runs in: English, since its answer is read.
 */
export function wineEnvironment(prefix: string): NodeJS.ProcessEnv {
  return { ...process.env, WINEPREFIX: prefix, LC_ALL: 'C' };
}
