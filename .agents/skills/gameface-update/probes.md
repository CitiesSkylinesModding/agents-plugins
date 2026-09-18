# The gameface probe catalogue

One entry per stable claim the coherent-gameface plugin rests on a probe: what to send, the tool it goes to, the answer expected, and the claim it settles.

**Expected answers are as of Cohtml 1.64.0.7, read off the plugin's claims at that version rather than off a run of this file**, so the first sweep also settles whether each snippet discriminates: one that cannot is rewritten with its entry.

Paths below are relative to `plugins/coherent-gameface/`.

## The version string

| Send | Tool | Expected | Settles |
| --- | --- | --- | --- |
| — | `game_status` | `Browser` carries `Cohtml/1.64.0.7` | the baseline lines; `skills/gameface/references/version-gating.md` "Detect the game's version" |
| `navigator.userAgent` | `game_eval` | `"Cohtml/1.64.0.7 (Windows; Native) cohtml/1.64.0.7 (Coherent Labs)"`, which `/cohtml\/([\d.]+)/i` parses to the version | the `navigator.userAgent` shape, same section |

## Selectors

Each goes to `game_eval` as `(() => { try { document.querySelector(SELECTOR); return 'ok'; } catch (error) { return String(error); } })()`.
`ok` is acceptance whether or not anything matched; a rejection's text contains `Invalid CSS selector`.
These settle the whitelist triple: `mcp/src/selectors.ts`, `skills/gameface-driving/SKILL.md` "Finding elements" and `skills/gameface/references/scripting-data-binding.md`.

| Construct | `SELECTOR` | Expected |
| --- | --- | --- |
| type, class, id | `'div.a#b'` | `ok` |
| attribute | `'[class]'` | `ok` |
| `[attr*=]` | `'[class*="a"]'` | `ok` |
| combinators | `'body > div + div ~ div div'` | `ok` |
| `:first-child` | `'div:first-child'` | `ok` |
| `:last-child` | `'div:last-child'` | `ok` |
| `:only-child` | `'div:only-child'` | `ok` |
| `:nth-child()` | `'div:nth-child(2)'` | `ok` |
| `:root` | `':root'` | `ok` |
| `:hover` | `'div:hover'` | `ok` |
| `:focus` | `'div:focus'` | `ok` |
| `:active` | `'div:active'` | `ok` |
| `::before` | `'div::before'` | `ok` |
| `::after` | `'div::after'` | `ok` |
| `:before` | `'div:before'` | `ok` |
| `:after` | `'div:after'` | `ok` |
| `:not()` | `'div:not(.a)'` | rejected |
| `:has()` | `'div:has(.a)'` | rejected |
| `:is()` | `'div:is(.a)'` | rejected |
| `:where()` | `'div:where(.a)'` | rejected |
| of-type family | `'div:first-of-type'`, `'div:last-of-type'`, `'div:only-of-type'`, `'div:nth-of-type(2)'` | each rejected |
| `:nth-last-child()` | `'div:nth-last-child(2)'` | rejected |
| `:empty` | `'div:empty'` | rejected |
| `:checked` | `'input:checked'` | rejected |
| `:disabled` | `'input:disabled'` | rejected |

### `:nth-child()` argument forms

Same wrapper, `SELECTOR` being `'li:nth-child(FORM)'`.

| `FORM` | Expected |
| --- | --- |
| `2` | `ok` |
| `even` | `ok` |
| `odd` | `ok` |
| `2n` | `ok` |
| `n` | `ok` |
| `n+2` | rejected |
| `-n+3` | rejected |
| `2n+1` | rejected — read off the `an+b` rule, never probed at 1.64.0.7 |

## DOM and JS

All to `game_eval`.

| Send | Expected | Settles |
| --- | --- | --- |
| `[typeof ResizeObserver, 'attributeStyleMap' in HTMLElement.prototype, 'append' in Element.prototype]` | `["function", true, true]` | the 1.47, 1.51 and 1.56 version probes: `version-gating.md` "Detect the game's version" |
| `[typeof btoa, typeof atob, typeof navigator.platform]` | `["undefined", "undefined", "undefined"]`; a `"function"` counts only once native as in the next row, and a `"string"` platform is the bundle's own assignment unless `'platform' in Navigator.prototype` | `scripting-data-binding.md` missing APIs |
| `[typeof sessionStorage, typeof URLSearchParams, typeof FormData, typeof File, typeof FileReader, typeof TextEncoder, typeof TextDecoder, typeof indexedDB, typeof document.cookie, typeof requestIdleCallback, typeof setImmediate]` | every one `"undefined"`; a `"function"` counts as present only once `String(fn).includes('[native code]')`, as the `postMessage` row below checks | `scripting-data-binding.md` "Also absent on the reference target"; `skills/gameface/SKILL.md` `sessionStorage` |
| `['click' in HTMLElement.prototype, typeof PointerEvent, typeof InputEvent]` | `[false, "undefined", "undefined"]`, a `"function"` counting only once native as above | `skills/gameface/SKILL.md` and `scripting-data-binding.md` "Simulating input from JS" |
| `'attachShadow' in Element.prototype` | `true` | Shadow DOM present (1.61+): `version-gating.md` probes, `tooling-workflow.md` Shadow DOM |
| `(() => { const host = document.createElement('div'); host.innerHTML = '<i></i> <i></i>\n<i></i>'; return Array.prototype.map.call(host.childNodes, node => node.nodeType).join(','); })()` | `"1,1,1"` — no whitespace text node; `"1,3,1,3,1"` is the 2.2 regime | the whitespace-node regime: `scripting-data-binding.md`, `tooling-workflow.md` Svelte, `version-gating.md` probes and breaking changes |
| `(() => { const host = document.createElement('div'); const live = host.getElementsByTagName('i'); host.appendChild(document.createElement('i')); return live.length; })()` | `1` | live `HTMLCollection` (1.52.1+): `scripting-data-binding.md` |
| `typeof window.postMessage + ' / ' + String(window.postMessage).includes('[native code]')` | `function / false` — present as the target bundle's polyfill, absent from the engine | `scripting-data-binding.md` missing APIs; `version-gating.md` polyfill caveat |
| `typeof setInterval` | `"function"` | `setInterval` present; its explicit-delay rule is the changelog's 2.0.0 entry, not a probe |
| `[typeof document.evaluate, typeof document.createTreeWalker, 'innerText' in HTMLElement.prototype, typeof document.title]` | `["undefined", "undefined", false, "undefined"]` | the four absent DOM APIs: `skills/gameface/SKILL.md`, `scripting-data-binding.md` "DOM and JS quirks", `skills/gameface-driving/SKILL.md` "Finding elements" |

## CSS

All to `game_eval`, as a style round-trip: `(() => { const el = document.createElement('div'); el.style.setProperty(PROP, VALUE); return el.style.getPropertyValue(PROP); })()`.
An empty string is the parser rejecting the declaration; a value read back proves parsing, and the changelog carries whether rendering implements it.

| `PROP`, `VALUE` | Expected | Settles |
| --- | --- | --- |
| `'row-gap'`, `'4px'`, then `'gap'`, `'4px'` | `""` for both; the longhand decides, and a shorthand reading back empty beside a longhand that parsed makes `skills/gameface/SKILL.md`'s `gap` round-trip example a false negative | the parser rejects flex `gap`, so the margin workaround stands: `layout-styling-text.md`, `version-gating.md` target section |
| `'aspect-ratio'`, `'16 / 9'` | `""` | `aspect-ratio` absent (2.2+): `layout-styling-text.md`, `version-gating.md` |
| `'position'`, `'fixed'` | `"fixed"` | the `position: fixed` declaration parses; that 1.56 reworked its support is the changelog's entry, not a probe: `version-gating.md` |
| `'text-decoration-style'`, each of `'solid'`, `'double'`, `'dotted'`, `'dashed'`, `'wavy'` | `"solid"` for `solid`, `""` for the rest | which `text-decoration-style` values the target accepts: `version-gating.md` 1.34.2 |
| `'transition-behavior'`, `'allow-discrete'` | `""` | discrete transitions absent (2.2+): `layout-styling-text.md` |

`@starting-style` is an at-rule, so it takes a sheet rather than a declaration: `(() => { const sheet = document.createElement('style'); sheet.textContent = '.gf-probe-control { opacity: 1; } @starting-style { .gf-probe { opacity: 0; } }'; document.head.appendChild(sheet); try { const rules = sheet.sheet?.cssRules; return rules ? Array.prototype.map.call(rules, rule => rule.cssText) : 'no cssRules'; } finally { sheet.remove(); } })()`.
Expected one entry, the control rule — the at-rule dropped, `@starting-style` absent (2.2+); a second entry opening on `@starting-style` is the at-rule kept, while an empty list or a bare `.gf-probe` rule settles nothing: `layout-styling-text.md`, `version-gating.md`.

The stylesheet selectors take the same snippet with the sheet's text replaced by `'.gf-probe-control { opacity: 1; } .gf-probe:not(.x) { opacity: 0; } .gf-probe::placeholder { opacity: 0; } .gf-probe:nth-of-type(2) { opacity: 0; }'`.
Expected the control rule alone — a stylesheet drops all three, where a kept rule names the construct that parses, and an empty list settles nothing: `skills/gameface/SKILL.md` stylesheet selectors.
