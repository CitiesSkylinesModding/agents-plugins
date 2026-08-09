import agnostic from '@toverux/blanc-hopital/oxlint/agnostic';
import all from '@toverux/blanc-hopital/oxlint/all';
import { defineConfig } from 'oxlint';

// oxlint-disable-next-line import/no-default-export - oxlint interface
export default defineConfig({
  extends: [all, agnostic],
  // .agents holds assets synced verbatim from other repos (rules, and the hooks run by
  // .claude/settings.json); they live outside the tsconfig program, so type-aware rules only see
  // `error` types there, and fixing them in place would break the next sync.
  ignorePatterns: ['dist', '.agents'],
  rules: {
    // The server is Node/Bun-only (page-context code is kept self-contained by design and cannot
    // import anything anyway), so Node builtins are fine.
    'import/no-nodejs-modules': 'off',
    // This codebase is a polling CDP client: poll loops and single-WebSocket command sequencing
    // await in loops by design; Promise.all would be wrong there.
    'no-await-in-loop': 'off',
    // Promisifying event-based APIs (WebSocket open, CDP request/response correlation) requires the
    // Promise constructor.
    'promise/avoid-new': 'off',
    // Cohtml does not support those DOM APIs.
    'unicorn/prefer-dom-node-append': 'off',
    'unicorn/prefer-dom-node-dataset': 'off',
    'unicorn/prefer-dom-node-remove': 'off',
    'unicorn/prefer-dom-node-text-content': 'off',
    'unicorn/prefer-modern-dom-apis': 'off'
  },
  overrides: [
    {
      files: ['plugins/coherent-gameface/mcp/src/config.ts'],
      rules: {
        // Config.ts is the designated env boundary; everything else must go through it.
        'node/no-process-env': 'off'
      }
    },
    {
      files: ['bench/run.ts'],
      rules: {
        // The benchmark driver reads the CSII_* variables that locate the game and hands them to
        // the core: it is that tool's env boundary, the way config.ts is the server's.
        'node/no-process-env': 'off'
      }
    },
    {
      files: ['bench/tests/*.test.ts'],
      rules: {
        // A fixture's numbers are the test: naming each token count and score would say the same
        // thing twice, and the assertion is where the reader checks the arithmetic.
        'no-magic-numbers': 'off'
      }
    }
  ]
});
