/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the questions this check cannot decide. */

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Content check for the cs2-modding plugin's shipped prose, which is a deliverable rather than
// documentation of one and has no other automated coverage: no runtime, no server, no tests.
// The four rules below are the plugin's own contract, stated in plugins/cs2-modding/AGENTS.md,
// which is why this check names that plugin instead of discovering every plugin the way
// check-plugin-sync.ts does. Exits nonzero (via a failed assertion) on the first violation.
//
// The matching logic here is itself untested -- this repository has no TypeScript test suite and
// this check does not introduce the first one. Each of the four rules was instead verified once by
// hand, by planting a violation in the skills tree, watching the assertion fire with the offending
// file named, and removing the plant. Re-do that when you change a rule.

const repoRoot = path.resolve(import.meta.dirname, '..');

const skillsRoot = 'plugins/cs2-modding/skills';

// The setup skill's provisioning catalog is the single place a mod may be named, so it is both the
// source of the name list and the one file exempt from the no-leak rule.
const catalogPath = `${skillsRoot}/cs2-modding-setup/references/mod-catalog.md`;

const shippedFiles = listFilesRecursively(skillsRoot);

assert.ok(shippedFiles.length > 0, `No shipped files found under ${skillsRoot}.`);

checkNoModNameLeaks(shippedFiles);
checkVersionBaselines(shippedFiles);
checkVolatilityMarkers(shippedFiles);
checkPointersResolve(shippedFiles);

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
          `${where} writes "${name}", a display name the catalog declares ordinary English. Is ` +
            `this a credit or your own subject? A credit goes: state the technique on its own ` +
            `authority. The subject stays, and no prose is bent around the word.`
        );

        continue;
      }

      violation ??=
        `${where} names the mod "${name}", which the catalog lists. The mods corpus is input, ` +
        `never output: state the technique on its own authority. ${catalogPath} is the only ` +
        `file allowed to name a mod. Where the word is what your subject calls the thing, the ` +
        `fix is a catalog declaration ruled by the maintainer, never a reworded sentence.`;
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
// The two carry different strengths, because a match means different things. Only a slug is
// unambiguously a citation; a third of the display names are ordinary English, and one that is
// also what the game's own subject matter calls the thing cannot credit anybody -- no reader takes
// it for a name. Those are reported as a question instead of enforced, which is why the catalog
// declares them per entry rather than the lint guessing. Nothing leaves the list either way: the
// declaration changes what a match costs, not whether the lint looks.
function readModNames(): readonly CatalogName[] {
  const catalog = readShippedFile(catalogPath);

  // Read entry by entry rather than in file-wide sweeps: a declaration means nothing except
  // against the heading above it, and pairing each Source line with its own entry is a stricter
  // guard than counting headings against Source lines, which twenty of each satisfy even when they
  // belong to different entries.
  const entries = catalog.split(/^### /mu).slice(1);

  assert.ok(entries.length > 0, `${catalogPath} declares no "###" mod entries to read.`);

  return entries.flatMap(entry => {
    const [displayName = '', ...bodyLines] = entry.split('\n');
    const body = bodyLines.join('\n');
    const slug = body.match(/^Source: \[(?<slug>[^\]]+)\]/mu)?.groups?.slug;

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
      { name: displayName, isBlocking: !/^Ordinary word:/mu.test(body) }
    ];
  });
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
    }
  }
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
  const escaped = name.replaceAll(/[$()*+.?[\\\]^{|}]/gu, String.raw`\$&`);

  return new RegExp(String.raw`\b${escaped}\b`, 'u');
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
