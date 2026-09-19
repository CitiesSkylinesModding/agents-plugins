# The gameface probe catalogue

One entry per stable claim the coherent-gameface plugin rests on a probe: what to send, the tool it goes to, the answer expected, and the claim it settles.

**Expected answers are as of Cohtml 2.2.1.3, read off a run of every entry at that version.**

A matching answer settles the claim only where a different answer was reachable: before reading one as confirmation, say what the entry would have returned on an engine without the feature, and rewrite any snippet whose two regimes answer alike. A snippet that reports a shape rather than the property in question — node types where identity is the claim, a presence check where behaviour is — passes on both sides and settles neither.

Paths below are relative to `plugins/coherent-gameface/`.

## The version string

| Send | Tool | Expected | Settles |
| --- | --- | --- | --- |
| — | `game_status` | `Browser` carries `Cohtml/2.2.1.3` | the baseline lines; `skills/gameface/references/version-gating.md` "Detect the game's version" |
| `navigator.userAgent` | `game_eval` | `"Cohtml/2.2.1.3 (Windows; Native) cohtml/2.2.1.3 (Coherent Labs)"`, which `/cohtml\/([\d.]+)/i` parses to the version | the `navigator.userAgent` shape, same section |

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
| `:host` | `(() => { const plain = document.createElement('div'); const host = document.createElement('div'); host.attachShadow({ mode: 'open' }); return [plain.matches(':host'), host.matches(':host')]; })()` | `[false, true]` — `:host` answers and discriminates, matching a shadow host and not a plain element, which is what puts it in the whitelist. It answers off the element rather than off the shadow root's scope, so `root.querySelectorAll(':host')` is `0`; `[false, false]` or `[true, true]` would mean it stopped discriminating |
| `::slotted()` | `(() => { const host = document.createElement('div'); host.innerHTML = '<i></i><i></i>'; document.body.appendChild(host); try { return [document.querySelectorAll('::slotted(i)').length, document.querySelectorAll('i').length]; } finally { host.remove(); } })()` | the two counts EQUAL — `::slotted(x)` is answered but degenerates to bare `x`, matching unslotted elements, which is why it is out of the whitelist and in `ANSWERED_NOT_REJECTED`. Unequal counts would mean the engine learned to slot-filter |
| `::part()` | `(() => { const host = document.createElement('div'); document.body.appendChild(host); try { const root = host.attachShadow({ mode: 'open' }); root.innerHTML = '<p part="x"></p>'; return [root.querySelectorAll('[part="x"]').length, root.querySelectorAll('::part(x)').length]; } finally { host.remove(); } })()` | `[1, 0]` — answered, but matches nothing against a real `part="x"` node, which is why it is out of the whitelist and in `ANSWERED_NOT_REJECTED`. `[1, 1]` would mean it started working |
| `::selection` | `[document.querySelectorAll('div::selection').length, document.querySelectorAll('div').length]` | the two counts EQUAL — answered but degenerating to the compound it trails, the third member of `ANSWERED_NOT_REJECTED`. It takes no argument, so `div::selection` and `div::selection(x)` are both answered, unlike `::slotted`/`::part`, which are rejected bare and empty |
| `:not()` | `'div:not(.a)'` | rejected |
| `:has()` | `'div:has(.a)'` | rejected |
| `:is()` | `'div:is(.a)'` | rejected |
| `:where()` | `'div:where(.a)'` | rejected |
| of-type family | `'div:first-of-type'`, `'div:last-of-type'`, `'div:only-of-type'`, `'div:nth-of-type(2)'` | each rejected |
| `:nth-last-child()` | `'div:nth-last-child(2)'` | rejected |
| `:empty` | `'div:empty'` | rejected |
| `:checked` | `'input:checked'` | rejected |
| `:disabled` | `'input:disabled'` | rejected |
| `:focus-within` | `'div:focus-within'` | rejected |
| `:focus-visible` | `'div:focus-visible'` | rejected |
| `:target` | `'div:target'` | rejected |
| `:lang()` | `'div:lang(en)'` | rejected |
| `:link` | `'a:link'` | rejected |
| `:visited` | `'a:visited'` | rejected |
| `::placeholder` | `'div::placeholder'` | rejected |

### `:nth-child()` argument forms

Same wrapper, `SELECTOR` being `'li:nth-child(FORM)'`.

| `FORM` | Expected |
| --- | --- |
| `2`, `+3`, `-1`, `0` | each `ok` |
| `even`, `odd`, `EVEN` | each `ok` |
| `2n`, `n`, `-n`, `+2n`, `N` | each `ok` |
| `' 2 '` | `ok` |
| `n+2`, `-n+3`, `2n+1`, `0n+2`, `2n-1` | each rejected |

### Selector lists

`(() => { const host = document.createElement('div'); host.style.display = 'none'; host.innerHTML = '<i class="a x"></i><b class="b"></b><u class="a"></u>'; document.body.appendChild(host); try { return { order: Array.prototype.map.call(host.querySelectorAll('.b, .a'), el => el.tagName).join(','), dupes: Array.prototype.map.call(host.querySelectorAll('.a, .x'), el => el.tagName).join(','), first: host.querySelector('.b, .a').tagName }; } finally { host.remove(); } })()` to `game_eval`.
Expected `{ order: "B,I,U", dupes: "I,U,I", first: "B" }` — a list parses and the engine walks it branch by branch.
Document order (`I,B,U`) with no repeat would be the standard behavior instead: `scripting-data-binding.md` "DOM and JS quirks", and the selector-list hints in `mcp/src/selectors.ts`.

## DOM and JS

All to `game_eval`.

| Send | Expected | Settles |
| --- | --- | --- |
| `[typeof ResizeObserver, 'attributeStyleMap' in HTMLElement.prototype, 'append' in Element.prototype]` | `["function", true, true]` | the 1.47, 1.51 and 1.56 version probes: `version-gating.md` "Detect the game's version" |
| `[typeof btoa, typeof atob, typeof navigator.platform]` | `["undefined", "undefined", "undefined"]`; a `"function"` counts only once native as in the `postMessage` row, and a `"string"` platform is the bundle's own assignment unless `'platform' in Navigator.prototype` | `scripting-data-binding.md` missing APIs |
| `[typeof sessionStorage, typeof URLSearchParams, typeof FormData, typeof File, typeof FileReader, typeof TextEncoder, typeof TextDecoder, typeof indexedDB, typeof document.cookie, typeof requestIdleCallback, typeof setImmediate]` | every one `"undefined"`; a `"function"` counts as present only once `String(fn).includes('[native code]')`, as the `postMessage` row below checks | `scripting-data-binding.md` "Also absent on the reference target"; `skills/gameface/SKILL.md` `sessionStorage` |
| `['click' in HTMLElement.prototype, typeof PointerEvent, typeof InputEvent]` | `[false, "undefined", "undefined"]`, a `"function"` counting only once native as above | `skills/gameface/SKILL.md` and `scripting-data-binding.md` "Simulating input from JS" |
| `[typeof [].findLast, typeof structuredClone, typeof Object.groupBy, typeof Array.fromAsync, typeof ''.replaceAll]` | `["undefined", "undefined", "undefined", "undefined", "function"]` — post-9.4 absent, 9.4 present | the V8 ceiling: `version-gating.md` V8 section, `skills/gameface/SKILL.md` |
| `'attachShadow' in Element.prototype` | `true` | Shadow DOM present (1.61+): `version-gating.md` probes, `tooling-workflow.md` Shadow DOM |
| `(() => { const host = document.createElement('div'); try { return host.attachShadow({ mode: 'open', clonable: true }).clonable; } catch (error) { return String(error); } })()` | `true` — the option is honoured. What a pre-2.2 engine answers is unprobed, so the plugin's `2.2+` gate rests on its own claim rather than on this row | `scripting-data-binding.md` binding over shadow-DOM subtrees |
| `(() => { const host = document.createElement('div'); host.innerHTML = '<i></i> <i></i>\n<i></i>'; const types = Array.prototype.map.call(host.childNodes, node => node.nodeType).join(','); const gaps = [host.childNodes[1], host.childNodes[3]]; const made = document.createTextNode(' '); return { types, shared: gaps[0] === gaps[1], values: gaps.map(gap => gap && gap.nodeValue), madeIsShared: made === gaps[0] }; })()` | `types` `"1,3,1,3,1"` — whitespace occupies `childNodes` (2.2+), against `"1,1,1"` for the pre-2.2 regime — with `shared` `true`, `values` `[" ", " "]` and `madeIsShared` `false`: parsed gaps are one shared node whose `nodeValue` ignores the source whitespace, while `createTextNode` still makes real ones. `shared` `false` would be the real-node regime. Do not assert a gap's `length` or `data`: the shared node keeps whatever any earlier write left in it, engine-wide | the whitespace-node regime AND the shared-node hazard: `scripting-data-binding.md`, `tooling-workflow.md` Svelte, `version-gating.md` probes and breaking changes |
| `(() => { const host = document.createElement('div'); host.style.display = 'none'; document.body.appendChild(host); try { const live = host.getElementsByTagName('i'); host.appendChild(document.createElement('i')); return live.length; } finally { host.remove(); } })()` | `1` — live `HTMLCollection` (1.52.1+). The same snippet on a DETACHED host answers `0`, which is why this one attaches: liveness stops at the document edge | `scripting-data-binding.md` live collections |
| `typeof window.postMessage + ' / ' + String(window.postMessage).includes('[native code]')` | `function / false` — present as the target bundle's polyfill, absent from the engine | `scripting-data-binding.md` missing APIs; `version-gating.md` polyfill caveat |
| `typeof setInterval` | `"function"` | `setInterval` present; its explicit-delay rule is the changelog's 2.0.0 entry, not a probe |
| `[typeof document.evaluate, typeof document.createTreeWalker, 'innerText' in HTMLElement.prototype, typeof document.title]` | `["undefined", "undefined", false, "undefined"]` | the four absent DOM APIs: `skills/gameface/SKILL.md`, `scripting-data-binding.md` "DOM and JS quirks", `skills/gameface-driving/SKILL.md` "Finding elements" |

## CSS

All to `game_eval`, as a style round-trip: `(() => { const el = document.createElement('div'); el.style.setProperty(PROP, VALUE); return el.style.getPropertyValue(PROP); })()`.
An empty string is the parser rejecting the declaration; a value read back proves parsing ALONE. Whether rendering implements it is a separate question, answered by a layout probe below where one covers the property and by the feature changelog otherwise — `transition-behavior: allow-discrete` and `@starting-style` are the two the plugin currently states behavior for on the changelog's word, with no layout probe of their own.

| `PROP`, `VALUE` | Expected | Settles |
| --- | --- | --- |
| `'gap'`, `'4px'`, then `'row-gap'`, `'4px'` | `"4px 4px"` and `"4px"` — the parser takes flex `gap` (2.2+); both empty is an engine without it, where the margin workaround stands | `layout-styling-text.md`, `version-gating.md` target section |
| `'aspect-ratio'`, `'16 / 9'` | `"16 / 9"` — present (2.2+) | `layout-styling-text.md`, `version-gating.md` |
| `'transition-behavior'`, `'allow-discrete'` | `"allow-discrete"` — discrete transitions present (2.2+) | `layout-styling-text.md` |
| `'position'`, `'fixed'` | `"fixed"` | the `position: fixed` declaration parses; that 1.56 reworked its support is the changelog's entry, not a probe: `version-gating.md` |
| `'display'`, `'grid'` | `""` — the example of a declaration no version ever parsed | the round-trip example in `skills/gameface/SKILL.md` |
| `'box-sizing'`, `'content-box'` | `""` — absent below 3.0 | the 3.0 version probe: `version-gating.md` "Detect the game's version", worked example |
| `'justify-content'`, `'space-evenly'` | `""` — absent below 3.1.1 | the 3.1.1 version probe: same two sections, `layout-styling-text.md` |
| `'text-decoration-style'`, each of `'solid'`, `'double'`, `'dotted'`, `'dashed'`, `'wavy'` | `"solid"` for `solid`, `""` for the rest | which `text-decoration-style` values the target accepts: `version-gating.md` 1.34.2 |

### Layout, a frame after the DOM is built

Each builds its case inside `position: fixed; left: -10000px; opacity: 0` so the live UI is untouched, waits two `requestAnimationFrame`s, measures, and removes the host.
Send them to `game_eval` with `awaitPromise`.

| Probe | Expected | Settles |
| --- | --- | --- |
| `(() => new Promise(resolve => { const host = document.createElement('div'); host.style.cssText = 'position: fixed; left: -10000px; opacity: 0; display: flex; flex-direction: row; gap: 20px'; host.innerHTML = '<i style="width: 50px; height: 10px"></i><i style="width: 50px; height: 10px"></i>'; document.body.appendChild(host); requestAnimationFrame(() => requestAnimationFrame(() => { const kids = host.children; const delta = kids[1].getBoundingClientRect().left - kids[0].getBoundingClientRect().left; host.remove(); resolve(delta); })); }))()` | `70` — `gap` lays out, not just parses (2.2+); `50` is a parsed-but-ignored `gap` | flex `gap` in `layout-styling-text.md` |
| `(() => new Promise(resolve => { const host = document.createElement('div'); host.style.cssText = 'position: fixed; left: -10000px; opacity: 0'; host.innerHTML = '<i style="display: block; width: 100px; aspect-ratio: 2 / 1"></i>'; document.body.appendChild(host); requestAnimationFrame(() => requestAnimationFrame(() => { const r = host.children[0].getBoundingClientRect(); host.remove(); resolve([r.width, r.height]); })); }))()` | `[100, 50]` — `aspect-ratio` lays out (2.2+); a height of `0` is a parsed-but-ignored declaration | `aspect-ratio` in `layout-styling-text.md` |
| `(() => new Promise(resolve => { const host = document.createElement('div'); host.style.cssText = 'position: fixed; left: -10000px; opacity: 0; width: 400px'; host.innerHTML = '<i style="display: block; transition: width 300ms linear; width: 0px; overflow: hidden">measured text here</i>'.repeat(2); document.body.appendChild(host); const boxes = host.children; const samples = [[], []]; requestAnimationFrame(() => requestAnimationFrame(() => { boxes[0].style.width = 'auto'; boxes[1].style.width = '180px'; let n = 0; const tick = () => { samples[0].push(Math.round(boxes[0].getBoundingClientRect().width)); samples[1].push(Math.round(boxes[1].getBoundingClientRect().width)); if (++n < 14) { requestAnimationFrame(tick); } else { host.remove(); resolve({ auto: samples[0], explicit: samples[1] }); } }; requestAnimationFrame(tick); })); }))()` | `auto` jumping straight to its final width (`[0, 400, 400, ...]`) while `explicit` ramps evenly (`[0, 40, 80, 120, 160, 180, 180, ...]`) — the control is what makes the jump mean anything, since a box that failed to animate at all would also give one jump. Both ramping would mean the workaround is retired | the "Animating `width`/`height` from 0 to `auto` fails" workaround in `layout-styling-text.md` |

### Stylesheets

A `<style>` element exposes no `sheet` property here, so a sheet is found through `document.styleSheets` by `ownerNode` and judged by rule COUNT — every `cssRules` index reads back `undefined` and there is no `item()`, making the count all a script learns.
One sheet per construct, each holding a control rule plus the construct's rule, in one call: the sheet is in `document.styleSheets` as soon as `appendChild` returns, so no frame wait is needed, and one call is what keeps `el` in scope — each `game_eval` is its own scope, and a DOM node cannot travel back through a return value.

`(() => { const el = document.createElement('style'); el.textContent = '.gf-probe-control { opacity: 1; } ' + CSS; document.head.appendChild(el); try { return Array.prototype.find.call(document.styleSheets, sheet => sheet.ownerNode === el).cssRules.length; } finally { el.remove(); } })()`

| `CSS` | Expected | Settles |
| --- | --- | --- |
| `'.gf-p:not(.x) { opacity: 0; }'` | `1` — dropped | the stylesheet drops: `skills/gameface/SKILL.md` |
| `'.gf-p::placeholder { opacity: 0; }'` | `1` — dropped | same |
| `'.gf-p:nth-of-type(2) { opacity: 0; }'` | `1` — dropped | same |
| `'.gf-p:first-child { opacity: 0; }'` | `2` — kept, the control proving a count of 2 is reachable | same |
| `'@starting-style { .gf-p { opacity: 0; } }'` | `2` — the at-rule is kept (2.2+); `1` is the pre-2.2 engine dropping it | `@starting-style` in `layout-styling-text.md`, `version-gating.md` |

## CDP input

The one entry no `game_*` tool can run: it needs a raw WebSocket to `ws://localhost:9444/devtools/page/0`, since the point is what the CDP `Input` domain does when nothing wraps it.

Install a full-viewport overlay through `Runtime.evaluate` on that socket, recording every `mousedown`/`mouseup`/`click`/`keydown`/`input` it sees, then send `Input.dispatchMouseEvent` (`mouseMoved`, `mousePressed`, `mouseReleased`), `Input.dispatchKeyEvent` (`keyDown`, `keyUp`) and `Input.insertText` at the overlay's centre, and read the record back.

Expected: every command answers with an empty result and no error, and the record stays EMPTY. Two controls make that mean something — a `Runtime.evaluate` over the same socket must succeed (the session is live), and a DOM-dispatched sequence on the overlay must land in the record (the listeners work). Without both, an empty record is just a broken probe.

Settles the CDP-input finding, which is stated on six carriers: `mcp/src/tools.ts` (`clickFn`'s docblock and `gameClick`'s), `mcp/src/server.ts` (the `game_click` tool description), `mcp/README.md`, `plugins/coherent-gameface/README.md`, `WHY.md` and `docs/ROADMAP.md` "The CDP surface across engine versions". `Input.dispatchTouchEvent` is on Coherent Labs' own input path and has never been probed here; adding it to the send list is what closes the open half of that entry.
