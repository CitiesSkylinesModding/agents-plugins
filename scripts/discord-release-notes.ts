/* oxlint-disable node/no-sync -- one-shot CLI script, synchronous IO is intentional. */
/* oxlint-disable eslint/no-console -- printing the message is this script's purpose. */

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

// Has an agent write a batch of GitHub releases as one Discord announcement.
// A release-please PR cuts all its releases on its merge commit, so the batch is every release
// sharing the target commit of the given tag, in publication order.
// The script resolves the headers, the links and the commit ranges, so the agent only words the
// bullets, from the commit messages.
// Usage: `bun scripts/discord-release-notes.ts [tag]` (defaults to the latest release).

const DISCORD_MESSAGE_LIMIT = 2000;

interface Release {
  readonly tag_name: string;
  readonly name: string;
  readonly html_url: string;
  readonly target_commitish: string;
  readonly published_at: string;
}

interface ReleaseConfig {
  readonly packages: Readonly<Record<string, { readonly component: string }>>;
  readonly plugins: ReadonlyArray<{ readonly groupName: string; readonly components: string[] }>;
}

interface Section {
  readonly header: string;
  readonly logCommand: string;
}

const config = JSON.parse(readFileSync('release-please-config.json', 'utf8')) as ReleaseConfig;

const releases = (
  JSON.parse(
    execFileSync('gh', ['api', '--paginate', '--slurp', 'repos/{owner}/{repo}/releases'], {
      encoding: 'utf8'
    })
  ) as Release[][]
).flat();

const [tag] = process.argv.slice(2);

const anchor =
  tag === undefined
    ? releases.toSorted((a, b) => b.published_at.localeCompare(a.published_at))[0]
    : releases.find(release => release.tag_name === tag);

assert.ok(anchor, tag === undefined ? 'No releases found.' : `No release tagged ${tag}.`);

// Release-please creates the tags on GitHub, and the commit ranges need them locally.
execFileSync('git', ['fetch', '--tags', '--quiet'], { stdio: 'inherit' });

const sections = releases
  .filter(release => release.target_commitish === anchor.target_commitish)
  .toSorted((a, b) => a.published_at.localeCompare(b.published_at))
  .flatMap(release => describeSection(release));

const message = execFileSync(
  'claude',
  [
    '--print',
    '--model=opus',
    '--effort=medium',
    '--allowedTools=Bash(git log:*)',
    // The repo's MCP servers would attach to a running game.
    '--strict-mcp-config',
    '--no-session-persistence'
  ],
  { input: buildPrompt(), encoding: 'utf8', stdio: ['pipe', 'pipe', 'inherit'] }
).trim();

console.log(message);

for (const { header } of sections) {
  if (!message.includes(header)) {
    console.warn(`\nWarning: header missing or altered: ${header}`);
  }
}

if (message.length > DISCORD_MESSAGE_LIMIT) {
  console.warn(
    `\nWarning: ${message.length} characters, over Discord's ${DISCORD_MESSAGE_LIMIT} limit.`
  );
}

function buildPrompt(): string {
  const plugins = sections
    .map(({ header, logCommand }) => `Header: ${header}\nCommits: \`${logCommand}\``)
    .join('\n\n');

  return `Write the Discord announcement for a release batch of this repo's agent plugins.
The readers are modders who use the plugins and skim: each bullet tells them, in plain words, what is different for them now.

For each plugin below, run its command and read the commit messages, bodies included.
The bodies say what a user gains, so they are all the reading this takes.

${plugins}

The message is one section per plugin, in the order above, with no blank line anywhere:

- The plugin's header line, copied character for character.
- Under it, a flat list of \`- \` bullets, the change that matters most to a user first.
- Each bullet is one short sentence stating an outcome the user can observe, in everyday words, the way you would tell a friend rather than the way the commit tells a maintainer.
- A headline change opens with a bold lead of a few words naming it; smaller ones are plain.
- Tool, parameter, and setting names go in backticks.
- Keep only changes a plugin user can observe. Refactors, tests, CI, tooling, and maintainer docs stay out, and a plugin left with nothing gets the single bullet "Maintenance release."

Example of the shape, with invented content:

## some-plugin [v1.4.0](<https://example.com/releases/some-plugin-v1.4.0>)
- **New \`inspect\` tool:** agents can now read a panel's state in one call.
- **Attaching works on Linux** without any extra setup.
- Long-running calls now time out instead of hanging the session.

Keep the whole message under ${DISCORD_MESSAGE_LIMIT - 100} characters.
Reply with the message alone: it is pasted into Discord as is.
`;
}

/**
 * Describes the section of the plugin a release belongs to.
 * A plugin's release units share a version and its commits, so only the unit named after the
 * plugin yields a section; the angle brackets keep Discord from embedding the link.
 */
function describeSection(release: Release): Section[] {
  // Release-please names a release "<component>: v<version>".
  const { component, version } =
    /^(?<component>.+): (?<version>v\S+)$/u.exec(release.name)?.groups ?? {};

  assert.ok(component && version, `Unexpected release name: ${release.name}`);

  const group = config.plugins.find(plugin => plugin.components.includes(component));

  if (group && group.groupName != component) {
    return [];
  }

  const [path] =
    Object.entries(config.packages).find(([, unit]) => unit.component == component) ?? [];

  assert.ok(path, `No release-please package for component ${component}.`);

  const tags = execFileSync('git', ['tag', '--list', `${component}-v*`, '--sort=-v:refname'], {
    encoding: 'utf8'
  })
    .trim()
    .split('\n');

  // A first release has no previous tag, and its range is the whole history of the path.
  const previous = tags[tags.indexOf(release.tag_name) + 1];
  const range = previous ? `${previous}..${release.tag_name}` : release.tag_name;

  return [
    {
      header: `## ${component} [${version}](<${release.html_url}>)`,
      logCommand: `git log --no-merges --format=%B%n---- ${range} -- ${path}`
    }
  ];
}
