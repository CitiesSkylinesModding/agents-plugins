# A City Pulse panel of the mod's own

## Kind

feature request

## Prompt

I'm making a Cities: Skylines II mod. I want a panel of my own in the game UI, called City Pulse, showing three live figures: the city's population as a big headline number, and under it how many buildings currently have no electricity and how many have no water, each beside the game's own electricity or water icon. It should be one of the game's panels rather than a floating box: it opens in the middle of the screen with the same kind of title bar and close button as City Info or Statistics, and only one of those panels is ever open, so opening mine closes whichever of theirs is open and opening theirs closes mine. I'd open and close it from a round button at the top right of the screen, next to the advisor button.

## Checklist

### The registrar guards its own body

- Falls in: the registrar's registry calls (`extend`, `append`, `override`, `add`) stand bare in the exported function with no `try`/`catch` around them.
- Avoids: the registrar wraps its registry calls in a `try`/`catch` and logs the caught value.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.js:140009

### The game's icons are addressed on a host that serves them

- Falls in: an icon of the game's is referenced as a `coui://` URL on the `gameui` host, in any casing, such as `coui://GameUI/Media/Game/Icons/Electricity.svg`.
- Avoids: the icon is referenced by the bare path the game's own components use, such as `Media/Game/Icons/Electricity.svg`, or by an absolute `assetdb://gameui/Media/...` URL.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src/Game/Game.SceneFlow/GameManager.cs:1767

### Sizes are written in rem

- Falls in: the mod's stylesheet or inline styles give the panel's width, paddings, margins or offsets in `px`.
- Avoids: every length in the mod's styles is in `rem` (or another relative unit such as `em` or `%`), with no `px` lengths.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.css:621

### The headline's font size follows the text-scale setting

- Falls in: the headline's `font-size` is a literal length, such as `40px` or `40rem`.
- Avoids: the headline's `font-size` is one of the game's `--fontSizeXXS` … `--fontSizeXXXL` tokens, or a `calc()` multiplying a `rem` value by `var(--fontScale)`.
- Reference: cs2-modding-ui/references/frontend-and-injection/frontend-and-injection.md
- Source: src-ui/source.js:47351

## Roots

- decompile
- install
- ui-bundle
