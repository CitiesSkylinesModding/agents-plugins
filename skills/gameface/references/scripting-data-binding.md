# Scripting a Gameface UI: the engine API, data binding, and JS quirks

Doc paths below are relative to `https://docs.coherent-labs.com/cpp-gameface/`.
Primary sources: `integration/ui_scripting/javascript_native/` (engine API) and `integration/ui_scripting/htmldatabinding/` (binding attributes).

## Bootstrap

The global `engine` object is the only JS-to-game bridge.
It exists once the page includes `cohtml.js` (shipped with the SDK; games bundle it).
Gate startup on the promise `engine.whenReady.then(...)`: it resolves whether or not the `Ready` event already fired, which a late `engine.on('Ready', ...)` subscription would miss.

## Events versus calls

- **Events** fan out: N handlers on both sides, no return value.
  `engine.trigger(name, ...args)` fires toward C++ handlers AND same-name JS handlers.
  `engine.on(name, callback, context?)` subscribes; `engine.off(name, sameCallback, context?)` unsubscribes (pass the same named function).
- **Calls** are request/response: exactly one C++ handler per name.
  `engine.call(name, ...args)` returns a Promise of the handler's return value; it rejects on handler errors.
- Reserved names: `Ready` (binding layer up), `*` (wildcard, receives every C++-to-JS event with the event name as first argument, at a per-event cost), `_Unhandled` (fires when a triggered event has no callback).
  `_Result`, `_OnReady`, and `_OnError` are internal; never trigger them.

## Marshaling across the boundary

- Numbers, strings, booleans, arrays, plain objects, and BigInt (up to 64 bits) cross freely.
- Objects arriving from C++ are by-value snapshots; holding a reference does not track later C++ changes.
- A JS object passed where a bound C++ type is expected must carry `__Type: 'TypeName'`.
- C++ can expose objects by reference (live reads, methods calling straight into C++).
  Access after the game destroys such an object throws.
  Safe data binding (1.61+) re-resolves sub-object access through the model path, so cached `arr[n]` tracks the slot, not the original object.
- Failed property type conversions fail silently: the UI simply does not update.

## Models and synchronization

A model is a named object mirrored between game code and the DOM.
Lifecycle, from either side: create, mutate, mark dirty, synchronize.

- JS side: `engine.createJSModel(name, obj)`, mutate, `engine.updateWholeModel(model)` (marks dirty), `engine.synchronizeModels()` (applies every dirty model to the DOM).
  Both of the last two are required; forgetting `synchronizeModels` is the classic "nothing updates" bug.
- `engine.createObservableModel(name)` auto-marks on top-level property assignment only; nested writes (`obs.a.b = x`) go unnoticed.
- `engine.addSynchronizationDependency(src, dep)` chains updates from one model to another.
- `engine.createOrMergeModel(name, obj)` adds missing properties to a game-provided model (recursively; cannot grow engine-bound arrays).
- Page reload or navigation silently unregisters ALL models, events, and call handlers; the game re-registers on its ready callback, JS-created ones are simply gone.
  Re-registering a model does NOT rebind DOM nodes that were bound before unregistration.
- Cohtml caches a model's shape by its type name, with no polymorphism (undocumented): two payloads sharing a type name must expose the same set of PRESENT properties.
  Presence is what matters, not value: a property set to null keeps the shape, while an omitted property changes it and breaks the cached binding.
  When payloads vary, encode each optional sub-object's presence into the type name.

## The data-bind-* attribute vocabulary

Expressions: attribute values are expressions where model references sit in `{{ }}`, for example `{{Player.health}} / {{Player.maxHealth}}`.
Expressions limited to arithmetic, comparisons, `toFixed()`, and `Math.floor/round/ceil/abs` evaluate natively in C++; anything else compiles to a JS stub with a boundary crossing per evaluation (slow in `data-bind-for` rows).
Array indexing inside `{{ }}` takes numeric literals only.
Evaluation order across attributes is unspecified; keep expressions side-effect free.

| Attribute                                                                         | Effect                                                                                                                        |
| --------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `data-bind-value`                                                                 | Sets `textContent`.                                                                                                           |
| `data-bind-html`                                                                  | Sets `innerHTML` (keep small; nested `data-bind-*` inside is unsupported).                                                    |
| `data-bind-if`                                                                    | Conditional presence of the element (boolean expression).                                                                     |
| `data-bind-for`                                                                   | `"iter:{{m.arr}}"` or `"index, iter:{{m.arr}}"`; repeats the element per item.                                                |
| `data-bind-class`                                                                 | `"expr[;expr]"`, each resolving to a class name to add.                                                                       |
| `data-bind-class-toggle`                                                          | `"cls:boolExpr[;cls:boolExpr]"`; toggles classes per condition.                                                               |
| `data-bind-style-left/top/width/height`                                           | Number means px.                                                                                                              |
| `data-bind-style-opacity`, `-color`, `-background-color`, `-background-image-url` | Style shorthands; colors accept CSS strings or unsigned ABGR numbers.                                                         |
| `data-bind-style-transform2d`, `-transform-rotate`                                | 6-number matrix; number means degrees.                                                                                        |
| `data-bind-style-PROPERTYNAME`                                                    | Generalized form for every supported CSS property (dynamic binder).                                                           |
| `data-bind-<domEvent>`                                                            | Handler expression for DOM events (`data-bind-click`, `data-bind-mouseover`, ...); receives `event` and `this` (the element). |

Custom attributes: `engine.registerBindingAttribute(name, class { init?; update?; deinit? })` creates `data-bind-<name>`.
`init`/`deinit` fire on DOM attach/detach (including via if/for).

Structural pitfalls:

- JS edits inside a `data-bind-if` subtree are lost when the condition flips; JS edits to `data-bind-for` clones are undefined behavior once the collection changes size.
- Elements whose `data-bind-if`/`data-bind-for` was added from JS after creation join the update cycle only when attached to the DOM, and such elements can become unselectable from JS afterwards (documented known issue).
  For heavily dynamic structure, drive the DOM with a framework and bind leaf values only.
- `data-bind-for` over collections of primitives is unsupported; wrap items in objects.
- Identical `{{expressions}}` evaluate once per synchronization (deduplicated).
- Binding over shadow-DOM-containing subtrees needs `attachShadow({clonable: true})` (2.2+).

## Developing without the game (mock data)

`content_development/mockgamedata/`: create the models the game would provide with `engine.createJSModel`, then `engine.mockEvent(name, handler)` / `engine.mockCall(name, handler)` register mock handlers that fire only when no real game handler exists, so the same bundle runs in the Player and in the game.
Availability depends on the game's bundled cohtml.js (CS2's lacks both); probe before relying on them.
Mocked call returns representing bound C++ types need `__Type`.
The DevTools Data Binding Models panel (newer SDKs) exports and imports model snapshots as JSON.

## Simulating input from JS

Dispatch real bubbling DOM events; framework delegated handlers (React and friends) receive them:

- `el.dispatchEvent(new MouseEvent('click', {bubbles: true, ...}))` works; `HTMLElement.click()` does not exist.
- `PointerEvent` and `InputEvent` constructors are missing: dispatch `pointer*` names as `MouseEvent`, and `new Event('input', {bubbles: true})` for input events.
- `KeyboardEvent` and `MouseEvent` constructors exist, as does the native `HTMLInputElement.value` setter (set value natively, then dispatch `input`).
- Real input reaches the page only when the game forwards it; hover state and `mouseenter/over/leave/out` update exclusively on game-fed mouse-move/scroll events.

## DOM and JS quirks

- Element lookup APIs: `document.evaluate` (XPath), `createTreeWalker`, and `innerText` do not exist, and `document.title` is undefined; scan `querySelectorAll` results and filter on `textContent` instead.
  In the JS query APIs (`querySelector*`, `closest`, `matches`), combinators, `:nth-child`, and `[attr*=]` all match, but `:not()` and `:first-of-type` throw "Invalid CSS selector" (verified on CS2).
- `event.target` and `event.currentTarget` are valid only inside the dispatching call stack; a stored event object has them nulled afterwards.
- Whitespace text nodes are virtualized through ONE shared internal node, materialized on access (pre-2.2 engines): never store a whitespace node reference, and avoid hardcoded `childNodes[i]` indexing (`innerHTML`-parsed markup shows no whitespace text nodes in `childNodes` at all).
  From 2.2, whitespace nodes are real DOM nodes (indices shift).
- `parentNode`/`parentElement` are not guaranteed for detached, unreferenced nodes.
- `getElementsByTagName/ClassName` return live `HTMLCollection`s only since 1.52.1.
- `window.onerror` receives a single event object (the standard 5-argument signature is not used), on V8 platforms only.
- `DOMContentLoaded` exists since 1.27; prefer `load` (which also waits for fonts).
- `document.createComment` and comments parsed via `innerHTML` produce real comment nodes; whether the page-load HTML parser keeps source comments is unverified, so keep framework comment anchors out of static markup.
  Consecutive text segments merge into one text node.
- Re-setting an `<img>` src to the SAME URL fires no `load` event; create a fresh `Image` per load when the event matters.
- Events the engine never fires: `contextmenu`, clipboard (`copy`/`cut`/`paste`), native drag-and-drop, `submit`/`reset`/`invalid`, composition (IME) events, `touchcancel`, and all Pointer Events including pointer capture.
- Timers, rAF, animations, and event dispatch tick inside the game's `View::Advance`; when the game stops advancing the view, page JS freezes entirely.

## Missing platform APIs and the usual shims

- `fetch()` has never existed; use `XMLHttpRequest` or the `whatwg-fetch` polyfill.
- `window.postMessage` is missing from the engine (`postmessage-polyfill` when a dev server needs it; some game bundles, CS2's included, polyfill it themselves); `setInterval` requires an explicit delay argument before 2.0; `btoa`/`atob` and `navigator.platform` are missing (assign `navigator.platform` before loading libraries that sniff it).
- Also absent on CS2 (probe per title): `sessionStorage`, `URLSearchParams` (`URL` itself is native and works), `FormData`, `File`/`FileReader`, `TextEncoder`/`TextDecoder`, `IndexedDB`, `document.cookie`, `requestIdleCallback`, `setImmediate`.
  `navigator` is minimal (`userAgent` and `getGamepads()`); `location`/`history` work without top-level navigation.
- `XMLHttpRequest` and `localStorage` exist but are served by the game's resource layer (`coui://` scheme and storage handler), so reachability and persistence are whatever the game wired.
  Cohtml extends XHR with `responseArrayBuffer()`/`responseBlob()`.
- `WebSocket` exists only when the game provides a socket transport; probe `typeof WebSocket` per title.
