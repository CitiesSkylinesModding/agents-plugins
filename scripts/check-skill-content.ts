/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the questions this check cannot decide. */

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Content check for the cs2-modding plugin's shipped prose, which is a deliverable rather than
// documentation of one and has no other automated coverage: no runtime, no server, no tests.
// The rules below are the plugin's own contract, stated in plugins/cs2-modding/AGENTS.md, which is
// why this check names that plugin instead of discovering every plugin the way
// check-plugin-sync.ts does. Exits nonzero (via a failed assertion) on the first violation.
//
// The matching logic here is itself untested -- this repository has no TypeScript test suite and
// this check does not introduce the first one. Each rule was instead verified once by hand, by
// planting a violation in the skills tree, watching the assertion fire with the offending file
// named, and removing the plant. Re-do that when you change a rule, and for a rule with more than
// one failure mode plant one violation per mode: the reachability rule fails both when a sibling
// is unlinked and when a folder has no entry file, and only the first is obvious to plant.

const repoRoot = path.resolve(import.meta.dirname, '..');

const pluginRoot = 'plugins/cs2-modding';
const skillsRoot = `${pluginRoot}/skills`;

// The setup skill's provisioning catalog is the single place a mod may be named, so it is both the
// source of the name list and the one file exempt from the no-leak rule.
const catalogPath = `${skillsRoot}/cs2-modding-setup/references/mod-catalog.md`;

// Display names that are also ordinary English: what the game's own subject matter calls the
// thing, which no reader takes for a name, so shipped prose writing one credits nobody. Their
// match is reported rather than enforced; every other display name, and every owner/repo slug,
// still fails the check.
//
// The list lives here and not beside the entry it exempts, because the catalog ships to an agent
// provisioning a mod corpus and a lint's own policy is nothing to that reader. Adding to it is the
// maintainer's call, and the trigger is this check failing on a word a reference's subject
// genuinely owns -- its wiki hub, its info view, a heading it cannot avoid writing. The test is
// whether the word carries the mod's identity: "Traffic" is what the game simulates, while
// "Node Controller" and "Advanced Line Tool" are names a reader can only read as names.
const ordinaryWordNames: ReadonlySet<string> = new Set(['Traffic']);

const shippedFiles = listFilesRecursively(skillsRoot);

assert.ok(shippedFiles.length > 0, `No shipped files found under ${skillsRoot}.`);

checkNoModNameLeaks(shippedFiles);
checkVersionBaselines(shippedFiles);
checkVolatilityMarkers(shippedFiles);
checkEvidenceMarkers(shippedFiles);
checkPointersResolve(shippedFiles);
checkDisclosedFilesAreReachable(shippedFiles);

// The mods corpus is input, never output: knowledge prose states a technique on its own authority
// and never credits the mod it was learned from. One forgetful authoring pass is all it takes to
// break that, and the leak reads as perfectly natural prose, so nothing but a machine catches it.
function checkNoModNameLeaks(files: readonly string[]): void {
  const modNames = readModNames();
  const questions: string[] = [];

  let violation: string | undefined;

  for (const file of files) {
    if (file == catalogPath) {
      continue;
    }

    const content = readShippedFile(file);

    for (const { name, isBlocking } of modNames) {
      const match = content.match(wholeWordPattern(name));

      if (match?.index == null) {
        continue;
      }

      const where = `${file}:${lineOf(content, match.index)}`;

      if (!isBlocking) {
        questions.push(
          `${where} writes "${name}", a display name this check treats as ordinary English. Is ` +
            `this a credit or your own subject? A credit goes: state the technique on its own ` +
            `authority. The subject stays, and no prose is bent around the word.`
        );

        continue;
      }

      violation ??=
        `${where} names the mod "${name}", which the catalog lists. The mods corpus is input, ` +
        `never output: state the technique on its own authority. ${catalogPath} is the only ` +
        `file allowed to name a mod. Where the word is what your subject calls the thing, the ` +
        `fix is the maintainer adding it to this check's ordinary-word list, never a reworded ` +
        `sentence.`;
    }
  }

  // Printed before the assertion, so a run that also fails still hands its questions over.
  for (const question of questions) {
    console.warn(`WARNING: ${question}`);
  }

  assert.ok(violation == null, violation);
}

interface CatalogName {
  readonly name: string;
  readonly isBlocking: boolean;
}

// Both spellings of every catalogued mod: the published display name that a leak would use in
// prose, and the owner/repo slug that a leak would use in a link.
//
// Nothing leaves the list when a name is declared ordinary: the declaration changes what a match
// costs, not whether the lint looks.
//
// The catalog carries no word about any of this. It ships to an agent provisioning a mod corpus,
// so the shape read here is a contract this file states and that file only has to keep: a "###"
// heading carrying the display name, and a "Source:" line whose link text is owner/repo.
function readModNames(): readonly CatalogName[] {
  const catalog = readShippedFile(catalogPath);

  // Read entry by entry rather than in file-wide sweeps: pairing each Source line with the heading
  // above it is a stricter guard than counting headings against Source lines, which twenty of each
  // satisfy even when they belong to different entries.
  const entries = catalog.split(/^### /mu).slice(1);

  assert.ok(entries.length > 0, `${catalogPath} declares no "###" mod entries to read.`);

  const names = entries.flatMap(entry => {
    const [displayName = '', ...bodyLines] = entry.split('\n');
    const slug = bodyLines.join('\n').match(/^Source: \[(?<slug>[^\]]+)\]/mu)?.groups?.slug;

    // An entry that drops either spelling takes it off the name list, and silently shrinking that
    // list is the one failure that would let a leak through unnoticed.
    assert.ok(
      displayName.length > 0 && slug != null,
      `${catalogPath} has an entry ("${displayName}") that is not a "###" display name followed ` +
        `by a "Source: [owner/repo](...)" line. Every entry needs both, or the missing spelling ` +
        `escapes this check.`
    );

    return [
      { name: slug, isBlocking: true },
      { name: displayName, isBlocking: !ordinaryWordNames.has(displayName) }
    ];
  });

  // A declared word no entry carries is dead policy, and it reads as protection that is not there.
  for (const declared of ordinaryWordNames) {
    assert.ok(
      names.some(({ name }) => name == declared),
      `"${declared}" is declared an ordinary word, but ${catalogPath} has no "###" entry under ` +
        `that name. Drop it here, or match it to the name the entry now carries.`
    );
  }

  return names;
}

// Every reference states, once, the game version its facts were verified against, so a reader can
// judge its age against their installed game. The trunk bodies carry their own baseline for the
// skill as a whole; a reference is the unit that goes stale on its own.
function checkVersionBaselines(files: readonly string[]): void {
  for (const file of files) {
    if (!isReference(file)) {
      continue;
    }

    const baselines = readShippedFile(file).match(/^Verified against game version .+\.$/gmu) ?? [];

    assert.equal(
      baselines.length,
      1,
      `${file} carries ${baselines.length} version baselines; a reference states exactly one ` +
        `line reading "Verified against game version <version>."`
    );
  }
}

// Volatile claims are found by grepping the marker, and that grep is the maintenance checklist for
// the next game version, so a marker spelled any other way is a claim that will not be re-checked.
// Hence the strict reading: every occurrence of the word in shipped prose must be the token. A
// reference that wants the English adjective rephrases instead of weakening the grep.
function checkVolatilityMarkers(files: readonly string[]): void {
  const token = 'VOLATILE';

  for (const file of files) {
    const content = readShippedFile(file);

    for (const match of content.matchAll(/volatile/giu)) {
      if (match.index == null) {
        continue;
      }

      const isToken = match[0] == token && content[match.index + token.length] == ':';

      assert.ok(
        isToken,
        `${file}:${lineOf(content, match.index)} spells the volatility marker "${match[0]}"; the ` +
          `only spelling is "${token}:", so that grepping it yields every claim to re-check.`
      );
    }
  }
}

// The sibling of the volatility rule, for the other half of the maintenance work. A volatile claim
// was established and rots; an unverified one was never established, and only a maintainer with a
// running game can close it. Six references invented six phrasings for that state before the token
// existed, so none of them was greppable and none read as the same kind of claim -- hence the same
// strict reading: every occurrence of the word in shipped prose must be the token.
function checkEvidenceMarkers(files: readonly string[]): void {
  const token = 'UNVERIFIED';

  for (const file of files) {
    const content = readShippedFile(file);

    for (const match of content.matchAll(/unverified/giu)) {
      if (match.index == null) {
        continue;
      }

      const isToken = match[0] == token && content[match.index + token.length] == ':';

      assert.ok(
        isToken,
        `${file}:${lineOf(content, match.index)} spells the evidence marker "${match[0]}"; the ` +
          `only spelling is "${token}:", so that grepping it yields every claim to confirm.`
      );
    }
  }
}

// Progressive disclosure is a chain of pointers, and a broken link costs the reader the whole tier
// below it. Applied to every shipped file rather than the trunk bodies alone: references
// cross-reference each other, and a dead end there reads no better.
function checkPointersResolve(files: readonly string[]): void {
  for (const file of files) {
    const content = readShippedFile(file);

    // The optional trailing title is matched rather than ignored: a target that stops at the first
    // space would leave a titled link matching nothing at all, and an unmatched link is an
    // unchecked one -- the silent gap this rule exists to close.
    for (const match of content.matchAll(/\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+"[^"]*")?\)/gu)) {
      const target = match.groups?.target;

      if (target == null || match.index == null || !isLocalPointer(target)) {
        continue;
      }

      // A fragment addresses a heading inside the target; only the file half is checkable here.
      const [targetPath] = target.split('#');

      if (targetPath == null || targetPath.length == 0) {
        continue;
      }

      const resolved = path.join(repoRoot, path.dirname(file), decodeURIComponent(targetPath));

      assert.ok(
        existsSync(resolved),
        `${file}:${lineOf(content, match.index)} points at "${target}", which does not exist. ` +
          `Progressive disclosure dead-ends there.`
      );

      // Existence alone passes a link that climbs out of the plugin, because the target does exist
      // here -- and a marketplace install copies only the plugin directory, so the same link dead-
      // ends for every user while this check stays green. That is the one broken pointer existence
      // cannot see, which is why containment is asserted rather than left to prose.
      assert.ok(
        !path.relative(path.join(repoRoot, pluginRoot), resolved).startsWith('..'),
        `${file}:${lineOf(content, match.index)} points at "${target}", which resolves outside ` +
          `${pluginRoot}. It works here and dead-ends for everyone who installs the plugin, ` +
          `since the install copies that directory alone. State the fact instead of linking.`
      );
    }
  }
}

// Pointer checking runs one way -- every link resolves -- which leaves the other way unchecked: a
// disclosed sub-file nothing links to. It ships, costs an install its bytes, and is read by nobody,
// and no other check can see it because the failure is the absence of a link rather than a bad one.
// A reference is a folder whose entry file repeats the topic name, so the entry file is the only
// place a sibling can be reached from. A file sitting directly under a flat "references" directory
// is reached from its skill's own body instead, and is out of scope here.
function checkDisclosedFilesAreReachable(files: readonly string[]): void {
  for (const file of files) {
    if (!isReference(file)) {
      continue;
    }

    const folder = path.dirname(file);
    const topic = path.basename(folder);
    const entryFile = `${folder}/${topic}.md`;

    if (topic == 'references' || file == entryFile) {
      continue;
    }

    // Asserted rather than skipped. A folder whose entry file is missing or misnamed leaves every
    // file in it unreachable at once, which is the worse form of the defect this rule is for --
    // and skipping it would exempt exactly the case with the most to lose.
    assert.ok(
      files.includes(entryFile),
      `${file} sits in a reference folder with no ${topic}.md beside it, so nothing reaches it. ` +
        `A reference is a folder whose entry file repeats the topic name.`
    );

    assert.ok(
      linkToPattern(path.basename(file)).test(readShippedFile(entryFile)),
      `${file} is disclosed into a reference folder and ${entryFile} never links to it, so no ` +
        `reader arrives. Link it by bare filename, or fold it back into the entry file.`
    );
  }
}

// The link grammar rather than a substring, for two reasons a substring gets wrong: a bare prose
// mention (or a name inside a code fence) satisfies it while giving the reader nothing to follow,
// and a plain match is a suffix match, so linking "what-gets-patched.md" would silently cover a
// "gets-patched.md" that nothing points at.
//
// The optional fragment and title are the same two the pointer rule above accepts. Matching a
// narrower grammar than that one would fail a link it resolves, on a message telling the author to
// write the link they already wrote.
function linkToPattern(fileName: string): RegExp {
  const escaped = escapeForPattern(fileName);

  return new RegExp(String.raw`\]\(\.?/?${escaped}(?:#[^)\s]*)?(?:\s+"[^"]*")?\)`, 'u');
}

function isLocalPointer(target: string): boolean {
  return !/^[a-z][a-z0-9+.-]*:/iu.test(target) && !target.startsWith('#');
}

// A reference is anything below a "references" directory, which is what separates it from a trunk
// body. Any depth, not just the first: the two reference families may nest a level further, and
// matching the immediate parent alone would exempt every reference from the baseline rule while
// still exiting green.
function isReference(file: string): boolean {
  return file.endsWith('.md') && file.includes('/references/');
}

// Case-sensitive and whole-word: lowercase prose is not a mod name, and a game type whose name
// embeds one ("TrafficFlowSystem") is not a citation either.
function wholeWordPattern(name: string): RegExp {
  return new RegExp(String.raw`\b${escapeForPattern(name)}\b`, 'u');
}

// Shared, because every pattern here is built from a name off the disk or out of the catalog. One
// builder escaping a narrower set than the other is a hole that opens on the first name carrying a
// metacharacter, and it opens two ways: an unbalanced bracket throws out of `new RegExp` and takes
// the whole run down with a stack trace instead of a named file, while a parenthesis quietly
// becomes a capture group and changes what the rule matches.
function escapeForPattern(text: string): string {
  return text.replaceAll(/[$()*+.?[\\\]^{|}]/gu, String.raw`\$&`);
}

function lineOf(content: string, index: number): number {
  return content.slice(0, index).split('\n').length;
}

// Repo-root-relative paths with forward slashes, so a message reads the same on either platform.
function listFilesRecursively(relativeDir: string): string[] {
  return readdirSync(path.join(repoRoot, relativeDir), { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory()
      ? listFilesRecursively(`${relativeDir}/${entry.name}`)
      : [`${relativeDir}/${entry.name}`]
  );
}

function readShippedFile(relativePath: string): string {
  return readFileSync(path.join(repoRoot, relativePath), 'utf8');
}
