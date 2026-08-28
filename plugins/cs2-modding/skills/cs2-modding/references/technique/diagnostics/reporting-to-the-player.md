# Reporting a problem to the player

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

The two surfaces a mod uses to say something went wrong, and the traps in each.
Whether a failure reaches the player _without_ being asked to is in [diagnostics.md](diagnostics.md).

## Notifications

The static notification facade's whole surface is three methods, everything after the identifier optional:

```csharp
void Push(string identifier, LocalizedString? title = null, LocalizedString? text = null,
          string titleId = null, string textId = null, string thumbnail = null,
          ProgressState? progressState = null, int? progress = null, Action onClicked = null)

void Pop(string identifier, float delay = 0f, /* then Push's eight, in the same order */)

bool Exist(string identifier)
```

All three no-op when the UI system behind them is not bound, which is the case until it is created and again after it is destroyed.
The progress states are `None`, `Progressing`, `Indeterminate`, `Complete`, `Failed`, `Cancelled` and `Warning`.

Three behaviours a caller needs:

- **`Pop` with a non-zero delay shows the notification first**, then schedules its removal — so it is the idiom for "say this, then fade".
  Source: `src/Game/Game.UI.Menu/NotificationUISystem.cs`.
- **Pushes merge by identifier, and three of the fields are first-write-wins**: `title`, `thumbnail` and `onClicked` are filled only while the stored one is still empty, so no later push can change them.
  `text`, `progressState` and `progress` overwrite every time, which is what makes a progress notification work.
  Source: `src/Game/Game.UI.Menu/NotificationUISystem.cs`.
- **Pass `title:` and `text:` on every push, including the ones that only move a progress bar.**
  `titleId` and `textId` are wrapped into vanilla key shapes (`Menu.NOTIFICATION_TITLE[<id>]` and `Menu.NOTIFICATION_DESCRIPTION[<id>]`), so a mod passing its own key there gets a key that does not exist.
  The wrapping runs on a null id too, so omitting one of the pair fills it with that shape wrapped around nothing rather than leaving it alone.
  By the rule above a bogus title then sticks for the life of the notification, while a bogus text overwrites the text that was there.
  Source: `src/Game/Game.PSI/NotificationSystem.cs` (the wrapping, applied whether or not an id was passed), `src/Game/Game.UI.Menu/NotificationUISystem.cs` (the key shapes).

## Dialogs

Dialogs go through `GameManager.instance.userInterface.appBindings`:

| Entry point | Shape |
| --- | --- |
| `ShowMessageDialog(MessageDialog, Action<int>)` | one confirm action plus optional others |
| `ShowConfirmationDialog(ConfirmationDialog, Action<int>)` | confirm and cancel |
| `ShowConfirmationDialogAndWait(ConfirmationDialog)` | the same, returning a `Task<int>` |
| `ShowConfirmationDialog(DismissibleConfirmationDialog, Action<int, bool>)` | second callback argument is the "do not show again" checkbox |
| `ShowErrorDialog(ErrorDialog)` and `DismissAllErrors()` | the error dialog, built by hand |

The callback's integer is **positional**: `0` is the confirm action, `1` the cancel action, and the other actions start at `2`.
A message dialog passes no cancel action, so its buttons yield `0` and then `2..n` — but **the frame can answer for the player**: closing the dialog rather than pressing a button invokes the same callback, with `1` on the default skin and `-1` on the Paradox one.
So handle every integer, including the ones your own action set never produces; a callback that only switches on its buttons leaves whatever it was gating on uncleared when the player presses Escape.
**Three of the five entry points share one callback slot on the bindings**, overwritten on each show: `ShowMessageDialog`, `ShowConfirmationDialog(ConfirmationDialog, …)` and `ShowConfirmationDialogAndWait`.
A message dialog raised while a confirmation is still unanswered therefore destroys the confirmation's callback — and where that callback was `ShowConfirmationDialogAndWait`'s, the awaited `Task<int>` never completes and the mod's async path hangs for the rest of the session, with no exception and no log line.
Only the dismissible overload has a slot of its own, and it protects that callback rather than the dialog: all of them drive the same binding, so any show still replaces whatever is on screen.
Source: `src/Game/Game.UI/AppBindings.cs` (the shared slot and the single binding), `src/Game/Game.UI/ConfirmationDialogBase.cs` (the action order the indices follow), `Cities2_Data/Content/Game/UI/index.js` (the close handler's `1` and `-1`).

**Error dialogs take a different route, and it deduplicates.**
They are keyed on a fingerprint of the message and the details, so two distinct failures that happen to report identically become one dialog with a bumped count and the second is never seen.
Their queue is last-in-first-out, so a new error displaces the one on screen rather than waiting behind it, and a repeat of a fingerprint the player has muted is counted and shown to nobody.
Source: `src/Game/Game.UI/ErrorDialogManager.cs`.

An `ErrorDialog` built by hand is the way to raise the error dialog without logging an error.
Its public fields are `severity` (`Warning` or `Error`), `actions`, `localizedTitle`, `localizedMessage` and `errorDetails`; `count` and `fingerprint` are filled in by the manager.
The action bits are `Continue = 1`, `Ignore = 2`, `Mute = 0x100`, `SaveAndContinue = 0x200`, `SaveAndQuit = 0x400`, `Quit = 0x20000` and `Rename = 0x40000`.

**The set you pass is not the set the player sees, and the difference can quit their game.**
The layout pass sorts the bits into exclusive groups — `Continue` with `Ignore`, `SaveAndContinue` with `SaveAndQuit`, `Quit` with `Rename` — and keeps only the **lowest set bit of each**.
So `Continue | Ignore` renders one button and `Quit | Rename` renders `Quit`; the dropped action is simply not there.
It then fills two of those groups where you left them empty: nothing from the first yields `Continue`, and nothing from the third yields `Quit`.
So a dialog built to offer a single dismiss button offers an exit-the-game button beside it, and the floor is two buttons rather than one.
Pass whichever of `Quit` and `Rename` belongs in that slot — one of them, never both.
`Mute` is not yours to pass at all: the manager rewrites that bit on every enqueue and again every frame from its own spam state, so it appears when that state says so and is cleared whatever you set.
Source: `src/Game/Game.UI/ErrorDialog.cs` (the groups, the lowest-bit rule and the two defaults), `src/Game/Game.UI/ErrorDialogManager.cs` (the `Mute` rewrite).

Showing one pauses the simulation the same way a logged error does.

## The trap both surfaces share

`LocalizedString`'s implicit conversion from `string` is the _id_ form, not the value form.
So passing a literal English sentence to a dialog or a notification passes it as a localisation key.
Write `LocalizedString.Value(...)` explicitly for literal text; `localization` owns what an unresolved key falls back to.
Source: `src/Game/Game.UI.Localization/LocalizedString.cs`.

(VOLATILE: the action-bit values and the `ErrorDialog` field names, the `AppBindings` method names, and the notification key namespaces — the game's own error dialog, app bindings and notification UI types.)
