/* oxlint-disable no-console, unicorn/no-process-exit -- CLI check script: the console is the report and the exit code is the verdict. */

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

// Freshness check for the gameface skill's baked version timeline.
// The gameface skill's version-gating.md timeline is immutable history and only rots
// by omission: releases newer than its recorded ceiling are simply missing.
// This script fetches the live Gameface feature changelog and lists every release version above the
// ceiling, so the timeline (and the ceiling marker) can be appended by hand.
// Exits nonzero when the timeline is stale. Network-dependent: run manually, not in CI.

const CHANGELOG_URL = 'https://docs.coherent-labs.com/cpp-gameface/changelog/feature/';
const TIMELINE_PATH = 'plugins/coherent-gameface/skills/gameface/references/version-gating.md';

const repoRoot = path.resolve(import.meta.dirname, '..');

const ceiling = await readTimelineCeiling();
const liveVersions = await fetchLiveVersions();

const newerVersions = liveVersions
  .filter(version => compareVersions(version, ceiling) > 0)
  .toSorted(compareVersions);

if (newerVersions.length == 0) {
  console.log(`Timeline is current: ceiling ${ceiling}, no newer release in the live changelog.`);
  process.exit(0);
}

console.error(`Timeline is STALE: ceiling is ${ceiling}, live changelog has newer releases:`);

for (const version of newerVersions) {
  console.error(`  - ${version}`);
}

console.error(
  `Append their notable web-platform entries to ${TIMELINE_PATH} ` +
    `and bump the timeline-ceiling marker.`
);

process.exit(1);

async function readTimelineCeiling(): Promise<string> {
  const markdown = await readFile(path.join(repoRoot, TIMELINE_PATH), 'utf8');
  const match = markdown.match(/<!-- timeline-ceiling: (?<ceiling>[\d.]+) -->/u);

  const ceilingVersion = match?.groups?.ceiling;

  assert.ok(
    ceilingVersion,
    `${TIMELINE_PATH} must contain a "<!-- timeline-ceiling: X.Y.Z -->" marker.`
  );

  return ceilingVersion;
}

async function fetchLiveVersions(): Promise<string[]> {
  const response = await fetch(CHANGELOG_URL);

  assert.ok(response.ok, `HTTP ${response.status} fetching ${CHANGELOG_URL}.`);

  const html = await response.text();

  // Only the page content is of interest; everything outside <main> is navigation chrome.
  const mainStart = html.indexOf('<main');

  assert.ok(mainStart !== -1, `Expected a <main> element in the changelog page.`);

  const main = html.slice(mainStart, html.lastIndexOf('</main>'));

  const versions = [...main.matchAll(/Version\s+(?<version>\d+(?:\.\d+){1,3})/gu)]
    .map(match => match.groups?.version)
    .filter((version): version is string => version != undefined);

  assert.ok(versions.length > 0, `Found no "Version X.Y.Z" headings in the changelog page.`);

  return [...new Set(versions)];
}

function compareVersions(a: string, b: string): number {
  const partsA = a.split('.').map(Number);
  const partsB = b.split('.').map(Number);

  for (let i = 0; i < Math.max(partsA.length, partsB.length); i++) {
    const delta = (partsA[i] ?? 0) - (partsB[i] ?? 0);

    if (delta != 0) {
      return delta;
    }
  }

  return 0;
}
