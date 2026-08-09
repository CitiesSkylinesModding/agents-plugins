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

// The baseline and the decompile warning are one header read by two rules, so they share this
// pattern and both read it off the same trimmed lines -- matching one rule untrimmed and the other
// trimmed fails a header that states its baseline correctly. `readShippedLines` owns why.
const baselinePattern = /^Verified against game version .+\.$/u;

// Fixed by the mechanics reference shape rather than chosen per file, since a mechanics reference
// cannot be checked at all without the tree and that is the whole of what its reader loses.
const mechanicsWarningLine = 'Without one you cannot check anything below.';

// Every rule reads the whole of each file it judges, so an uncached read costs one decode per rule
// per file on a path the pre-commit hook runs on every commit. The process is single-shot, so there
// is no staleness window to trade for it. Declared with the constants rather than beside its reader
// because the rules run at module top level, before a later `const` would initialize.
const shippedFileCache = new Map<string, string>();

const shippedFiles = listFilesRecursively(skillsRoot);

assert.ok(shippedFiles.length > 0, `No shipped files found under ${skillsRoot}.`);

checkNoModNameLeaks(shippedFiles);
checkVersionBaselines(shippedFiles);
checkDecompileWarnings(shippedFiles);
checkVolatilityMarkers(shippedFiles);
checkEvidenceMarkers(shippedFiles);
checkPointersResolve(shippedFiles);
checkDisclosedFilesAreReachable(shippedFiles);
checkMechanicsProseBudget(shippedFiles);
checkTechniqueProseBudget(shippedFiles);

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

    const baselines = readShippedLines(file).filter(line => baselinePattern.test(line));

    assert.equal(
      baselines.length,
      1,
      `${file} carries ${baselines.length} version baselines; a reference states exactly one ` +
        `line reading "Verified against game version <version>."`
    );
  }
}

// Placement is asserted by line, not by substring. A file carrying the words anywhere at all --
// inside a fence, below its whole body -- is a reference whose header lost its warning, which is
// the drift this rule exists to catch, and it reads as green to every weaker test.
//
// The opener and the closer are asserted everywhere. Between them sits the line saying where this
// file's own claims are checkable, which differs per file and which no pattern covers, so AGENTS.md
// states the cases and a reader's eye is the check -- except in the mechanics family, where the
// shape doc fixes that line to one sentence and the branch below holds it to that.
function checkDecompileWarnings(files: readonly string[]): void {
  // A reference opens on whichever source its own claims are checkable against, and closes on what
  // locates that source. Almost every one is the decompile. A file whose subject ships as data or
  // as an install artifact rather than as C# says so instead, and sends the reader somewhere the
  // setup skill does not provision -- so the two lines move together and a variant is one pair, not
  // two free lines. This list is the whole of what an author may write; a reference resting on a
  // source none of these names needs a pair added here before it can pass, which is deliberate:
  // the alternative is every author inventing a wording and the tree drifting a phrase at a time.
  const headerVariants = [
    {
      opener: `**Read this with the decompile open.**`,
      closer: '`cs2-modding-setup` provisions it.'
    },
    {
      opener: `**Read this with the game's string tables open.**`,
      closer: `They ship inside the install, which the toolchain's environment variables locate.`
    },
    {
      opener: `**Read this with the game install open.**`,
      closer: `The toolchain's environment variables locate it.`
    }
  ];

  // The header is the baseline, a blank line, then the block: opener, the file's own cost line, the
  // closer. Only the two fixed lines are asserted, so the offsets are what pins the middle one.
  const openerOffset = 2;
  const closerOffset = 4;

  for (const file of files) {
    if (!isTrunkReference(file)) {
      continue;
    }

    const lines = readShippedLines(file);
    const openers = lines.filter(line => headerVariants.some(each => each.opener == line));

    // The accepted pairs are printed rather than merely counted: an author who reached this message
    // wrote a wording of their own, and a message saying "one of the known variants" without naming
    // them leaves reading this script as the only route back.
    const acceptedPairs = headerVariants
      .map(each => `  ${each.opener}\n    ${each.closer}`)
      .join('\n');

    assert.equal(
      openers.length,
      1,
      `${file} carries ${openers.length} warning openers on a line of their own; every reference ` +
        `under the trunk skill opens its warning block exactly once, on one of:\n${acceptedPairs}`
    );

    const variant = headerVariants.find(each => each.opener == openers[0]);

    assert.ok(variant != null, `${file} opens its warning block on no known variant.`);

    // CheckVersionBaselines runs first and has already rejected a reference carrying no baseline,
    // and both rules read the same pattern off the same trimmed lines, so this is never -1.
    const baselineLine = lines.findIndex(line => baselinePattern.test(line));

    assert.equal(
      lines.indexOf(variant.opener),
      baselineLine + openerOffset,
      `${file} states its warning block somewhere other than one blank line under its version ` +
        `baseline, so a reader arriving by a link into this file may never meet it.`
    );

    // The closer is matched against the opener's own variant, so a file cannot tell a reader to
    // open one source and then route them to what locates a different one.
    assert.equal(
      lines.indexOf(variant.closer),
      baselineLine + closerOffset,
      `${file} closes its warning block with something other than "${variant.closer}" on the ` +
        `second line under the opener, leaving a reader no route to the source it names.`
    );

    // Last, so a file missing its closer is told that rather than told its middle line is wrong.
    const middleLine = lines[baselineLine + openerOffset + 1] ?? '';

    // The mechanics family is the one place this line is not a judgement: the shape doc fixes it to
    // one sentence, because it is true of every file in the family. Left unasserted it is the line
    // a first author copies from the nearest technique sibling, shipping "the technique holds
    // without one" into a file that cannot be checked without one -- the borrowed sentence the
    // contract names, arriving by the one route a green run would never show.
    if (isMechanicsReference(file)) {
      assert.equal(
        middleLine,
        mechanicsWarningLine,
        `${file} states "${middleLine}" between its warning block's opener and closer; every ` +
          `mechanics reference states "${mechanicsWarningLine}" there, which the mechanics ` +
          `reference shape fixes for the whole family.`
      );

      continue;
    }

    // Everywhere else, whether that line is right for this file is a reader's judgement; whether
    // there is one at all is not, and a blank there satisfies both offsets above.
    assert.ok(
      middleLine.length > 0,
      `${file} leaves the line between its decompile warning's opener and closer empty; that ` +
        `line is where the file states what a reader without the source loses.`
    );
  }
}

// Every reference under the trunk skill, rather than the two families it happens to hold today: a
// third family added below it would otherwise be exempt from the warning rule while the run stayed
// green. The other skills' references are correctly out of scope -- the rule is the trunk's.
function isTrunkReference(file: string): boolean {
  return isReference(file) && file.startsWith(`${skillsRoot}/cs2-modding/references/`);
}

function isMechanicsReference(file: string): boolean {
  return isReference(file) && file.includes('/references/mechanics/');
}

function isTechniqueReference(file: string): boolean {
  return isReference(file) && file.includes('/references/technique/');
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

    // Fenced content is dropped first, so a link shown inside a worked example does not pass as the
    // link a reader follows. An entry file illustrating the disclosure convention is the ordinary
    // way to write one, and it satisfies the grammar below while leaving the sibling unreachable.
    const entry = stripFencedBlocks(readShippedFile(entryFile));

    // Reported here rather than left to the budget rules, which see only their two families and
    // run after this one. An unclosed fence drops every line below it out of the text searched, so
    // without this the next assertion tells the author to write a link they already wrote and names
    // the real defect nowhere.
    assert.ok(
      !entry.hasUnclosedFence,
      `${entryFile} leaves a code fence unclosed, so everything below it reads as fenced and no ` +
        `link in it can be found. Close the fence.`
    );

    assert.ok(
      linkToPattern(path.basename(file)).test(entry.lines.join('\n')),
      `${file} is disclosed into a reference folder and ${entryFile} never links to it, so no ` +
        `reader arrives. Link it by bare filename, or fold it back into the entry file.`
    );
  }
}

// A mechanics reference orients rather than explains, and the failure it is written against is
// length: review catches a wrong sentence and never asks whether the file is too long.
function checkMechanicsProseBudget(files: readonly string[]): void {
  checkProseBudget(files, {
    warnAt: 60,
    failAt: 100,
    isInFamily: isMechanicsReference,
    failAdvice:
      `A mechanics reference maps and routes: disclose a section into a sibling rather than ` +
      `growing it.`,
    warnQuestion:
      `Is the topic this dense, or is a section explaining what the reader could read for ` +
      `themselves?`
  });
}

// The looser thresholds are ADR 0007's: technique prose is material stated nowhere else, so the
// mechanics diagnosis of length does not transfer.
function checkTechniqueProseBudget(files: readonly string[]): void {
  checkProseBudget(files, {
    warnAt: 300,
    failAt: 400,
    isInFamily: isTechniqueReference,
    failAdvice:
      `A technique file this long is carrying a self-contained account a reader consults rather ` +
      `than reads through: disclose it into a sibling.`,
    warnQuestion:
      `Is the topic this dense, or is a self-contained account still sitting inline where a ` +
      `sibling should hold it?`
  });
}

// Prose is the part that over-produces, so a budget counts prose alone -- a map table and a
// pseudo-code listing cost nothing against it and grow as far as the topic needs.
//
// Warn then fail, because density is a judgement the maintainer owns: a topic genuinely worth its
// budget says so in the check output, while one over the ceiling has stopped doing its family's
// job.
function checkProseBudget(files: readonly string[], budget: ProseBudget): void {
  const { warnAt, failAt, isInFamily, failAdvice, warnQuestion } = budget;
  const questions: string[] = [];

  let violation: string | undefined;

  for (const file of files) {
    if (!isInFamily(file)) {
      continue;
    }

    const prose = countProseLines(readShippedFile(file));

    if (prose == null) {
      violation ??=
        `${file} leaves a code fence unclosed, so every line below it escapes this budget ` +
        `entirely. Close the fence.`;

      continue;
    }

    const counted = `${file} carries ${prose} prose lines`;

    if (prose > failAt) {
      violation ??= `${counted} against a ceiling of ${failAt}. ${failAdvice}`;
    } else if (prose > warnAt) {
      questions.push(`${counted}, over the ${warnAt}-line budget. ${warnQuestion}`);
    }
  }

  // Printed before the assertion, so a run that fails on one file still hands back every question
  // it raised about the others.
  for (const question of questions) {
    console.warn(`WARNING: ${question}`);
  }

  assert.ok(violation == null, violation);
}

interface ProseBudget {
  readonly warnAt: number;
  readonly failAt: number;
  readonly isInFamily: (file: string) => boolean;
  readonly failAdvice: string;
  readonly warnQuestion: string;
}

// Not blank, not a heading, not a table row, not inside a fence: what is left is the prose a reader
// has to be told. Fenced content is dropped rather than matched line by line, since a listing's own
// lines would otherwise count against a budget the listing is exempt from -- which is also why an
// unclosed fence returns undefined rather than a count: it exempts the whole tail of the file, so
// the cheapest way to silence this budget is a typo the author never sees.
function countProseLines(content: string): number | undefined {
  const { lines, hasUnclosedFence } = stripFencedBlocks(content);

  if (hasUnclosedFence) {
    return undefined;
  }

  return lines.filter(line => {
    const trimmed = line.trim();

    return trimmed.length > 0 && !trimmed.startsWith('#') && !trimmed.startsWith('|');
  }).length;
}

// CommonMark closes a fence only on the character it was opened with, and only on a run at least as
// long, so a fence line of the other character or of a shorter run is ordinary content. Tracking
// which marker opened is what lets a listing hold a fenced example: a single boolean toggle closes
// the listing on its first inner fence, counts the remainder as prose, and where the strays are odd
// runs off the end of the file reporting an unclosed fence in a file whose fences all balance.
//
// Two readers need this. The prose budget drops fenced lines so a listing costs nothing against it,
// and the reachability rule drops them so a link written inside an example does not read as a link
// a reader can follow -- the case its own link grammar was written to defeat and cannot see.
function stripFencedBlocks(content: string): UnfencedLines {
  const lines: string[] = [];

  let openFence: string | undefined;

  for (const line of content.split('\n')) {
    // Three spaces of indent at most: four or more make an indented code block, whose content is
    // literal and opens no fence, so trimming first lets an indented example swallow the file.
    const match = /^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$/u.exec(line);
    const fence = match?.groups?.fence;

    if (fence != null) {
      if (openFence == null) {
        openFence = fence;
        continue;
      }

      // A closing fence carries no info string and repeats the opener's character at least as far,
      // so ```csharp inside an open block is content rather than the closer -- which is how a
      // nested example is written, and the shape doc teaches nested examples as this family's own
      // convention. A run is one repeated character, so startsWith is the same-marker test.
      const isCloser =
        (match?.groups?.info ?? '').trim().length == 0 &&
        fence.startsWith(openFence.charAt(0)) &&
        fence.length >= openFence.length;

      if (isCloser) {
        openFence = undefined;
        continue;
      }
    }

    if (openFence == null) {
      lines.push(line);
    }
  }

  return { lines, hasUnclosedFence: openFence != null };
}

interface UnfencedLines {
  readonly lines: readonly string[];
  readonly hasUnclosedFence: boolean;
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
  const cached = shippedFileCache.get(relativePath);

  if (cached != null) {
    return cached;
  }

  const content = readFileSync(path.join(repoRoot, relativePath), 'utf8');

  shippedFileCache.set(relativePath, content);

  return content;
}

// Trailing whitespace only. Two trailing spaces are Markdown's hard line break, so an author
// reaches for them in a header of several lines and an untrimmed comparison reports a line that is
// present and correctly placed as one that is missing. Leading whitespace stays, because it is what
// separates a header line from the same words indented in a list item or a fence lower down:
// discarding it lets a rule anchor on an example in the body and blame the header for it.
function readShippedLines(relativePath: string): readonly string[] {
  return readShippedFile(relativePath)
    .split(/\r?\n/u)
    .map(line => line.trimEnd());
}
