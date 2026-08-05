# Patching the game for debugging

Verified against game version 1.6.0f1.
Paths throughout are Windows.

A retail build ships the player library with managed debugging compiled out, so a debugger has nothing to attach to.
Swapping that library for the development variant from the matching Unity editor, and turning the debug connection on in the boot configuration, turns the user's installed game into a development build.
Both edits live in the game folder, both survive until the next game update, and both are reversible.

## The version that has to match

The development library must come from the editor version the game was built with — not the newest editor, and not whichever version the modding toolchain installed for compiling mods.
Read it off the game:

```powershell
(Get-Item "$env:CSII_INSTALLATIONPATH\Cities2.exe").VersionInfo.ProductVersion
```

(VOLATILE: the game's Unity version — the executable itself, which is the only copy that tracks a game update; a number written down anywhere else is a snapshot.)

Install exactly that editor version through Unity Hub, with Windows build support, when it is not already present.
Only the one file below is taken from it.

## The patch

1. Back up `<game>\UnityPlayer.dll` — a game update will overwrite it anyway, but the backup is what makes the change reversible on demand.
2. Copy `UnityPlayer.dll` from `<editor>\Editor\Data\PlaybackEngines\windowsstandalonesupport\Variations\win64_player_development_mono\` over the game's copy.
3. Append `player-connection-debug=1` on its own line to `<game>\Cities2_Data\boot.config`.

## Verifying it worked

Launch the game and look at the bottom-right corner: a development build shows a small "Development Build" label there.
No label means step 2 did not take — usually the file was copied from the wrong editor version, or from the non-development variation folder.

From outside the game, `Player.log` at the root of the user data path is the second signal: on a patched install its `Player connection` line carries `[Debug] 1`.

`diagnostics` carries two further log signals, one sourced in each edit, plus two checks readable with the game closed.

Set `Debug patch: applied` in the record once a signal confirms it, so a later session does not re-walk this.

Verify before pointing a debugger at the game.
An unpatched build refuses the connection in a way that reads as "the debugger cannot find the process", which sends people looking in the wrong place.

## Why not the automated patcher

The community distributes a build-time package a mod project can reference to have all of this done on every Debug build, and undone on Release and Clean builds.
It carries its own copy of the development binaries, pinned to one Unity version.
Once the game moves to a newer Unity than the package was built against, the copied library no longer matches the game's own data and the game fails at launch with `Failed to load PlayerSettings`.

The official wiki documents that failure and recommends the manual copy instead.
Do the manual copy: it is three steps, and it always matches because the version comes from the game.

## After a game update

An update restores the retail player library and can reset the boot configuration, so both steps are re-applied — and the editor version to copy from may have moved with it, so re-read it rather than reusing the last one.
This is the debug-patch half of the refresh branch in the parent skill.

## What the patch unlocks

Breakpoints in a mod's own code, hit while the game runs, in Visual Studio ("Attach Unity Debugger") or Rider ("Attach to Unity Process").
They resolve only for a mod installed locally, in the folder `CSII_LOCALMODSPATH` points at, rather than one subscribed from the mod platform.

Mod code is normally not Burst-compiled, so it debugs as ordinary managed code.
The game's own jobified systems are native code no managed debugger can see, so stepping into one needs the `--burst-disable-compilation` launch flag the parent skill covers.

The same patch is what the sibling `unity-devtools` plugin needs, if the user happens to have it: that plugin drives a running game over the Mono Soft Debugger protocol, which a retail build does not expose.
Worth a sentence to the user when the patch lands, and never a requirement — nothing here depends on that plugin being installed.
