# Patching the game for debugging on Linux

Verified against game version 1.6.0f1.

The game runs under Proton, so the patch is [debug-patching.md](debug-patching.md)'s own three edits applied to the Windows game, and everything there holds except where the development library comes from.
The commands below use the shell variables and the `wine` and `winpath` helpers defined at the top of the setup in [linux-toolchain.md](../../cs2-mod-project/references/linux-toolchain.md); define them first, and keep the game closed.

## Getting the development library

Read the game's own Unity version off the executable — usually not the toolchain's `CSII_UNITYVERSION`:

```bash
strings -el "$GAME/Cities2.exe" | grep -A1 '^ProductVersion$'
# <version> (<changeset>)
GAMEUNITY=<version>
GAMECHANGESET=<changeset>
```

Unity Hub for Linux has no Windows player to offer for that version, and 7-Zip cannot open Unity's Windows installer.
Take the file from a temporary silent install of that version into the game's prefix, then uninstall it:

```bash
curl -LO "https://download.unity3d.com/download_unity/$GAMECHANGESET/Windows64EditorInstaller/UnitySetup64-$GAMEUNITY.exe"
wine cmd /c "$(winpath "$PWD/UnitySetup64-$GAMEUNITY.exe") /S /D=C:\\Program Files\\Unity $GAMEUNITY"
WINEPREFIX="$PFX" "$PROTON/files/bin/wineserver" -w
cp "$PFX/drive_c/Program Files/Unity $GAMEUNITY/Editor/Data/PlaybackEngines/windowsstandalonesupport/Variations/win64_player_development_mono/UnityPlayer.dll" UnityPlayer-development.dll
wine "C:\\Program Files\\Unity $GAMEUNITY\\Editor\\Uninstall.exe" /S
WINEPREFIX="$PFX" "$PROTON/files/bin/wineserver" -w
```

When the game's version equals the toolchain's, that editor is already in the prefix: copy the file from it and skip the install and the uninstall, which would remove the editor the build needs.

## The patch

```bash
cp "$GAME/UnityPlayer.dll" UnityPlayer-retail.dll
cp -f UnityPlayer-development.dll "$GAME/UnityPlayer.dll"
grep -q '^player-connection-debug=1' "$GAME/Cities2_Data/boot.config" ||
  printf 'player-connection-debug=1\r\n' >> "$GAME/Cities2_Data/boot.config"
```

`boot.config`'s last line ends in CRLF, so the appended line does too.

## Attaching from Linux

Verify as [debug-patching.md](debug-patching.md) says; `Player.log` sits at the root of `$USERDATA`.
The game's player-connection broadcast reaches the Linux host, and a native client of the Mono soft debugger attaches to the port it advertises, which is how the sibling `unity-devtools` plugin connects.
Rider's Attach to Unity Process speaks the same protocol (UNVERIFIED: Rider on Linux attaching to the game under Proton — nobody has tried it).
