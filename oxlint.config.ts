import agnostic from '@toverux/blanc-hopital/oxlint/agnostic';
import all from '@toverux/blanc-hopital/oxlint/all';
import { defineConfig } from 'oxlint';

// oxlint-disable-next-line import/no-default-export - oxlint interface
export default defineConfig({
  extends: [all, agnostic],
  ignorePatterns: ['dist'],
  rules: {
    // The server is Node/Bun-only (page-context code is kept self-contained by design and
    // cannot import anything anyway), so Node builtins are fine.
    'import/no-nodejs-modules': 'off',
    // This codebase is a polling CDP client: poll loops and single-WebSocket command
    // sequencing await in loops by design; Promise.all would be wrong there.
    'no-await-in-loop': 'off',
    // Promisifying event-based APIs (WebSocket open, CDP request/response correlation)
    // requires the Promise constructor.
    'promise/avoid-new': 'off'
  },
  overrides: [
    {
      files: ['server/src/config.ts'],
      rules: {
        // Config.ts is the designated env boundary; everything else must go through it.
        'node/no-process-env': 'off'
      }
    }
  ]
});
