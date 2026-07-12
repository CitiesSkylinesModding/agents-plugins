import config from '@toverux/blanc-hopital/oxfmt';
import { defineConfig } from 'oxfmt';

// oxlint-disable-next-line import/no-default-export - oxfmt interface
export default defineConfig({
  // .agents and .claude hold skills/rules synced from toverux/skills (see skills-lock.json);
  // formatting them would break the lock hashes.
  ignorePatterns: ['dist', '.agents', '.claude'],
  ...config
});
