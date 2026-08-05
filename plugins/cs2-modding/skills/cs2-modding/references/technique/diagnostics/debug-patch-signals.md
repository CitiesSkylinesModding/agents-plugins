# Confirming the debug patch took

Verified against game version 1.6.0f1.

Whether a debugger can attach at all, read from the logs and from the install.
Applying the patch is the setup skill's `debug-patching` reference; getting a Burst-compiled job into the debugger once attached is `performance-and-memory`.

The patch is two edits — swapping in the development player binary, and turning the player connection's debug flag on in `boot.config` — and each signal below is _sourced_ in one of them.

Two signals in the logs, readable after a launch:

- **`SceneFlow.log`'s version block reads `Game configuration: Development (Mono)`.**
  Its source is Unity's own debug-build property, which is a property of the player binary, so this line reports the **binary swap**.
  An unpatched install reads `Release (Mono)`.
- **`Player.log` carries `Starting managed debugger on port <n>` and the matching mono debugger-agent option line.**
  That is the debugger agent coming up, which the **`boot.config` flag** turns on — and it names the port, which is assigned per launch rather than fixed, so this is where the port to attach to comes from.

Two static checks, with the game closed:

- `Cities2_Data\boot.config` ends with `player-connection-debug=1`, readable in a text editor.
- `UnityPlayer.dll` itself contains the string `UnityPlayer_Win64_player_development_mono_x64.pdb`, which the linker embeds in the binary's debug directory.
  Grepping the DLL for `player_development_mono` is the check, and it reads the **binary swap** off the swapped file itself — so it holds whichever route applied the patch.

A `.pdb` of that name may also sit beside the DLL; nothing requires it to be there, so its absence proves nothing and the grep above is the check to run.

A mod can also read Unity's debug-build property at runtime.

**Symbols come across for free on a local deployment.**
Not a signal that the patch took — this is the mod's own symbols rather than the player binary's.
The loader loads a `.pdb` sitting beside the mod's assembly when one is there, and the toolchain's deployment copies the whole output directory, so a locally deployed mod gets file and line numbers in every logged stack trace.

(VOLATILE: the quoted strings above — the game manager's version block, the mono debugger agent's own startup output, the install's `Cities2_Data` directory, and `UnityPlayer.dll`'s own embedded debug-directory path.)

(UNVERIFIED: whether a single missing line names which of the two edits failed — only the both-applied case has been observed. Reverting one edit at a time on a patched install and re-reading the two lines settles it.)
