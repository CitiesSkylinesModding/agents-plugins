/* oxlint-disable node/no-sync -- sequential check script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the questions this check cannot decide. */

import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Content check for the agent-facing prose, which is a deliverable rather than documentation of
// one and has no other automated coverage. The cs2-modding skills take the trap contract stated in
// plugins/cs2-modding/AGENTS.md, which is why this check names that plugin instead of discovering
// every plugin the way check-plugin-sync.ts does; the gameface skills take their marker and
// baseline rules; the line-cite ban reaches every maintained file an agent loads. Exits nonzero
// (via a failed assertion) on the first violation.
//
// The matching logic here is itself untested: no test suite runs this check. Each rule was instead
// verified once by hand, by planting a violation, watching the assertion fire with the offending
// file named, and removing the plant. Re-do that when you change a rule, once per failure mode.

const repoRoot = path.resolve(import.meta.dirname, '..');

const pluginRoot = 'plugins/cs2-modding';
const skillsRoot = `${pluginRoot}/skills`;

// The gameface update sweep finds the volatility marker and the baseline line by grep, so those two
// rules reach this tree.
const gamefaceSkillsRoot = 'plugins/coherent-gameface/skills';
const gamefaceBaselinePattern = /^Verified against Cohtml (?<version>[\d.]*\d)\.$/u;

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
// maintainer's call, and the trigger is this check failing on a word a skill's subject genuinely
// owns. The test is whether the word carries the mod's identity: "Traffic" is what the game
// simulates, while "Node Controller" and "Advanced Line Tool" are names a reader can only read as
// names.
const ordinaryWordNames: ReadonlySet<string> = new Set(['Traffic']);

// Each body's line count once seeded, rounded up to the next 25. A ceiling is what keeps the trap
// list from regrowing a line at a time, so raising one is a commit of its own: growth stays a
// visible decision, and a new trap past the limit displaces a weaker one instead.
const bodyCeilings: Readonly<Record<string, number>> = {
  'cs2-modding': 125,
  'cs2-modding-ui': 50,
  'cs2-mod-project': 75,
  'cs2-modding-setup': 200
};

// The trap check picks markers out by this exact form, so any other spelling is a claim no game
// update will flag. A symbol is a dotted name; `none` marks a claim resting on no type.
const symbolMarkerPattern = /^\[symbol: (?:none|[A-Z_a-z]\w*(?:\.[A-Z_a-z]\w*)*)\]$/u;
const symbolMarkerCandidatePattern = /\[\s*symbols?\s*:[^\]]*\]?/giu;

// The trap check also reads the server's log-line markers, so they take the same rule.
const serverSourceRoot = `${pluginRoot}/mcp/src`;

// A source file name followed by a line number or range: `Foo.cs:120`, `Foo.cs:120-140`,
// `Foo.cs:120ff`, `foo.js#L12`. A line cite into the decompile shifts with every game update, and
// one into this repo's own files shifts with every edit above it.
const lineCitePattern = /\b[\w.-]+\.(?:cs|js|jsx|ts|tsx|mjs|cjs|css|scss|md)(?::\d[\w-]*|#L\d+)/u;

// The maintained prose an agent loads. The server's tests and fixtures stay out, since their stack
// traces are captured output, and so do ADRs 0003 through 0007: superseded records keep their text.
const lineCiteRoots: readonly string[] = [
  'plugins/coherent-gameface/skills',
  'plugins/cs2-modding/skills',
  'plugins/unity-devtools/skills',
  '.agents/rules',
  '.agents/skills',
  'docs'
];
const lineCiteExempt = /^docs\/adr\/000[3-7]-/u;

// Every rule reads the whole of each file it judges, so an uncached read costs one decode per rule
// per file on a path the pre-commit hook runs on every commit. The process is single-shot, so there
// is no staleness window to trade for it. Declared with the constants rather than beside its reader
// because the rules run at module top level, before a later `const` would initialize.
const shippedFileCache = new Map<string, string>();

const shippedFiles = listFilesRecursively(skillsRoot);

assert.ok(shippedFiles.length > 0, `No shipped files found under ${skillsRoot}.`);

checkNoModNameLeaks(shippedFiles);
checkPointersResolve(shippedFiles);
checkBodyCeilings(shippedFiles);
checkSymbolMarkers([...shippedFiles, ...listFilesRecursively(serverSourceRoot)]);
checkNoLineCites(listLineCiteScope());

const gamefaceSkillFiles = listFilesRecursively(gamefaceSkillsRoot);

checkVolatilityMarkers(gamefaceSkillFiles);
checkGamefaceBaselines(gamefaceSkillFiles);

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

// The gameface baseline belongs to a skill as a whole: one engine version moves every gameface claim
// at once. Two versions across the tree are legitimate only mid-sweep, which is never a state to
// commit.
function checkGamefaceBaselines(files: readonly string[]): void {
  const versions = new Set<string>();

  for (const file of files.filter(candidate => path.basename(candidate) == 'SKILL.md')) {
    const baselines = readShippedLines(file).flatMap(
      line => gamefaceBaselinePattern.exec(line)?.groups?.version ?? []
    );

    assert.equal(
      baselines.length,
      1,
      `${file} carries ${baselines.length} version baselines; a gameface skill states exactly ` +
        `one line reading "Verified against Cohtml <version>."`
    );

    versions.add(baselines[0] ?? '');
  }

  assert.equal(
    versions.size,
    1,
    `The gameface skills state ${versions.size} Cohtml baselines (${[...versions].join(', ')}); ` +
      `they move together, last, when a sweep closes.`
  );
}

// The gameface update sweep finds its volatile claims by grepping the marker, so a marker spelled
// any other way is a claim the next engine update will not re-check. Hence the strict reading:
// every occurrence of the word must be the token, and prose wanting the adjective rephrases.
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
// below it. Applied to every shipped file, the references included.
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

function checkBodyCeilings(files: readonly string[]): void {
  const bodies = files.filter(file => path.basename(file) == 'SKILL.md');

  for (const body of bodies) {
    const skill = path.basename(path.dirname(body));
    const ceiling = bodyCeilings[skill];

    // A new skill without a ceiling would escape the rule entirely, which is the one way to grow
    // the trap list unseen.
    assert.ok(ceiling != null, `${body} has no ceiling; add its skill to bodyCeilings.`);

    const lineCount = readShippedFile(body).trimEnd().split('\n').length;

    assert.ok(
      lineCount <= ceiling,
      `${body} runs ${lineCount} lines against a ceiling of ${ceiling}. A new trap past the ` +
        `ceiling displaces a weaker one; raising the ceiling is a commit of its own.`
    );
  }

  // A ceiling naming no body is dead policy, and it reads as protection that is not there.
  for (const skill of Object.keys(bodyCeilings)) {
    assert.ok(
      bodies.some(body => path.basename(path.dirname(body)) == skill),
      `bodyCeilings names "${skill}", which has no SKILL.md under ${skillsRoot}.`
    );
  }
}

function checkSymbolMarkers(files: readonly string[]): void {
  for (const file of files) {
    const content = readShippedFile(file);

    for (const match of content.matchAll(symbolMarkerCandidatePattern)) {
      assert.ok(
        symbolMarkerPattern.test(match[0]),
        `${file}:${lineOf(content, match.index)} writes the marker "${match[0]}"; the trap check ` +
          `reads only "[symbol: <Fully.Qualified.Type>]" or "[symbol: none]".`
      );
    }
  }
}

function checkNoLineCites(files: readonly string[]): void {
  for (const file of files) {
    const content = readShippedFile(file);
    const match = lineCitePattern.exec(content);

    assert.ok(
      match == null,
      `${file}:${lineOf(content, match?.index ?? 0)} cites "${match?.[0]}", a line number. ` +
        `Lines shift with every edit and game update: name the symbol instead.`
    );
  }
}

function listLineCiteScope(): readonly string[] {
  const pluginInstructions = readdirSync(path.join(repoRoot, 'plugins'))
    .map(plugin => `plugins/${plugin}/AGENTS.md`)
    .filter(file => existsSync(path.join(repoRoot, file)));

  const agentInstructions = ['AGENTS.md', ...pluginInstructions];

  return [
    ...lineCiteRoots.flatMap(root => listFilesRecursively(root)),
    ...agentInstructions
  ].filter(file => file.endsWith('.md') && !lineCiteExempt.test(file));
}

function isLocalPointer(target: string): boolean {
  return !/^[a-z][a-z0-9+.-]*:/iu.test(target) && !target.startsWith('#');
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
  const cached = shippedFileCache.get(relativePath);

  if (cached != null) {
    return cached;
  }

  const content = readFileSync(path.join(repoRoot, relativePath), 'utf8');

  shippedFileCache.set(relativePath, content);

  return content;
}

// Trailing whitespace only: two trailing spaces are Markdown's hard line break, and a baseline line
// carrying them is still the baseline. Leading whitespace stays, so the same words indented in a
// list item or a fence lower down never read as the baseline.
function readShippedLines(relativePath: string): readonly string[] {
  return readShippedFile(relativePath)
    .split(/\r?\n/u)
    .map(line => line.trimEnd());
}
