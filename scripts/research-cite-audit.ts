/* oxlint-disable node/no-sync -- sequential audit script, synchronous IO is intentional. */
/* oxlint-disable no-console -- the console carries the report. */

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';

// Line-cite audit for the cs2-game-update sweep. A research cite is right when the line it names
// in the new decompile carries the text the same cite named in the old one, so every cite is
// checked by text identity rather than by arithmetic over the diff: that is what catches the cites
// a line map never moves (a cite inside a changed hunk, a continuation a parser missed, a range
// end left behind).
//
// Each cite in the working docs is paired with the same cite in the docs as committed at --since
// (line by line through git's own diff, then by position on the line), its --since number is read
// at --old and its working number at --new, and a pair whose texts differ is reported with the
// closest line in --new. A cite into a docs markdown file is checked through that file's own diff
// since --since instead. A game cite it cannot check is reported too (UNCHECKED, with the reason;
// GONE: a file missing at --new; REWRITTEN and COUNT: a line whose cites cannot be paired).
//
// The report is a worklist, not a proof. It parses only the cite forms docs/research/README.md
// prescribes, reading a short `<name>.md:<line>` as the sibling research file, so an unbackticked
// bare `:<line>` in a diagram or a comma-separated list goes unread; and a cite whose own line
// changed cosmetically (renamed locals, a re-minified bundle) is held on similarity, so a slip onto
// a near-identical neighbour passes.
//
// --apply rewrites every cite whose text has an exact, unique match and reports the rest. It also
// moves a range end that has no match of its own by its partner's offset where the text there is
// similar. It reports as MOVED every move it cannot anchor firmly (that range end, a short line or
// one whose neighbours changed, range ends that moved apart) and every bare `:<line>` it moves,
// since the parser gives a bare cite the file the prose cited last, not always the one meant.
//
// It has no tests of its own: check a change by replaying --apply in a throwaway worktree over the
// docs a past sweep started from, and comparing the result with that sweep's reviewed commits.

const repoRoot = path.resolve(import.meta.dirname, '..');

// Below this similarity two lines are different lines; decompiler churn stays well above it.
const SIMILARITY_THRESHOLD = 0.75;

// A normalized line shorter than this identifies nothing (`}`, `else`), so it is widened.
const MINIMUM_SIGNATURE_LENGTH = 6;

// Below this length an exact match is too generic to move a cite unreported.
const ANCHORED_SIGNATURE_LENGTH = 20;

// How many lines around a short line widen its signature, on each side.
const SIGNATURE_CONTEXT = 2;

// How far a markdown cite's target line is searched for after its file was edited.
const MARKDOWN_SEARCH_WINDOW = 400;

// How much of a line the report quotes.
const QUOTE_LENGTH = 70;

// The UI bundle copy is one blob far past execFileSync's default buffer.
// oxlint-disable-next-line no-magic-numbers
const GIT_OUTPUT_LIMIT = 1024 * 1024 * 1024;

const { values: options } = parseArgs({
  options: {
    decompile: { type: 'string' },
    old: { type: 'string' },
    new: { type: 'string', default: 'HEAD' },
    since: { type: 'string', default: 'HEAD' },
    apply: { type: 'boolean', default: false }
  }
});

assert.ok(
  options.decompile != null && options.old != null,
  `Usage: --decompile <root> --old <ref> [--new <ref>] [--since <ref>] [--apply]`
);

const decompileRoot = options.decompile;
const oldRef = options.old;
const newRef = options.new;
const sinceRef = options.since;

// Git failures read as empty output below, so a mistyped ref would otherwise report a clean run.
for (const [root, ref] of [
  [decompileRoot, oldRef],
  [decompileRoot, newRef],
  [repoRoot, sinceRef]
] as const) {
  assert.ok(
    git(root, 'rev-parse', '--verify', `${ref}^{commit}`)[0],
    `${ref} is not a commit in ${root}.`
  );
}

interface Cite {
  readonly file: string | undefined;
  readonly written: string | undefined;
  readonly numbers: readonly number[];
  readonly isExplicit: boolean;
  readonly start: number;
  readonly text: string;
}

interface Finding {
  readonly kind: 'COUNT' | 'GONE' | 'MD' | 'MOVED' | 'REWRITTEN' | 'TEXT' | 'UNCHECKED';
  readonly doc: string;
  readonly line: number;
  readonly cite: string;
  readonly detail: string;
}

// A cite is a path ending in a known extension followed by `:<spec>`, or a backticked bare
// `:<spec>` continuation. A spec is a line, a range, or a slash list of either. The continuation
// may follow a closing backtick and a slash (`A.cs:1`/`:2`), so the lookbehind must not reject
// `/`, or the second number of every such pair goes unchecked.
const specSource = String.raw`\d+(?:-\d+)?(?:/\d+(?:-\d+)?)*`;

const BACKTICK = '`';

const citePattern = new RegExp(
  `${String.raw`(?<path>[\w./-]*?[\w-]+\.(?:cs|js|css|md)):(?<spec>`}${specSource})` +
    `${String.raw`|(?<![\w.)\]])`}${BACKTICK}:(?<bare>${specSource})${BACKTICK}`,
  'gu'
);

// The file a short `<Type>.cs` name resolves to when the decompile holds several of that name.
const AMBIGUOUS = '(ambiguous)';

// The file such a name resolves to when none of the files sharing it changed, so no cite into any
// of them can have gone stale.
const UNCHANGED = '(unchanged)';

const hunkPattern =
  /^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<newStart>\d+)(?:,(?<newCount>\d+))? @@/u;

const namePattern = /(?<path>[\w./-]*?[\w-]+\.(?:cs|js|css|md))(?![\w:])/gu;

const docs = [
  ...['docs/research', 'docs/solutions', 'bench/questions'].flatMap(directory =>
    readdirSync(path.join(repoRoot, directory))
      .filter(name => name.endsWith('.md'))
      .map(name => `${directory}/${name}`)
  ),
  'docs/SOURCES.md',
  'docs/ROADMAP.md'
];

const decompileFilesByBase = Map.groupBy(
  git(decompileRoot, 'ls-tree', '-r', '--name-only', oldRef, 'src', 'src-ui'),
  file => path.basename(file)
);

assert.ok(
  decompileFilesByBase.size > 0,
  `${oldRef} holds no src or src-ui files in ${decompileRoot}.`
);

const newDecompileFiles = git(
  decompileRoot,
  'ls-tree',
  '-r',
  '--name-only',
  newRef,
  'src',
  'src-ui'
).filter(file => file != '');

const newDecompileBases = new Set(newDecompileFiles.map(file => path.basename(file)));

// --no-renames: rename detection lists only a renamed file's new path, hiding the deleted one.
const changedDecompileFiles = new Set(
  git(decompileRoot, 'diff', '--name-only', '--no-renames', oldRef, newRef, '--', 'src', 'src-ui')
);

const repoFiles = new Set(git(repoRoot, 'ls-files'));
const blobCache = new Map<string, string[]>();
const signatureCache = new Map<string, string[]>();

// Rebuilt per cite this would re-normalize a 135k-line bundle for every cite into it, so it is
// cached beside the signatures.
const blankCache = new Map<string, boolean[]>();
const findings: Finding[] = [];

// Read before the audit runs: with --apply the run rewrites these same files, and a notice taken
// afterwards would count its own edits and fire on every clean tree.
const audited = new Set(docs);
const dirtyAtStart = git(repoRoot, 'diff', '--name-only', sinceRef).filter(name =>
  audited.has(name)
);

for (const doc of docs) {
  auditDoc(doc);
}

for (const finding of findings) {
  console.log(
    `${finding.doc}:${finding.line}\t${finding.kind}\t${finding.cite}\t${finding.detail}`
  );
}

// A doc already edited since --since cannot be paired line for line everywhere, so some of its cites
// land in REWRITTEN or COUNT instead of being checked by text. The skill asks for a clean tree; this
// says so when it is not, rather than leaving the reader to infer it from the finding mix.

if (dirtyAtStart.length > 0) {
  console.log(
    `${dirtyAtStart.length} documents already differ from ${sinceRef}; a cite whose line the edit moved is reported as REWRITTEN or COUNT rather than checked by text. Run from a clean tree for full coverage.`
  );
}

console.log(`${findings.length} findings${options.apply ? ' left after --apply' : ''}.`);

function auditDoc(doc: string): void {
  const working = readFileSync(path.join(repoRoot, doc), 'utf8').split('\n');
  const toCommitted = lineMap(repoRoot, doc);
  const committedCites = parseCites(gitShow(repoRoot, sinceRef, doc), doc);
  let isRewritten = false;

  for (const [lineNumber, cites] of parseCites(working, doc)) {
    const committedLine = toCommitted(lineNumber);

    if (committedLine == null) {
      const detail = 'the paragraph changed length: open every cite on it';

      findings.push({ kind: 'REWRITTEN', doc, line: lineNumber, cite: joinCites(cites), detail });
      continue;
    }

    const before = committedCites.get(committedLine) ?? [];

    if (before.length != cites.length) {
      const detail = 'the line was rewritten: open every cite on it';

      findings.push({ kind: 'COUNT', doc, line: lineNumber, cite: joinCites(cites), detail });
      continue;
    }

    const replacements = cites.map((cite, index) =>
      auditCite(doc, lineNumber, before[index] ?? cite, cite)
    );

    const line = working[lineNumber - 1];

    if (!options.apply || line == null || replacements.every(text => text == null)) {
      continue;
    }

    working[lineNumber - 1] = cites.reduceRight(
      (rewritten, cite, index) =>
        rewritten.slice(0, cite.start) +
        (replacements[index] ?? cite.text) +
        rewritten.slice(cite.start + cite.text.length),
      line
    );

    isRewritten = true;
  }

  if (isRewritten) {
    writeFileSync(path.join(repoRoot, doc), working.join('\n'));
  }
}

// Records what the cite gets wrong, and returns its rewritten text where --apply may take it.
function auditCite(doc: string, line: number, before: Cite, cite: Cite): string | undefined {
  const unchecked = (detail: string): undefined => {
    findings.push({ kind: 'UNCHECKED', doc, line, cite: cite.text, detail });

    return undefined;
  };

  if (cite.file == UNCHANGED) {
    return undefined;
  }

  if (cite.file == AMBIGUOUS) {
    return unchecked('several changed decompile files share this name: name the file by path');
  }

  if (cite.file == null && !cite.isExplicit && cite.written == null) {
    return unchecked('no file cited before this bare cite in its section: name its file');
  }

  if (cite.file == null) {
    return isGameName(cite.written)
      ? unchecked('no such file at --old: a new file, or a path to fix')
      : undefined;
  }

  if (before.file != cite.file || specShape(before.text) != specShape(cite.text)) {
    return unchecked('the cite changed file or shape since --since: open it');
  }

  if (cite.file.endsWith('.md')) {
    auditMarkdownCite(doc, line, before, cite);

    return undefined;
  }

  const oldLines = gitShow(decompileRoot, oldRef, cite.file);
  const newLines = gitShow(decompileRoot, newRef, cite.file);

  if (oldLines.length > 1 && newLines.length <= 1) {
    const detail = `${cite.file} is gone at --new: find where its code went`;

    findings.push({ kind: 'GONE', doc, line, cite: cite.text, detail });
  }

  if (oldLines.length <= 1 || newLines.length <= 1) {
    return undefined;
  }

  if (before.numbers.some(number => number > oldLines.length)) {
    return unchecked(`its committed number runs past ${cite.file} at --old: open it`);
  }

  return placeCite(doc, line, before, cite);
}

// A written name that the decompile holds at --new, so a cite that resolved to nothing is still
// one of the game's and cannot be passed over.
function isGameName(written: string | undefined): boolean {
  if (written == null || written.endsWith('.md')) {
    return false;
  }

  // A path under `src/` is the game's however its directories are spelled; any other path with
  // directories is a mod's or another checkout's.
  if (written.includes('/')) {
    return /(?:^|\/)src(?:-ui)?\//u.test(written);
  }

  return newDecompileBases.has(written);
}

// Places each number of a cite into a decompile file both refs hold, recording what it gets wrong.
function placeCite(doc: string, line: number, before: Cite, cite: Cite): string | undefined {
  const file = cite.file ?? '';
  const oldLines = gitShow(decompileRoot, oldRef, file);
  const newLines = gitShow(decompileRoot, newRef, file);
  const oldSignatures = signatures(decompileRoot, oldRef, file);
  const newSignatures = signatures(decompileRoot, newRef, file);
  const newBlank = blankLines(newRef, file, newLines);

  const placements = before.numbers.map((was, index) =>
    place(oldSignatures, newSignatures, {
      was,
      now: cite.numbers[index] ?? was,
      wasWidened: isWidened(oldLines, was)
    })
  );

  const numbers = { was: before.numbers, now: cite.numbers };

  keepRangesTogether(cite.text, numbers, placements, {
    oldSignatures,
    newSignatures,
    newBlank
  });

  let rewritten = cite.text;

  // A number that already differs from --since was moved by hand, and placing the --since text over
  // it reverts that correction. One edited end disqualifies the whole cite: rewriting its partner
  // alone would leave a range spanning lines nobody chose.
  const wasEdited = before.numbers.some((was, index) => (cite.numbers[index] ?? was) != was);

  for (const [index, placement] of placements.entries()) {
    const was = before.numbers[index] ?? 0;
    const now = cite.numbers[index] ?? was;

    if (placement.state == 'held' || placement.state == 'similar') {
      continue;
    }

    if (options.apply && placement.state == 'moved' && wasEdited) {
      const detail = `${cite.file}:${now} -> ${placement.line} (edited since --since: left as written)`;

      findings.push({ kind: 'MOVED', doc, line, cite: cite.text, detail });

      continue;
    }

    if (options.apply && placement.state == 'moved') {
      rewritten = replaceNumber(rewritten, index, placement.line);

      // A bare cite moved by its own file's text is only as right as the file the parser gave it,
      // and an inferred move only as right as the similarity that placed it.
      if (!cite.isExplicit || placement.isInferred == true) {
        const reason = cite.isExplicit
          ? 'weakly anchored: confirm the line'
          : 'bare: confirm its file';
        const detail = `${cite.file}:${now} -> ${placement.line} (${reason})`;

        findings.push({ kind: 'MOVED', doc, line, cite: cite.text, detail });
      }

      continue;
    }

    const suggestion =
      placement.state == 'moved'
        ? placement.line
        : closest(newSignatures, oldSignatures[was - 1] ?? '', now);

    const bare = cite.isExplicit ? '' : ' (bare: confirm its file first)';
    const quoted = `${quote(oldLines, was)} || ${quote(newLines, now)}`;

    findings.push({
      kind: 'TEXT',
      doc,
      line,
      cite: cite.text,
      detail: `${cite.file}:${now} -> ${suggestion ?? '?'}${bare}\t${quoted}`
    });
  }

  return rewritten == cite.text ? undefined : rewritten;
}

// A move is inferred when a range end took its partner's offset on similarity rather than on an
// exact match of its own, and is reported even when applied.
type Placement =
  | { readonly state: 'held' | 'lost' | 'similar' }
  | { readonly state: 'moved'; readonly line: number; readonly isInferred?: boolean };

interface SignaturePair {
  readonly oldSignatures: readonly string[];
  readonly newSignatures: readonly string[];
  readonly newBlank: readonly boolean[];
}

interface CiteNumbers {
  readonly was: readonly number[];
  readonly now: readonly number[];
}

interface Range {
  readonly start: number;
  readonly end: number;
}

// Where the text a cite named at --old sits at --new. An exact, unique match anywhere wins over
// a merely similar line at the cited number, since a `}` or a one-token change can look alike
// across unrelated lines; similarity only holds a cite whose own line changed cosmetically.
function place(
  oldSignatures: readonly string[],
  newSignatures: readonly string[],
  at: { was: number; now: number; wasWidened: boolean }
): Placement {
  const { was, now, wasWidened } = at;

  const wanted = oldSignatures[was - 1];

  if (wanted == null || newSignatures[now - 1] == wanted) {
    return { state: 'held' };
  }

  const exact = newSignatures.flatMap((candidate, index) =>
    candidate == wanted ? [index + 1] : []
  );

  const [line] = exact;

  if (exact.length == 1 && line != null) {
    // A short line or one whose neighbours both changed can be a renamed identifier landing on
    // another construct (the UI bundle is re-minified every update), so the move is reported.
    const isAnchored =
      !wasWidened &&
      wanted.length >= ANCHORED_SIGNATURE_LENGTH &&
      (isSimilar(oldSignatures[was - 2] ?? '', newSignatures[line - 2]) ||
        isSimilar(oldSignatures[was] ?? '', newSignatures[line]));

    return { state: 'moved', line, isInferred: !isAnchored };
  }

  // Text found more than once moved to one of several copies; similarity cannot say which.
  if (exact.length > 1) {
    return { state: 'lost' };
  }

  return isSimilar(wanted, newSignatures[now - 1]) ? { state: 'similar' } : { state: 'lost' };
}

// A range whose one end moved while the other found no exact match takes the moved end's offset
// where its text is similar there, and is reported where it is not: a range whose ends disagree
// is worse than a stale one, so either end lost reports both. A range left inverted is reported.
function keepRangesTogether(
  text: string,
  numbers: CiteNumbers,
  placements: Placement[],
  fileSignatures: SignaturePair
): void {
  let index = 0;

  for (const part of text.slice(text.lastIndexOf(':') + 1).split('/')) {
    if (part.includes('-')) {
      settleRange({ start: index, end: index + 1 }, numbers, placements, fileSignatures);
    }

    index += part.includes('-') ? 2 : 1;
  }
}

function settleRange(
  { start, end }: Range,
  numbers: CiteNumbers,
  placements: Placement[],
  { oldSignatures, newSignatures, newBlank }: SignaturePair
): void {
  const slots = [start, end].map(slot => ({
    slot,
    was: numbers.was[slot] ?? 0,
    now: numbers.now[slot] ?? 0
  }));

  const offset = slots
    .map(({ slot, was }) => movedOffset(placements[slot], was))
    .find(value => value != null);

  if (offset == null) {
    return;
  }

  // Ends that moved by different offsets either lost or gained lines inside the range, or one of
  // them matched the wrong copy, so both are reported.
  const offsets = slots.map(({ slot, was }) => movedOffset(placements[slot], was));

  // Both ends moved, by different offsets: neither can settle the other, so both are reported. The
  // inversion check below still runs, since two ends that moved apart are what can cross.
  if (offsets.every(value => value != null) && offsets[0] != offsets[1]) {
    for (const { slot } of slots) {
      const placement = placements[slot];

      if (placement?.state == 'moved') {
        placements[slot] = { ...placement, isInferred: true };
      }
    }
  } else {
    for (const { slot, was } of slots) {
      followPartner(
        placements,
        { slot, was, offset },
        {
          oldSignatures,
          newSignatures,
          newBlank
        }
      );
    }
  }

  // An end that stays keeps its working number, which is what the moved end lands against.
  const [firstLine, lastLine] = slots.map(
    ({ slot, now }) => movedOffset(placements[slot], 0) ?? now
  );

  const isSplit = slots.some(({ slot }) => placements[slot]?.state == 'lost');

  if (isSplit || (firstLine != null && lastLine != null && firstLine > lastLine)) {
    placements[start] = { state: 'lost' };
    placements[end] = { state: 'lost' };
  }
}

// One end follows the offset its partner established, where the line it lands on identifies itself.
function followPartner(
  placements: Placement[],
  { slot, was, offset }: { slot: number; was: number; offset: number },
  { oldSignatures, newSignatures, newBlank }: SignaturePair
): void {
  const state = placements[slot]?.state;
  const wanted = oldSignatures[was - 1] ?? '';
  const atOffset = newSignatures[was + offset - 1];

  // A held end whose text sits exactly at the partner's offset as well is a repeated line (a
  // brace, a `return`), and the partner's offset settles which copy the range meant.
  if (state == 'held' && atOffset == wanted && offset != 0) {
    placements[slot] = { state: 'moved', line: was + offset };
  } else if (state == 'similar' || state == 'lost') {
    placements[slot] = landsAt(wanted, atOffset, newBlank[was + offset - 1])
      ? { state: 'moved', line: was + offset, isInferred: true }
      : { state: 'lost' };
  }
}

// The offset a moved end travelled; with `was` at zero, the line it landed on.
function movedOffset(placement: Placement | undefined, was: number): number | undefined {
  return placement?.state == 'moved' ? placement.line - was : undefined;
}

function blankLines(ref: string, file: string, lines: readonly string[]): boolean[] {
  const key = `${ref}:${file}`;
  const cached = blankCache.get(key) ?? lines.map((_, index) => isBlank(lines, index + 1));

  blankCache.set(key, cached);

  return cached;
}

function signatures(root: string, ref: string, file: string): string[] {
  const key = `${root}:${ref}:${file}`;
  const lines = gitShow(root, ref, file);
  const cached =
    signatureCache.get(key) ?? lines.map((_, index) => signature(lines, index + 1) ?? '');

  signatureCache.set(key, cached);

  return cached;
}

function auditMarkdownCite(doc: string, line: number, before: Cite, cite: Cite): void {
  const file = cite.file ?? '';
  const toCommitted = lineMap(repoRoot, file);
  const { length } = gitShow(repoRoot, sinceRef, file);

  for (const [index, was] of before.numbers.entries()) {
    const now = cite.numbers[index] ?? was;

    // A number past the file's end belongs to some other file the prose meant.
    if (was > length) {
      const detail = `runs past ${file}: name the file it means`;

      findings.push({ kind: 'UNCHECKED', doc, line, cite: cite.text, detail });
      continue;
    }

    if (toCommitted(now) == was) {
      continue;
    }

    if (toCommitted(now) == null) {
      const detail = `${file}:${now} sits in a passage that changed length: open it`;

      findings.push({ kind: 'UNCHECKED', doc, line, cite: cite.text, detail });
      continue;
    }

    const low = Math.max(1, was - MARKDOWN_SEARCH_WINDOW);
    const candidates = Array.from(
      { length: 2 * MARKDOWN_SEARCH_WINDOW },
      (_, offset) => low + offset
    );
    const moved = candidates.find(candidate => toCommitted(candidate) == was);

    if (moved == null) {
      const detail = `${file}:${now} no longer maps to a line of the committed file: open it`;

      findings.push({ kind: 'UNCHECKED', doc, line, cite: cite.text, detail });
    } else if (moved != now) {
      findings.push({
        kind: 'MD',
        doc,
        line,
        cite: cite.text,
        detail: `${file}:${now} -> ${moved}`
      });
    }
  }
}

function parseCites(lines: readonly string[], doc: string): Map<number, Cite[]> {
  const cites = new Map<number, Cite[]>();
  let isInFence = false;
  let current: string | undefined;
  let currentName: string | undefined;

  for (const [index, line] of lines.entries()) {
    // A bare cite may lean on a path cited paragraphs earlier, but never across a heading.
    if (line.startsWith('```')) {
      isInFence = !isInFence;
    }

    if (line.startsWith('#') && !isInFence) {
      current = undefined;
      currentName = undefined;
    }

    const events = [
      ...[...line.matchAll(namePattern)].map(match => ({ match, isName: true })),
      ...[...line.matchAll(citePattern)].map(match => ({ match, isName: false }))
    ].toSorted((a, b) => a.match.index - b.match.index || Number(a.isName) - Number(b.isName));

    for (const { match, isName } of events) {
      const written = match.groups?.path;

      // A markdown file merely named is a pointer to read, not a cite target, so it leaves the
      // file a following bare `:<line>` belongs to alone.
      if (written != null && !(isName && written.endsWith('.md'))) {
        current = resolve(written, doc);
        currentName = written;
      }

      if (isName) {
        continue;
      }

      const spec = match.groups?.spec ?? match.groups?.bare ?? '';

      const cite: Cite = {
        file: current,
        written: currentName,
        numbers: [...spec.matchAll(/\d+/gu)].map(number => Number(number[0])),
        isExplicit: written != null,
        start: match.index,
        text: match[0]
      };

      cites.set(index + 1, [...(cites.get(index + 1) ?? []), cite]);
    }
  }

  return cites;
}

function resolve(written: string, doc: string): string | undefined {
  if (written.endsWith('.md')) {
    if (repoFiles.has(written)) {
      return written;
    }

    const near = path.normalize(path.join(path.dirname(doc), written));

    if (repoFiles.has(near)) {
      return near;
    }

    const hits = [...repoFiles].filter(
      file => file.startsWith('docs/') && file.endsWith(`/${written}`)
    );

    return hits.length == 1 ? hits[0] : undefined;
  }

  const file = ['DecompiledCitiesSkylines2/', 'cs2-decompile/'].reduce(
    (stripped, prefix) =>
      stripped.includes(prefix)
        ? stripped.slice(stripped.indexOf(prefix) + prefix.length)
        : stripped,
    written
  );

  const candidates = decompileFilesByBase.get(path.basename(file)) ?? [];

  if (file.startsWith('src/') || file.startsWith('src-ui/')) {
    return candidates.includes(file) ? file : undefined;
  }

  const matching = file.includes('/')
    ? candidates.filter(candidate => candidate.endsWith(`/${file}`))
    : candidates;

  // Several files of one name matter only where one of them moved; otherwise any is still right.
  if (matching.length > 1) {
    return matching.some(candidate => changedDecompileFiles.has(candidate)) ? AMBIGUOUS : UNCHANGED;
  }

  return matching[0];
}

// Maps a working line of a repo file to its line at --since, from `git diff -U0` hunk headers:
// lines outside a hunk shift by the hunks above them, and a hunk replacing as many lines as it
// removes pairs them in order, since that is what an in-place rewrite of a paragraph looks like.
function lineMap(root: string, file: string): (line: number) => number | undefined {
  const hunks = git(root, 'diff', '-U0', sinceRef, '--', file).flatMap(header => {
    const groups = hunkPattern.exec(header)?.groups;

    return groups == null
      ? []
      : [
          {
            oldStart: Number(groups.oldStart),
            oldCount: Number(groups.oldCount ?? 1),
            newStart: Number(groups.newStart),
            newCount: Number(groups.newCount ?? 1)
          }
        ];
  });

  return line => {
    let shift = 0;

    for (const hunk of hunks) {
      if (hunk.newCount > 0 && line >= hunk.newStart && line < hunk.newStart + hunk.newCount) {
        return hunk.oldCount == hunk.newCount ? hunk.oldStart + (line - hunk.newStart) : undefined;
      }

      // An empty side's start is the line before the change, not the first changed line.
      const newAfter = hunk.newStart + Math.max(hunk.newCount, 1);
      const oldAfter = hunk.oldStart + Math.max(hunk.oldCount, 1);

      if (line >= newAfter) {
        shift = oldAfter - newAfter;
      }
    }

    return line + shift;
  };
}

// A line's comparable text: whitespace and the decompiler's cosmetic churn (`this.`, `out var`,
// short-circuit operators) removed. A line too short to identify anything is widened to its
// neighbours, so a `}` is matched by the block it closes.
function signature(lines: readonly string[], line: number): string | undefined {
  const text = lines[line - 1];

  if (line < 1 || text == null) {
    return undefined;
  }

  if (normalize(text).length >= MINIMUM_SIGNATURE_LENGTH) {
    return normalize(text);
  }

  return lines
    .slice(Math.max(0, line - 1 - SIGNATURE_CONTEXT), line + SIGNATURE_CONTEXT)
    .map(neighbour => normalize(neighbour))
    .join('|');
}

// Whether a line's signature had to be widened with its neighbours. Kept beside the signatures
// rather than inside them: a marker in the text would be scored as content by similarity().
function isWidened(lines: readonly string[], line: number): boolean {
  return normalize(lines[line - 1] ?? '').length < MINIMUM_SIGNATURE_LENGTH;
}

// A line with no text of its own identifies nothing, so a range end must never follow its partner's
// offset onto one. A merely short line (`},`) still identifies the construct it closes and is fine.
function isBlank(lines: readonly string[], line: number): boolean {
  return normalize(lines[line - 1] ?? '') == '';
}

function normalize(text: string): string {
  return text
    .replaceAll(/\bthis\./gu, '')
    .replaceAll('&&', '&')
    .replaceAll('||', '|')
    .replaceAll(/\bout var\b/gu, 'out')
    .replaceAll(/\s+/gu, '');
}

function closest(
  signatureList: readonly string[],
  wanted: string,
  near: number
): number | undefined {
  const wantedBigrams = bigrams(wanted);

  const best = signatureList.reduce<{ line: number; score: number }>(
    (winner, candidate, index) => {
      const score = similarity(wanted, candidate, wantedBigrams);
      const line = index + 1;
      const isNearer = Math.abs(line - near) < Math.abs(winner.line - near);

      return score > winner.score || (score == winner.score && isNearer) ? { line, score } : winner;
    },
    { line: 0, score: 0 }
  );

  return best.score >= SIMILARITY_THRESHOLD ? best.line : undefined;
}

function isSimilar(wanted: string, candidate: string | undefined): boolean {
  return candidate != null && similarity(wanted, candidate) >= SIMILARITY_THRESHOLD;
}

// Sørensen-Dice over character bigrams: cheap enough to score every line of a 40,000-line file per
// stale cite, and tolerant of the renamed locals a re-decompile leaves behind.
function similarity(
  left: string,
  right: string,
  leftBigrams?: ReadonlyMap<string, number>
): number {
  if (left == right) {
    return 1;
  }

  if (left.length < 2 || right.length < 2) {
    return 0;
  }

  // Scoring one line against every line of a file re-counts the same left bigrams each time, so a
  // caller scoring in a loop passes them in and this counts only what the candidate consumes.
  const counts = leftBigrams ?? bigrams(left);
  const used = new Map<string, number>();
  let shared = 0;

  for (let i = 0; i < right.length - 1; i++) {
    const pair = right.slice(i, i + 2);
    const spent = used.get(pair) ?? 0;

    if ((counts.get(pair) ?? 0) > spent) {
      used.set(pair, spent + 1);
      shared++;
    }
  }

  return (2 * shared) / (left.length + right.length - 2);
}

function bigrams(text: string): Map<string, number> {
  const counts = new Map<string, number>();

  for (let i = 0; i < text.length - 1; i++) {
    const pair = text.slice(i, i + 2);

    counts.set(pair, (counts.get(pair) ?? 0) + 1);
  }

  return counts;
}

// A range end only follows its partner's offset onto a line that identifies itself. A short line's
// signature is its neighbourhood, which every member of that neighbourhood scores similar to, so
// without this the end lands on the blank line or brace beside the construct.
function landsAt(
  wanted: string,
  atOffset: string | undefined,
  blank: boolean | undefined
): boolean {
  return isSimilar(wanted, atOffset) && blank != true;
}

// A spec's shape is its separators: `:12-15` and `:12/15` both hold two numbers and mean different
// things, so pairing them would write one cite's numbers into the other's punctuation.
function specShape(text: string): string {
  const spec = /^[\d/-]+/u.exec(text.slice(text.lastIndexOf(':') + 1))?.[0] ?? '';

  return spec.replaceAll(/\d+/gu, '#');
}

// Replaces the index-th number of the cite's spec, which is everything after its last colon, so a
// digit inside the path is never counted.
function replaceNumber(text: string, index: number, value: number): string {
  const colon = text.lastIndexOf(':');
  let seen = -1;

  const rewritten = text.slice(colon + 1).replaceAll(/\d+/gu, number => {
    seen++;

    return seen == index ? String(value) : number;
  });

  return text.slice(0, colon + 1) + rewritten;
}

function joinCites(cites: readonly Cite[]): string {
  return cites.map(cite => cite.text).join(' ');
}

function quote(lines: readonly string[], line: number): string {
  return (lines[line - 1] ?? '').trim().slice(0, QUOTE_LENGTH);
}

function gitShow(root: string, ref: string, file: string): string[] {
  const key = `${root}:${ref}:${file}`;
  const cached = blobCache.get(key) ?? git(root, 'show', `${ref}:${file}`);

  blobCache.set(key, cached);

  return cached;
}

// An unreadable blob (a file the ref lacks) reads as empty, which every caller treats as nothing
// to check.
function git(root: string, ...args: string[]): string[] {
  try {
    return execFileSync('git', ['-C', root, ...args], {
      encoding: 'utf8',
      maxBuffer: GIT_OUTPUT_LIMIT
    }).split('\n');
  } catch {
    return [];
  }
}
