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
    // "vendor" holds third-party sources, kept as fetched but for the patches their VENDOR.md
    // records (e.g. unity-devtools' Mono.Debugger.Soft): reformatting one would put our own
    // whitespace into every upstream diff taken against it from then on.
    'vendor',
    // Markdown here is mostly agent-facing prose, where every character is context an agent pays
    // for: oxfmt pads table cells to a common column width, and that padding is pure cost with no
    // option to turn it off. Ignoring .md also covers the CHANGELOG.md files release-please
    // generates, whose reformatting made CI fail on release PRs (dirty tree after the check).
    '**/*.md',
    // The release-please json extra-files rewrite the unity dnx version pin here, re-expanding the
    // "args" array oxfmt would collapse; ignoring these keeps release commits CI-clean, the same
    // reason the generated CHANGELOG.md files above are out.
    'plugins/unity-devtools/.mcp.json',
    'plugins/unity-devtools/.codex-plugin/mcp.json'
  ],
  ...config
});
