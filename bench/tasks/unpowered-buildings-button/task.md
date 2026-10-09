# An unpowered-buildings button in the bottom-right menu

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want a new button in the column of round buttons at the bottom right of the in-game screen, the one with the notifications and Chirper buttons. It should carry an electricity icon and a small badge in its corner showing how many buildings currently have no electricity, kept up to date while I play, and hidden when the count is zero. Clicking the button selects the next building without power and moves the camera to it, so I can cycle through all of them.

## Checklist

### The button reaches the right menu through a route that renders it

- Falls in: the registrar calls `append` with the module path `game-ui/game/components/right-menu/right-menu.tsx` and the export name `RightMenu` (or another export whose component renders a fixed list rather than its `children`), in the module-path form `append(modulePath, exportName, component)`.
- Avoids: the registrar calls `append("GameBottomRight", component)` in the anchor form, or `extend`s `RightMenu` with a wrapper that renders the original component beside the mod's button.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.js:129783

### The registrar guards its own body

- Falls in: the registrar's registry calls (`append`, `extend`, `override`, `add`) stand bare in the function body with no `try`/`catch` around them.
- Avoids: the registrar wraps its registry calls in a `try`/`catch` and logs the caught value.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.js:140009

### Sizes are written in rem

- Falls in: the mod's stylesheet or inline styles give the badge's dimensions, offsets, padding or radius in `px`.
- Avoids: every length in the mod's styles is in `rem` (or another relative unit such as `em` or `%`), with no `px` lengths.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.css:621

### The badge's font size follows the text-scale setting

- Falls in: the badge's `font-size` is a literal length, such as `12px` or `12rem`.
- Avoids: the badge's `font-size` is one of the game's `--fontSizeXXS` … `--fontSizeXXXL` tokens, or a `calc()` multiplying a `rem` value by `var(--fontScale)`.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.css:207

## Roots

- decompile
- install
- ui-bundle
