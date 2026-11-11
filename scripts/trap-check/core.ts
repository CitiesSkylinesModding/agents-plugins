import path from 'node:path';

// The trap check's pure core: every `[symbol: <type>]` marker in the scanned files, resolved to
// the decompile files that declare its type and judged against the files a game update changed.

export interface CheckInput {
  readonly scanned: readonly ScannedFile[];
  /**
   * The decompile's files at the target commit, as `src/<assembly>/<namespace>/<Type>.cs`.
   */
  readonly decompileFiles: readonly string[];
  readonly changedFiles: ReadonlySet<string>;
  /**
   * The decompile files declaring a type of this simple name, wherever they sit.
   */
  readonly findDeclarations: (type: string) => readonly string[];
}

export interface ScannedFile {
  readonly path: string;
  readonly text: string;
}

export type ClaimState = 'changed' | 'clean' | 'missing' | 'unchecked';

export interface Claim {
  readonly file: string;
  readonly line: number;
  readonly symbol: string;
  readonly state: ClaimState;
  readonly files: readonly string[];
}

const NO_SYMBOL = 'none';

// The segments of `src/<assembly>/<namespace>/<Type>.cs`; a root-namespace type has one fewer.
const NAMESPACED_DEPTH = 4;

const markerPattern = /\[symbol: (?<symbol>[^\]]+)\]/gu;

export function checkClaims(input: CheckInput): Claim[] {
  const layout = indexLayout(input.decompileFiles);

  return input.scanned.flatMap(scanned =>
    scanned.text.split('\n').flatMap((text, index) =>
      Array.from(text.matchAll(markerPattern), match => {
        const symbol = match.groups?.symbol ?? '';

        return { file: scanned.path, line: index + 1, symbol, ...judge(symbol) };
      })
    )
  );

  function judge(symbol: string): Pick<Claim, 'files' | 'state'> {
    if (symbol == NO_SYMBOL) {
      return { state: 'unchecked', files: [] };
    }

    const files = resolve(layout, symbol);
    const found = files.length ? files : findDeclared(input, symbol);

    if (!found.length) {
      return { state: 'missing', files: [] };
    }

    const isChanged = found.some(file => input.changedFiles.has(file));

    return { state: isChanged ? 'changed' : 'clean', files: found };
  }
}

// A nested type lives in its outermost type's file, so the longest namespace whose next segment
// names a file wins; a root-namespace type is the split with no namespace at all.
function resolve(
  layout: ReadonlyMap<string, readonly string[]>,
  symbol: string
): readonly string[] {
  const parts = symbol.split('.');

  for (let length = parts.length - 1; length >= 0; length--) {
    const files = layout.get(`${parts.slice(0, length).join('.')}/${parts[length]}`);

    if (files) {
      return files;
    }
  }

  return [];
}

// A file the layout misses, such as one holding a type beside the one it is named after, still
// sits in its namespace's folder.
function findDeclared(input: CheckInput, symbol: string): readonly string[] {
  const dot = symbol.lastIndexOf('.');
  const namespace = dot == -1 ? '' : symbol.slice(0, dot);

  return input
    .findDeclarations(symbol.slice(dot + 1))
    .filter(file => namespaceOf(file) == namespace);
}

// Keys each file by `<namespace>/<Type>`: a namespaced type sits in its namespace's folder under
// its assembly's, a root-namespace type directly under its assembly's.
function indexLayout(files: readonly string[]): Map<string, string[]> {
  return Map.groupBy(files, file => `${namespaceOf(file)}/${path.basename(file, '.cs')}`);
}

function namespaceOf(file: string): string {
  const segments = file.split('/');

  return segments.length == NAMESPACED_DEPTH ? (segments[2] ?? '') : '';
}
