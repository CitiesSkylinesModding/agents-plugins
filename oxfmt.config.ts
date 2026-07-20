import config from '@toverux/blanc-hopital/oxfmt';
import { defineConfig } from 'oxfmt';

// oxlint-disable-next-line import/no-default-export - oxfmt interface
export default defineConfig({
  ignorePatterns: [
    'dist',
    // .agents and .claude hold skills/rules synced from toverux/skills (see skills-lock.json);
    // formatting them would break the lock hashes.
    '.agents',
    '.claude',
    // .config/dotnet-tools.json is the dotnet local-tools manifest, managed by the dotnet CLI.
    '.config',
    // "vendor" holds vendored submodules (e.g., unity-devtools' Mono.Debugger.Soft): the tree must
    // stay pristine, and its sparse checkout carries root .md/.json files oxfmt would rewrite.
    'vendor',
    // "release-please" generates CHANGELOG.md files; reformatting them makes CI fail on release PRs
    // (dirty tree after the format check).
    '**/CHANGELOG.md',
    // The release-please json extra-files rewrite the unity dnx version pin here, re-expanding the
    // "args" array oxfmt would collapse; ignoring these keeps release commits CI-clean, same reason
    // as the CHANGELOG.md rule above.
    'plugins/unity-devtools/.mcp.json',
    'plugins/unity-devtools/.codex-plugin/mcp.json'
  ],
  ...config
});
