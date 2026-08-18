/**
 * Diagnosis of Cohtml's bare invalid-selector error, shared by every selector-taking tool.
 *
 * The engine rejects a large slice of CSS with `SyntaxError: Invalid CSS selector (<sel>) in
 * QuerySelector!` and names neither the offending construct nor a way out, so a caller with no
 * skill loaded tends to retry the same selector unchanged.
 * This module runs only on an error the engine already returned: it can never refuse a selector
 * the engine would have accepted, whatever the version in front of it.
 *
 * Detection is whitelist-complement rather than a list of known-bad constructs: the engine answers
 * a short set of pseudo-classes, so any token outside that set is the suspect, which names the
 * culprit even for constructs nobody probed.
 * That set was read off a live engine, so it records what was verified to work and not what the
 * parser accepts.
 * A construct outside it may still be answered, which is why the message names a suspect rather
 * than a verdict and always leaves the syntax-slip exit open.
 *
 * Everything here is pure.
 * This holds the server-side copy of the whitelist; the `gameface` and `gameface-driving` skills
 * teach the same set to agents, so a new engine version is verified against all three together.
 */

import { oneLine } from 'common-tags';

/**
 * The engine's own marker, matched loosely so surrounding stack-frame noise cannot hide it.
 */
const INVALID_SELECTOR_MARKER = 'Invalid CSS selector';

/**
 * What the engine writes in front of the selector it rejected, marker included, so a reworded
 * marker cannot leave the trigger and the parse disagreeing about what a rejection looks like.
 */
const SELECTOR_PREFIX = `${INVALID_SELECTOR_MARKER} (`;

/**
 * What the engine writes after it, naming the query API that threw.
 * Neither end of the selector is escaped, so a `) in <api>!` inside the selector reads exactly like
 * the engine's own: the count is what tells the two apart, never the position.
 */
const SELECTOR_TAIL = /\) in \w+!/gu;

/**
 * The pseudo-classes and pseudo-elements the JS query APIs (`querySelector*`, `closest`,
 * `matches`) are verified to answer.
 * The pseudo-elements match zero elements rather than throwing, which is why they belong here.
 * Verified live against Cohtml 1.64.0.7.
 * Stylesheet selector support is a different, wider fact family and must not be read off this set.
 */
export const SUPPORTED_PSEUDOS: ReadonlySet<string> = new Set([
  ':first-child',
  ':last-child',
  ':only-child',
  ':nth-child',
  ':root',
  ':hover',
  ':focus',
  ':active',
  ':before',
  ':after',
  '::before',
  '::after'
]);

/**
 * The same support, as the fallback message spells it out for a caller with no skill loaded.
 * Prose rather than a rendering of the set above: it covers the selector syntax that carries no
 * pseudo token at all, names `:nth-child()` with its parentheses, and folds each pseudo-element's
 * two spellings into one.
 * A test binds the two in both directions, so neither can advertise support the detector withholds
 * nor withhold support it grants.
 */
export const SUPPORTED_SUMMARY = oneLine`
  type, class, id and attribute selectors, combinators, \`:first-child\`, \`:last-child\`,
  \`:only-child\`, \`:nth-child()\`, \`:root\`, \`:hover\`, \`:focus\`, \`:active\`, \`::before\`
  and \`::after\`
`;

/**
 * What `:nth-child()` accepts: an integer, `even`/`odd`, or a bare `an` step.
 * An `an+b` offset throws exactly like an unsupported pseudo-class, so the token being whitelisted
 * is not enough to clear it.
 * The forms probed live are `2`, `even`, `odd`, `2n` and `n`, against `n+2` and `-n+3`; the rest of
 * the rule is read off those, so a re-probe starts by widening that list.
 */
const NTH_CHILD_ARGUMENT = /^\s*(?:[+-]?\d+|even|odd|[+-]?\d*n)\s*$/iu;

/**
 * Pseudo-class and pseudo-element tokens, with the argument list when one directly follows.
 * The scan resumes inside that argument rather than past it, so a construct nested in another one
 * (`:not(:first-of-type)`) surfaces alongside its host.
 */
const PSEUDO_TOKEN = /(?<colons>::?)(?<name>[a-z][a-z-]*)(?:\((?<argument>[^()]*)\))?/giu;

/**
 * The spans of a selector that carry no selector syntax: a backslash escape, a quoted string, an
 * attribute body.
 * A colon inside any of them belongs to a name or a value, never to a pseudo-class.
 * Ordered so an escaped quote is neutralised before the string scan pairs quotes off.
 * The attribute body's closing bracket is optional, so an unclosed `[` masks what follows rather
 * than being skipped: that text is inside an attribute as far as anyone can tell, and it is a colon
 * there that would otherwise read as a pseudo-class.
 * It stops at `)` all the same, which an unescaped attribute body cannot contain and which an
 * unclosed `[` inside a functional pseudo's argument would otherwise swallow, taking the rest of
 * the selector with it.
 * Both together keep a run of unclosed brackets linear, since the first consumes the rest instead
 * of each rescanning to end-of-string.
 */
const LITERAL_SPANS: readonly RegExp[] = [/\\./gu, /"[^"]*"|'[^']*'/gu, /\[[^\])]*\]?/gu];

/**
 * What a masked literal span is filled with.
 * It has to fall outside every class the other patterns here match on.
 * Outside a pseudo name, or a span sitting straight after an argument-less pseudo glues onto it and
 * `div:hover[data-x]` reads as the unknown construct `:hover...`; outside a digit, or a masked span
 * inside `:nth-child()` reads as a valid integer argument and clears a token that should have been
 * flagged.
 */
const MASK_FILLER = '#';

/**
 * The scan pattern with no condition named, which most hints reach for as written.
 */
const SCAN_PATTERN = scanPattern();

/**
 * The answer both selector-list shorthands get: the engine has no list to distribute over.
 */
const BRANCH_PER_QUERY = `run one query per branch of the list and merge the results.`;

/**
 * The answer both sides of the enabled/disabled pair get, one property carrying both.
 */
const DISABLED_HINT = `${scanPattern('`el.disabled`')}.`;

/**
 * One sentence per construct, keyed by the pseudo token with its arguments stripped.
 * A flagged token absent from here falls back to the generic line, which still names it.
 */
const HINTS: ReadonlyMap<string, string> = new Map<string, string>([
  [':not', `${SCAN_PATTERN}.`],
  [':has', `${scanPattern('what the element contains')}.`],
  [':is', BRANCH_PER_QUERY],
  [':where', BRANCH_PER_QUERY],
  [':first-of-type', ofTypeHint(':first-child')],
  [':last-of-type', ofTypeHint(':last-child')],
  [':only-of-type', ofTypeHint(':only-child')],
  [':nth-of-type', ofTypeHint(':nth-child()')],
  // Reached only when the argument fails NTH_CHILD_ARGUMENT, the token itself being supported.
  [
    ':nth-child',
    oneLine`
      the engine takes an integer, \`even\`, \`odd\`, or a bare \`an\` step here, and throws on an
      \`an+b\` offset.
    `
  ],
  [
    ':nth-last-child',
    `\`:nth-child()\` counts from the start only, so ${scanPattern('a position from the end')}.`
  ],
  [':empty', `${scanPattern('`el.children.length` and `el.textContent`')}.`],
  [':checked', `${scanPattern('`el.checked`')}.`],
  [':disabled', DISABLED_HINT],
  [':enabled', DISABLED_HINT]
]);

/**
 * What a flagged construct with no hint of its own gets: the naming is the value, and the two exits
 * (express it in JS, or drop it) cover both a predicate and a pseudo-element.
 */
const GENERIC_HINT = oneLine`
  express the condition in JS (${SCAN_PATTERN}) or drop it from the selector.
`;

/**
 * The sentence every diagnosis opens on, naming who rejected the selector.
 */
const DIAGNOSIS_LEAD = `Cohtml's JS query APIs rejected this selector.`;

/**
 * The fault a selector carries when no construct explains the rejection, shared by both branches so
 * the two cannot drift into giving different advice.
 */
const SYNTAX_SLIP = `a syntax slip, an unclosed bracket or quote`;

/**
 * The exit kept open whatever the scan named, since a flagged construct is a suspect rather than a
 * verdict and the engine reports one rejection however many faults the selector holds.
 */
const SYNTAX_SLIP_EXIT = `If that is not the fault, check the selector for ${SYNTAX_SLIP}.`;

/**
 * What a rejected selector made only of verified constructs gets: no culprit is invented, and the
 * verified set travels with the message so a caller with no skill loaded can check it.
 */
const GENERIC_DIAGNOSIS = oneLine`
  ${DIAGNOSIS_LEAD} It names no construct known to be unsupported, so check it for ${SYNTAX_SLIP}.
  Verified to work here: ${SUPPORTED_SUMMARY}; a pseudo-class outside that set may still throw.
`;

/**
 * Whether an error came from the engine rejecting a selector.
 * Such an error repeats identically on every retry, which is what lets a polling caller abort on it
 * instead of waiting out its budget.
 */
export function isInvalidSelectorError(engineError: string): boolean {
  return engineError.includes(INVALID_SELECTOR_MARKER);
}

/**
 * Enriches an engine error with the offending construct and a rewrite, keeping the engine's own
 * text verbatim in front of it.
 * Any error that is not an invalid-selector rejection comes back untouched, so a caller can route
 * every failure through this without checking first.
 * The selector is optional: absent one, it is read back out of the engine's own message.
 */
export function diagnoseSelectorError(engineError: string, selector?: string): string {
  if (!isInvalidSelectorError(engineError)) {
    return engineError;
  }

  const suspects = findSuspects(selector ?? selectorFromError(engineError));

  if (suspects.length == 0) {
    return `${engineError}\n\n${GENERIC_DIAGNOSIS}`;
  }

  const sentences = suspects.map(
    ({ token, hint }) => `\`${token}\` falls outside the set this engine answers: ${hint}`
  );

  // Concatenated rather than templated through `oneLine`, which would collapse the newlines of the
  // stack the engine text usually carries, and set off by a blank line, since the engine text ends
  // mid-stack and advice glued to the last frame reads as one more frame.
  return `${engineError}\n\n${DIAGNOSIS_LEAD} ${sentences.join(' ')} ${SYNTAX_SLIP_EXIT}`;
}

/**
 * The selector the engine named, which is what lets a caller holding no selector of its own -- a
 * predicate, an evaluated snippet -- still get a diagnosis.
 * Empty where the message carried no selector, and equally where it carried more than one tail: a
 * second one means either the selector or an appended stack frame holds text shaped like the
 * engine's own, and no rule picks the right end, so the caller gets the generic diagnosis rather
 * than a guess that can accuse a construct sitting inside an attribute value.
 */
function selectorFromError(engineError: string): string {
  const start = engineError.indexOf(SELECTOR_PREFIX);

  if (start == -1) {
    return '';
  }

  const afterPrefix = engineError.slice(start + SELECTOR_PREFIX.length);
  const tails = [...afterPrefix.matchAll(SELECTOR_TAIL)];

  return tails.length == 1 ? afterPrefix.slice(0, tails[0]?.index) : '';
}

/**
 * A construct the scan flagged, as the message quotes it back, with the rewrite it earns.
 */
interface Suspect {
  readonly token: string;
  readonly hint: string;
}

/**
 * Collects the constructs in a selector the engine is not verified to answer, in the order they
 * appear, one entry per distinct construct.
 */
function findSuspects(selector: string): Suspect[] {
  const suspects: Suspect[] = [];
  const seen = new Set<string>();

  // A fresh regex state per call: PSEUDO_TOKEN is global, so a shared lastIndex would make the
  // scan depend on the previous selector.
  const scanner = new RegExp(PSEUDO_TOKEN.source, PSEUDO_TOKEN.flags);
  const scannable = maskLiterals(selector);

  let match = scanner.exec(scannable);

  while (match != null) {
    const { colons = '', name = '', argument } = match.groups ?? {};
    const key = `${colons}${name}`.toLowerCase();

    if (!seen.has(key) && !isSupported(key, argument)) {
      seen.add(key);
      suspects.push({
        // Sliced from the selector rather than the masked copy, so the construct is quoted back
        // exactly as the caller wrote it, and with its arguments: `:nth-child(-n+3)` beats
        // `:nth-child`.
        token: selector.slice(match.index, match.index + match[0].length),
        hint: HINTS.get(key) ?? GENERIC_HINT
      });
    }

    // Resume just past the token name, leaving its arguments to be scanned in their own right.
    scanner.lastIndex = match.index + colons.length + name.length;

    match = scanner.exec(scannable);
  }

  return suspects;
}

/**
 * Blanks every literal span with a filler of its own length, so the scan neither reads a colon
 * inside one as a pseudo-class nor loses the offsets that map a token back to what the caller
 * wrote.
 */
function maskLiterals(selector: string): string {
  return LITERAL_SPANS.reduce(
    (masked, span) => masked.replace(span, literal => MASK_FILLER.repeat(literal.length)),
    selector
  );
}

/**
 * Whether the engine is verified to answer this token, arguments included.
 */
function isSupported(key: string, argument: string | undefined): boolean {
  if (!SUPPORTED_PSEUDOS.has(key)) {
    return false;
  }

  return key != ':nth-child' || argument == null || NTH_CHILD_ARGUMENT.test(argument);
}

/**
 * The recovery every construct expressing a predicate shares: the query API cannot carry the
 * condition, so JS evaluates it and leaves a selectable mark behind.
 */
function scanPattern(condition?: string): string {
  return oneLine`
    filter \`querySelectorAll\` results in \`game_eval\`${condition ? ` on ${condition}` : ''},
    tag the element you want with a data attribute, then query that attribute
  `;
}

/**
 * The one answer the whole of-type family gets, which is why they share a builder.
 */
function ofTypeHint(equivalent: string): string {
  return oneLine`
    use \`${equivalent}\` where the parent holds only that element type,
    otherwise ${SCAN_PATTERN}.
  `;
}
