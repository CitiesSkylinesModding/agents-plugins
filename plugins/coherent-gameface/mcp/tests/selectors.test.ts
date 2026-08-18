import { describe, expect, test } from 'bun:test';
import {
  SUPPORTED_PSEUDOS,
  SUPPORTED_SUMMARY,
  diagnoseSelectorError,
  isInvalidSelectorError
} from '../src/selectors';

/**
 * The engine's rejection, verbatim from Cohtml 1.64.0.7.
 */
function rejection(selector: string): string {
  return `SyntaxError: Invalid CSS selector (${selector}) in QuerySelector!`;
}

/**
 * Every construct the supported-set sentence names, backticked or not, with the bare `()` that
 * prose puts on `:nth-child()` taken back off.
 * Read off the sentence rather than the assembled message, so the prose around it stays free to
 * mention a construct as an example of one that throws.
 */
function claimedBySummary(): ReadonlySet<string> {
  // A backticked claim is taken whole, spaces included, so an argument written `( 2n )` is judged
  // as the caller would write it rather than truncated at the space.
  const quoted = [...SUPPORTED_SUMMARY.matchAll(/`(?<claim>[^`]*)`/gu)].map(
    match => match.groups?.claim ?? ''
  );
  // Anything colon-opening the prose left unbackticked counts too, so an entry added in the
  // surrounding plain text is checked rather than overlooked.
  const bare = [...SUPPORTED_SUMMARY.replaceAll(/`[^`]*`/gu, ' ').matchAll(/::?[^\s,]+/gu)].map(
    match => match[0]
  );

  return new Set(
    // Kept on containing a colon rather than opening on one, so an element-qualified claim like
    // `div:has(.x)` fails the shape check below instead of being dropped before it is ever judged.
    [...quoted, ...bare]
      .filter(claim => claim.includes(':'))
      .map(claim => claim.replace(/\(\)$/u, ''))
  );
}

/**
 * The diagnosis alone, with the engine's own text sliced back off.
 */
function diagnosisOf(selector: string): string {
  const engineError = rejection(selector);
  const enriched = diagnoseSelectorError(engineError, selector);

  expect(enriched).toStartWith(engineError);

  return enriched.slice(engineError.length).trim();
}

describe(`triggering`, () => {
  test(`fires on the engine's marker through surrounding frame noise`, () => {
    const noisy = `${rejection('div:not(.x)')}\n    at <anonymous>:1:10`;

    expect(diagnoseSelectorError(noisy, 'div:not(.x)')).toContain(
      '`:not(.x)` falls outside the set this engine answers'
    );
    expect(isInvalidSelectorError(noisy)).toBe(true);
  });

  test(`leaves an error that is not a selector rejection alone`, () => {
    const unrelated = `TypeError: Cannot read properties of null (reading 'click')`;

    expect(diagnoseSelectorError(unrelated, 'div:not(.x)')).toBe(unrelated);
    expect(isInvalidSelectorError(unrelated)).toBe(false);
  });

  test(`keeps the engine's text verbatim in front of the diagnosis`, () => {
    const engineError = rejection('div:has(.x)');

    expect(diagnoseSelectorError(engineError, 'div:has(.x)')).toStartWith(`${engineError}\n\n`);
  });

  test(`sets the diagnosis off from the stack the engine text carries`, () => {
    const stacked = `${rejection('div:not(.x)')}\n    at <anonymous>:1:10`;

    // A blank line, not a space: glued on, the advice reads as one more stack frame.
    expect(diagnoseSelectorError(stacked, 'div:not(.x)')).toStartWith(`${stacked}\n\n`);
  });
});

describe(`the selector the caller did not pass`, () => {
  test(`reads it back out of the engine's own message`, () => {
    const diagnosis = diagnoseSelectorError(rejection('.row:nth-last-child(2)'));

    expect(diagnosis).toContain('`:nth-last-child(2)` falls outside the set this engine answers');
  });

  test(`keeps a selector carrying its own parentheses whole`, () => {
    const diagnosis = diagnoseSelectorError(rejection('div:not(.a):has(.b)'));

    expect(diagnosis).toContain('`:not(.a)` falls outside the set this engine answers');
    expect(diagnosis).toContain('`:has(.b)` falls outside the set this engine answers');
  });

  test(`falls back to the generic diagnosis when the message names no selector`, () => {
    const diagnosis = diagnoseSelectorError('SyntaxError: Invalid CSS selector!');

    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`leaves a frame appended to the same line out of the selector`, () => {
    const framed = `${rejection('div:has(a)')} at Object.querySelectorAll (native:code)`;
    const diagnosis = diagnoseSelectorError(framed);

    expect(diagnosis).toContain('`:has(a)` falls outside');
    expect(diagnosis).not.toContain('`:code');
  });

  test(`names no construct when a frame carries a tail shaped like the engine's`, () => {
    const framed = `${rejection('div.a')} at Object.querySelectorAll (native:code) in Frame!`;
    const diagnosis = diagnoseSelectorError(framed);

    expect(diagnosis).not.toContain('falls outside');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`names no construct when the selector itself carries such a tail`, () => {
    const diagnosis = diagnoseSelectorError(rejection(`[data-x="(:not(a) in js!)"]`));

    expect(diagnosis).not.toContain('`:not');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`declines rather than taking the first of two tails`, () => {
    // The construct here is real and outside the masked spans, so taking either tail would name it.
    // Only refusing to choose produces the fallback, which is what pins the ambiguity rule.
    const diagnosis = diagnoseSelectorError(rejection(`div:has(x)[data-t="q) in Z!"]`));

    expect(diagnosis).not.toContain('`:has');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });
});

describe(`the suspect is never a verdict`, () => {
  test(`leaves the syntax-slip exit open whenever it names a construct`, () => {
    expect(diagnosisOf('a:link[href')).toContain('check the selector for a syntax slip');
  });
});

describe(`whitelist-complement detection`, () => {
  test(`names the offending construct with its arguments`, () => {
    expect(diagnosisOf('.panel > div:not(.hidden)')).toContain(
      '`:not(.hidden)` falls outside the set this engine answers'
    );
  });

  test(`names a construct nobody documented, with the generic hint`, () => {
    const diagnosis = diagnosisOf('div:scope');

    expect(diagnosis).toContain('`:scope` falls outside the set this engine answers');
    expect(diagnosis).toContain('express the condition in JS');
  });

  test(`names every distinct construct once, in the order they appear`, () => {
    const diagnosis = diagnosisOf('div:first-of-type:not(.a):not(.b):has(span)');

    expect(diagnosis.indexOf('`:first-of-type`')).toBeLessThan(diagnosis.indexOf('`:not(.a)`'));
    expect(diagnosis.indexOf('`:not(.a)`')).toBeLessThan(diagnosis.indexOf('`:has(span)`'));
    expect(diagnosis).not.toContain('`:not(.b)`');
  });

  test(`reaches a construct nested inside another one's arguments`, () => {
    const diagnosis = diagnosisOf('div:not(:first-of-type)');

    expect(diagnosis).toContain('`:not(:first-of-type)` falls outside the set this engine answers');
    expect(diagnosis).toContain('`:first-of-type` falls outside the set this engine answers');
  });

  test(`matches a construct whatever its case`, () => {
    expect(diagnosisOf('div:NOT(.x)')).toContain(
      '`:NOT(.x)` falls outside the set this engine answers'
    );
  });

  test(`clears a supported construct written in upper case`, () => {
    const diagnosis = diagnosisOf('LI:NTH-CHILD(2):first-of-type');

    expect(diagnosis).not.toContain('`:NTH-CHILD');
    expect(diagnosis).toContain('`:first-of-type` falls outside');
  });

  test(`reaches a flagged construct's own hint whatever its case`, () => {
    const diagnosis = diagnosisOf('div:FIRST-OF-TYPE');

    expect(diagnosis).toContain('`:first-child`');
    expect(diagnosis).not.toContain('express the condition in JS');
  });

  test(`clears every supported construct`, () => {
    const supported =
      ':root :hover :focus :active :first-child :last-child :only-child ::before ::after';

    expect(diagnosisOf(supported)).not.toContain('falls outside the set this engine answers');
  });

  test(`clears the :nth-child() argument forms the engine answers`, () => {
    const supported = 'li:nth-child(2), li:nth-child(odd), li:nth-child(even), li:nth-child(2n)';

    expect(diagnosisOf(supported)).not.toContain('falls outside the set this engine answers');
  });

  test(`flags an :nth-child() offset, which throws despite the token being supported`, () => {
    const diagnosis = diagnosisOf('li:nth-child(-n+3)');

    expect(diagnosis).toContain('`:nth-child(-n+3)` falls outside the set this engine answers');
    expect(diagnosis).toContain('an `an+b` offset');
  });
});

describe(`the spans that only look like constructs`, () => {
  test(`never names a construct that only appears inside an attribute value`, () => {
    const diagnosis = diagnosisOf(`div[data-label="a:not(b)"]`);

    expect(diagnosis).not.toContain('`:not');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`still names a real construct sitting next to such a value`, () => {
    const diagnosis = diagnosisOf(`div[data-label=':has(x)']:first-of-type`);

    expect(diagnosis).toContain('`:first-of-type` falls outside the set this engine answers');
    expect(diagnosis).not.toContain('`:has');
  });

  test(`reads an escaped colon as part of the name it sits in`, () => {
    const diagnosis = diagnosisOf(String.raw`.hover\:bg-red > div`);

    expect(diagnosis).not.toContain('`:bg-red');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`ignores a colon inside an attribute value that carries no quotes`, () => {
    const diagnosis = diagnosisOf('div[data-ns=foo:bar]');

    expect(diagnosis).not.toContain('`:bar');
  });

  test(`ignores one inside an attribute the selector never closed`, () => {
    const diagnosis = diagnosisOf('div[data-role=button :has(.icon)');

    expect(diagnosis).not.toContain('`:has');
    expect(diagnosis).toContain('names no construct known to be unsupported');
  });

  test(`ignores one running past a second unclosed attribute`, () => {
    expect(diagnosisOf('.list[data-ns=foo:bar .item[data-id]')).not.toContain('`:bar');
  });

  test(`still names a construct standing outside the brackets`, () => {
    expect(diagnosisOf('div[data-x]:not(.y)')).toContain('`:not(.y)` falls outside');
  });

  test(`lets an unclosed attribute end where the pseudo holding it ends`, () => {
    const diagnosis = diagnosisOf('div:not([data-active):has(.icon)');

    expect(diagnosis).toContain('`:not([data-active)` falls outside');
    expect(diagnosis).toContain('`:has(.icon)` falls outside');
  });

  test(`masks a pathological bracket run without backtracking into a stall`, () => {
    // The span patterns have been quadratic twice. A generous bound still catches that: the same
    // input took over 100 seconds under the pattern this guards against.
    const pathological = '['.repeat(200_000);
    const started = performance.now();

    diagnoseSelectorError(rejection(pathological), pathological);

    expect(performance.now() - started).toBeLessThan(2000);
  });

  test(`quotes the construct back exactly as it was written`, () => {
    const diagnosis = diagnosisOf('div:has([data-label="a b"])');

    expect(diagnosis).toContain('`:has([data-label="a b"])` falls outside');
  });

  test(`clears a supported construct an attribute selector follows straight on from`, () => {
    const followed = 'div:hover[data-active], li:first-child[data-id], input:focus[type="text"]';

    expect(diagnosisOf(followed)).not.toContain('falls outside');
  });

  test(`still reaches the hint for a flagged construct an attribute follows`, () => {
    expect(diagnosisOf('div:first-of-type[data-x]')).toContain('`:first-child`');
  });

  test(`reads a masked :nth-child() argument as the slip it is, not as an integer`, () => {
    const diagnosis = diagnosisOf(`li:nth-child('2n+1')`);

    expect(diagnosis).toContain('an `an+b` offset');
  });
});

describe(`the hint table`, () => {
  const scanPattern = 'tag the element you want with a data attribute';

  test(`points the predicate constructs at the game_eval scan pattern`, () => {
    for (const selector of ['div:not(.x)', 'div:has(.x)']) {
      expect(diagnosisOf(selector)).toContain(scanPattern);
    }
  });

  test(`points each of-type construct at its child-family equivalent`, () => {
    const equivalents: ReadonlyArray<readonly [string, string]> = [
      ['div:first-of-type', '`:first-child`'],
      ['div:last-of-type', '`:last-child`'],
      ['div:only-of-type', '`:only-child`'],
      ['div:nth-of-type(2)', '`:nth-child()`']
    ];

    for (const [selector, equivalent] of equivalents) {
      const diagnosis = diagnosisOf(selector);

      expect(diagnosis).toContain(equivalent);
      expect(diagnosis).toContain('where the parent holds only that element type');
    }
  });

  test(`points the selector-list shorthands at one query per branch`, () => {
    for (const selector of ['div:is(.a, .b)', 'div:where(.a, .b)']) {
      expect(diagnosisOf(selector)).toContain('run one query per branch of the list');
    }
  });

  test(`names the property each state construct is expressed through`, () => {
    const properties: ReadonlyArray<readonly [string, string]> = [
      ['input:checked', '`el.checked`'],
      ['input:disabled', '`el.disabled`'],
      ['input:enabled', '`el.disabled`'],
      ['div:empty', '`el.children.length`']
    ];

    for (const [selector, property] of properties) {
      const diagnosis = diagnosisOf(selector);

      expect(diagnosis).toContain(property);
      expect(diagnosis).toContain(scanPattern);
    }
  });

  test(`tells :nth-last-child() that counting only runs from the start`, () => {
    expect(diagnosisOf('li:nth-last-child(2)')).toContain('counts from the start only');
  });
});

describe(`the generic fallback`, () => {
  test(`invents no culprit and lists the supported set`, () => {
    const diagnosis = diagnosisOf('div[');

    expect(diagnosis).not.toContain('falls outside the set this engine answers');
    expect(diagnosis).toContain('check it for a syntax slip');
    expect(diagnosis).toContain('`:nth-child()`');
    expect(diagnosis).toContain('a pseudo-class outside that set may still throw');
  });

  test(`carries the supported summary into the message`, () => {
    // What binds the two checks below, which read the summary rather than the assembled message.
    expect(diagnosisOf('div[')).toContain(SUPPORTED_SUMMARY);
  });

  test(`names every construct the detector itself clears`, () => {
    for (const pseudo of SUPPORTED_PSEUDOS) {
      // A pseudo-element is written once, in its double-colon spelling, and stands for both keys.
      const named = claimedBySummary().has(pseudo) || claimedBySummary().has(`:${pseudo}`);

      expect(named).toBe(true);
    }
  });

  test(`claims no construct the detector would flag`, () => {
    const claimed = claimedBySummary();

    expect(claimed.size).toBeGreaterThan(0);

    for (const pseudo of claimed) {
      // Two checks, because neither alone is enough. The detector is deliberately lenient and reads
      // a malformed claim as the valid key it starts with, so shape is asserted first; and the
      // whitelist holds bare keys while the detector also rules on the argument, so a summary
      // naming `:nth-child(2n)` claims something true where `:nth-child(2n+1)` does not.
      // The name matches PSEUDO_TOKEN's own, since a claim it cannot tokenise is one the detector
      // is blind to; and only `:nth-child` may carry an argument, since it is the only key whose
      // argument the detector rules on.
      expect(pseudo).toMatch(/^(?:::?[a-z][a-z-]*|:nth-child\([a-z0-9\s+-]*\))$/iu);
      expect(diagnosisOf(`div${pseudo}`)).not.toContain('falls outside');
    }
  });
});
