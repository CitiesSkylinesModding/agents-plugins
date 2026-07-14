# Gameface performance: cost model, rules, and profiling

Doc paths below are relative to `https://docs.coherent-labs.com/cpp-gameface/`.
Primary source: `performance_optimization/enhancedperformancetracing/` (the de-facto optimization guide, organized per pipeline stage).

## The frame pipeline

Each frame the host game drives, in order:

1. **Advance** (UI thread): JS execution (timers, events, rAF), CSS animation ticks, style recalculation.
   Also where the previous frame's layout results sync back, so heavy layout in frame N taxes frame N+1's Advance.
2. **Layout** (worker thread): full flex solve when a layout property changed, or a cheap node-transform update when only visual properties changed.
3. **Displaying**: DOM iterated, paint commands recorded for elements intersecting dirty regions only.
4. **Painting** (render thread, Renoir): batching, tessellation, GPU uploads, draw calls.

Two consequences frame everything: work scales with what CHANGED this frame (dirty nodes and dirty regions), and layout results are one frame late for JS.

## The invalidation cost model

- Style recalculation cost is proportional to the number of DOM nodes whose styles changed; changing one property submits the whole node.
- Animation tick cost is proportional to animated elements times animated properties each.
- The layout split is the big lever: changing a LAYOUT property (`width`, `height`, `top`, `padding`, `margin`, any `flex-*`) triggers a full-tree flex solve; changing only visual properties (`transform`, `opacity`, `color`, ...) takes the fast transform-update path.
  Animate transforms and opacity, never widths and margins.
- The flex algorithm's worst case is O(4^depth).
  Layout caching normally saves you, but cache misses multiply with deep trees, many children per level, and undefined dimensions.

Rules the docs give content developers:

- Define explicit dimensions (`width`/`height` or `flex-basis`) wherever possible; `auto` dimensions force re-division of space at every level.
- Keep DOM trees shallow; use `position: absolute` to take decorative elements out of flex flow entirely (computed separately, explicitly called out as a win).
- Use `align-items: stretch`/`align-self: stretch` sparingly; `flex-wrap: wrap` with overflow runs the stretch step twice.
- Minimize per-frame JS: everything in `Execute Timers` markers is yours.
- Complex selectors (combinators) sit behind a per-game flag partly because disabling them is a documented performance win.
  Structural pseudo-classes (`:first-child`, `:nth-child`, ...) force style rematching of sibling nodes; use them sparingly.

## Repaint and layers

- Rendering is incremental: only dirty regions repaint.
  Fewer changing screen regions means less work in every downstream stage.
  Visualize dirty regions with F3 in the Player, or Paint/Redraw Flashing in the DevTools Cohtml panel.
- Layer-creating properties force the element's content into a separate GPU texture: `opacity`, `filter`, `backdrop-filter`, `mix-blend-mode`, `isolation: isolate`, `mask-image`.
  Use sparingly; blur needs two extra textures; every layer adds render-target switches.
  Elements that form no stacking context display much faster.
- Simple Opacity (1.58+, `content_development/simpleopacity/`): `coh-simple-opacity: on` multiplies opacity into descendant colors instead of compositing a texture, trading exact standard visuals (overlapping children blend) for GPU memory and speed.
  Inoperative when the element also uses filters, blend modes, or isolation.
- SVG paths re-tessellate whenever they draw at a new scale: scale-animating a path pays tessellation every frame.
  Watch `Fetch Tessellated Path` (cache hit) versus `Tessellate Path` (miss) markers.
  SVG surface caching covers SVGs up to 1024x1024; inline SVGs are never GPU-cached.
- Image draws batch into one draw call only when the images share a GPU texture: atlas UI images (Atlas Creator tool docs).

## Silent traps

- Async style solving (default since 1.55) silently reverts to synchronous for the whole view when any active stylesheet merely CONTAINS `coh-composition-id` or custom-effect declarations, used or not.
  Removing the declarations restores it.
- Inline CSS and inline JS in HTML defeat the preload caches; keep them in separate files.
  A preloaded HTML fragment fetched over XHR must be inserted UNMODIFIED to stay cached.
- Internal caches (`performance_optimization/internalcaches/`) mis-sized both ways: a GPU texture created and destroyed every frame in traces means a cache is too small for the effect load (blur, layers); the content-side fix is reducing layer-creating usage.
- Cohtml evicts unreferenced images from its cache quickly, so an image only displays instantly when something keeps it alive: a live DOM reference exempts it (even a `display: none` node), or preload with `new Image()` and await `load`.

## Profiling

- The Chrome DevTools Performance tab is the only JS-capable profiler.
  Record, then read Cohtml markers per stage; hovering layer/paint markers highlights the responsible DOM node, and the Summary tab links it to the Elements panel: that is the attribution workflow.
- Checkboxes add texture counters, GPU memory tracks, and per-frame screenshots.
  Higher trace levels (L2/L3) identify individual nodes and data-binding attributes but degrade performance and can produce traces of hundreds of MB; start at L1.
- Marker names drift across versions; treat them as approximate labels, and expect a 1.x-era game (CS2) to expose fewer panels and markers than the 3.x docs describe.
- Source maps must be INLINED for the inspector (it cannot fetch `coui://` URLs).
- The Cohtml DevTools panel (More Tools > Cohtml) holds paint flashing, cache statistics and sizing, and metadata emission for GPU debuggers.
