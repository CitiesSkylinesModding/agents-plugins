/* oxlint-disable node/no-sync -- one-shot CLI script, synchronous IO is intentional. */
/* oxlint-disable eslint/no-console -- printing the message is this script's purpose. */

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';

// Formats a batch of GitHub releases as one Discord announcement.
// A release-please PR cuts all its releases on its merge commit, so the batch is every release
// sharing the target commit of the given tag, in publication order.
// Usage: `bun scripts/discord-release-notes.ts [tag]` (defaults to the latest release).

const DISCORD_MESSAGE_LIMIT = 2000;

interface Release {
  readonly tag_name: string;
  readonly name: string;
  readonly body: string;
  readonly html_url: string;
  readonly target_commitish: string;
  readonly published_at: string;
}

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

const message = releases
  .filter(release => release.target_commitish === anchor.target_commitish)
  .toSorted((a, b) => a.published_at.localeCompare(b.published_at))
  .map(release => formatRelease(release))
  .join('\n');

console.log(message);

if (message.length > DISCORD_MESSAGE_LIMIT) {
  console.warn(
    `\nWarning: ${message.length} characters, over Discord's ${DISCORD_MESSAGE_LIMIT} limit.`
  );
}

function formatRelease(release: Release): string {
  // Release-please names a release "<component>: v<version>".
  const { component, version } =
    /^(?<component>.+): (?<version>v\S+)$/u.exec(release.name)?.groups ?? {};

  assert.ok(component && version, `Unexpected release name: ${release.name}`);

  // The body opens with a "## [version](compare link) (date)" heading the new title replaces, and
  // its blank lines would render as gaps between the releases.
  const lines = release.body
    .split(/\r?\n/u)
    .filter(line => line.trim() !== '' && !line.startsWith('## '));

  return [`## ${component}: [${version}](${release.html_url})`, ...lines].join('\n');
}
