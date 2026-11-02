/**
 * Cs2-dev monitor entry point, a plugin monitor on Claude Code.
 *
 * Each line it prints reaches the agent as a notification, whichever tool it is using.
 * A monitor that fails costs nothing else: the hint on tool results needs none of this.
 */

import process from 'node:process';
import { loadUserDataPath } from './config';
import { createGameHost } from './game-host';
import { watchForNews } from './monitor-loop';

const userDataPath = loadUserDataPath();

await watchForNews({
  host: createGameHost(userDataPath),
  userDataPath,
  print: line => {
    process.stdout.write(`${line}\n`);
  },
  isDone: () => false
});
