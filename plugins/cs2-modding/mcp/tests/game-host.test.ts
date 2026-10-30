import { afterEach, beforeEach, describe, expect, spyOn, test } from 'bun:test';
import { ChildProcess } from 'node:child_process';
import { chmod, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir, userInfo } from 'node:os';
import path from 'node:path';
import process from 'node:process';
import {
  createGameHost,
  type GameHost,
  parsePgrep,
  parseReapers,
  parseTasklist,
  run
} from '../src/game-host';

function readFixture(name: string): Promise<string> {
  return readFile(path.join(import.meta.dirname, 'fixtures', name), 'utf8');
}

async function failureOf(call: Promise<unknown>): Promise<string> {
  try {
    await call;
  } catch (error) {
    return String(error);
  }

  return 'no failure';
}

describe('parseTasklist', () => {
  test(`reads the PID of the game's row`, async () => {
    expect(parseTasklist(await readFixture('tasklist.csv'))).toEqual([308]);
  });

  test(`reads no PID from the line of an empty list`, async () => {
    expect(parseTasklist(await readFixture('tasklist-empty.csv'))).toEqual([]);
  });
});

describe('parsePgrep', () => {
  test(`reads each PID, and nothing from an empty answer`, () => {
    expect(parsePgrep('274761\n308\n')).toEqual([274_761, 308]);
    expect(parsePgrep('')).toEqual([]);
  });

  test(`never yields a PID that addresses a process group`, () => {
    expect(parsePgrep('0\n-1\nnonsense\n')).toEqual([]);
  });
});

describe('parseReapers', () => {
  const reaper = '/home/a/.local/share/Steam/ubuntu12_32/reaper SteamLaunch';

  test(`reads the reaper of the game`, () => {
    const output = `278591 ${reaper} AppId=949230 -- /steam/_v2-entry-point --verb=waitforexitandrun`;

    expect(parseReapers(output)).toEqual([278_591]);
  });

  test(`leaves the reapers of other games`, () => {
    const output = [
      `1 ${reaper} AppId=570 -- x`,
      `2 ${reaper} AppId=9492301 -- x`,
      `3 ${reaper} AppId=49230 -- x`
    ].join('\n');

    expect(parseReapers(output)).toEqual([]);
  });
});

describe('run', () => {
  test(`returns the output and the exit code`, async () => {
    expect(await run('sh', ['-c', 'echo hello; exit 3'])).toEqual({
      output: 'hello\n',
      errorOutput: '',
      code: 3
    });
  });

  test(`rejects on a command that does not answer, rather than reading it as empty`, async () => {
    expect(await failureOf(run('sleep', ['5'], { timeoutMilliseconds: 50 }))).toContain(
      'did not answer'
    );
  });

  test(`rejects on a command that cannot start`, async () => {
    expect(await failureOf(run('/nowhere/at/all', []))).toContain('nowhere');
  });

  test(`returns despite a background process holding the pipe`, async () => {
    const start = Date.now();
    const { output } = await run('sh', ['-c', 'sleep 3 & echo hello']);

    expect(output).toBe('hello\n');
    expect(Date.now() - start).toBeLessThan(1000);
  });

  test(`gives up on a process that survives its kill`, async () => {
    const kill = spyOn(ChildProcess.prototype, 'kill').mockReturnValue(false);
    const unref = spyOn(ChildProcess.prototype, 'unref');
    const start = Date.now();

    try {
      expect(await failureOf(run('sleep', ['3'], { timeoutMilliseconds: 50 }))).toContain(
        'did not answer'
      );

      expect(kill).toHaveBeenCalledWith('SIGKILL');
      expect(unref).toHaveBeenCalled();
      expect(Date.now() - start).toBeLessThan(1000);
    } finally {
      kill.mockRestore();
      unref.mockRestore();
    }
  });
});

describe.if(process.platform == 'linux')('the Linux host', () => {
  const { env } = process;

  let stubs: string;
  let host: GameHost;

  beforeEach(async () => {
    stubs = await mkdtemp(path.join(tmpdir(), 'cs2-dev-stubs-'));
    host = createGameHost(path.join(stubs, 'userdata'));
    process.env = { ...env, PATH: `${stubs}${path.delimiter}${env.PATH ?? ''}` };
  });

  afterEach(async () => {
    process.env = env;

    await rm(stubs, { recursive: true });
  });

  async function writeStub(name: string, script: string): Promise<void> {
    const stubPath = path.join(stubs, name);

    await writeFile(stubPath, `#!/bin/sh\n${script}\n`);
    await chmod(stubPath, 0o755);
  }

  test(`reads a pgrep that matched nothing as no process`, async () => {
    await writeStub('pgrep', 'exit 1');

    expect(await host.findPids()).toEqual({ game: [], reapers: [] });
  });

  test.each(['exit 2', 'exit 3', 'kill -9 $$'])(
    `never reads a pgrep that failed as a stopped game: %s`,
    async script => {
      await writeStub('pgrep', script);

      expect(await failureOf(host.findPids())).toContain('pgrep');
    }
  );

  test(`fails a launch systemd-run refused`, async () => {
    await writeStub('systemd-run', 'exit 1');

    expect(await failureOf(host.launch())).toContain('systemd-run');
  });

  test(`says why a launch failed`, async () => {
    await writeStub('systemd-run', 'echo "Failed to connect to bus" >&2; exit 1');

    expect(await failureOf(host.launch())).toContain(`Failed to connect to bus`);
  });

  test(`says why a close never left, past the noise of wine`, async () => {
    const proton = path.join(stubs, 'proton');
    const compatData = path.join(stubs, 'compatdata');

    await mkdir(path.join(proton, 'bin'), { recursive: true });
    await mkdir(compatData);
    await writeFile(path.join(compatData, 'config_info'), `10.0\n${proton}/share/fonts/\n`);
    await writeFile(
      path.join(proton, 'bin', 'wine'),
      `#!/bin/sh\necho "using server-side synchronization." >&2\necho "wine: not owned" >&2\nexit 1\n`
    );
    await chmod(path.join(proton, 'bin', 'wine'), 0o755);

    const answer = await createGameHost(path.join(compatData, 'pfx', 'users')).askToEnd();

    expect(answer).toBe(`wine taskkill answered "wine: not owned".`);
  });

  test(`says why it could not read the prefix's config_info`, async () => {
    const compatData = path.join(stubs, 'compatdata');

    await mkdir(path.join(compatData, 'config_info'), { recursive: true });

    const answer = await createGameHost(path.join(compatData, 'pfx', 'users')).askToEnd();

    expect(answer).toContain(`could not be read`);
    expect(answer).toContain(`EISDIR`);
  });

  test(`cuts the prefix at its own folder, under a library in a folder named alike`, async () => {
    const proton = path.join(stubs, 'proton');
    const compatData = path.join(stubs, 'pfx', 'library', 'compatdata');

    await mkdir(path.join(proton, 'bin'), { recursive: true });
    await mkdir(compatData, { recursive: true });
    await writeFile(path.join(compatData, 'config_info'), `10.0\n${proton}/share/fonts/\n`);
    await writeFile(path.join(proton, 'bin', 'wine'), `#!/bin/sh\necho "Close message sent"\n`);
    await chmod(path.join(proton, 'bin', 'wine'), 0o755);

    const answer = await createGameHost(path.join(compatData, 'pfx', 'users')).askToEnd();

    expect(answer).toBeUndefined();
  });

  test(`finds the user's service manager with no environment to say where`, async () => {
    const seen = path.join(stubs, 'seen');

    await writeStub('systemd-run', `echo "$XDG_RUNTIME_DIR" > '${seen}'`);

    const { XDG_RUNTIME_DIR: _dir, DBUS_SESSION_BUS_ADDRESS: _bus, ...bare } = process.env;

    process.env = bare;

    await host.launch();

    expect(await readFile(seen, 'utf8')).toBe(`/run/user/${userInfo().uid}\n`);
  });

  test(`leaves the environment's own way to the service manager alone`, async () => {
    const seen = path.join(stubs, 'seen');

    await writeStub('systemd-run', `echo "$XDG_RUNTIME_DIR" > '${seen}'`);

    process.env = { ...process.env, XDG_RUNTIME_DIR: '/run/elsewhere' };

    await host.launch();

    expect(await readFile(seen, 'utf8')).toBe('/run/elsewhere\n');
  });
});
