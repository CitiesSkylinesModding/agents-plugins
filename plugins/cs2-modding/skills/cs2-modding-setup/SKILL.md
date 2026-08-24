---
name: cs2-modding-setup
description: 'Provisions and maintains the local ground truth for Cities: Skylines II mod development. Use when the user asks to be set up for CS2 modding, when the decompile or the readable copy of the game UI bundle needs creating or updating, when they ask which mods are worth reading for a problem, when the in-game debug menu or the UI debugging port turns out to be unavailable, or when another skill finds no local source recorded.'
---

# Setting up a Cities: Skylines II modding environment

Five things, provisioned separately and none of them required: a **decompile** of the installed game, the game's **developer launch options**, a **debug patch** that lets a debugger attach to it, a **mod corpus** to read from, and a readable copy of the game's **UI bundle**, which ships as one minified line.

Take the request as it comes rather than asking the user for a keyword.
No argument, or anything that reads as "set me up", runs first-time setup.
A game update, a stale decompile, or "does this still match my game" is the refresh branch.
"What should I read for X" consults the catalog and clones nothing unless asked.

Verified against game version 1.6.0f1.
Paths and commands throughout are Windows.

## The record

One file holds everything this skill provisions: `~/.cs2-modding/setup.md`, which is `%USERPROFILE%\.cs2-modding\setup.md` on Windows.

Read it before reading any local source, here and in every other skill of this plugin.
A missing file, a missing key, or a `(none)` value means that source does not exist: route the user to this skill and provision it, rather than guessing a path or grepping one that is not there.

It sits in the user's home directory rather than in agent memory or in a project file for two reasons worth telling the user when you create it: only one of the two supported harnesses has persistent agent memory, and what it records belongs to the machine — one game install, one decompile, however many mod projects.
State the path in plain text when you write it, because a user who does not know the file exists cannot move it or delete it.

Fixed keys, one per line, `(none)` for anything not provisioned:

```markdown
# cs2-modding setup

Game install: C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II
Game version: 1.6.0f1
Unity version: 2022.3.71f1
Decompile root: C:\Users\<user>\Documents\cs2-decompile
Decompiled: 2026-08-01
Debug patch: applied
Launch options: set
Mod corpus root: (none)
UI bundle copy: (none)
UI bundle lines: (none)
```

`UI bundle copy` is a reformatted copy of the game's own `Cities2_Data/Content/Game/UI/index.js`, which ships minified to a single line and cannot be read around or cited as it stands; step 5 makes one.
`UI bundle lines` is that copy's line count.
Anything citing the copy cites line numbers, so the count is how a later reader tells whether their copy and the citations still agree — a differing count means they do not, whatever produced it.

## First-time setup

### 1. Locate the game and read its versions

The official modding toolchain, once installed, exports the paths as environment variables: `CSII_INSTALLATIONPATH` (game root), `CSII_MANAGEDPATH` (the managed assemblies), `CSII_USERDATAPATH` (user data, mods, logs).
Read those first, ask the user for the game root when they are unset, and offer the platform's default install path as a suggestion to confirm rather than as an answer.

The Unity version is the product version of `Cities2.exe`:

```powershell
(Get-Item "$env:CSII_INSTALLATIONPATH\Cities2.exe").VersionInfo.ProductVersion
```

Take it from the executable and nowhere else.
The toolchain exports a `CSII_UNITYVERSION` too, but that is the editor version it installed for compiling mods, which drifts from the game's own — and debug patching against the drifted one is the documented way to break the game at launch.

The game version is the `Game version:` line that every launch writes to `Player.log`, at the root of the user data path:

```powershell
Select-String -Path "$env:CSII_USERDATAPATH\Player.log" -Pattern "^Game version:"
```

That needs the game to have been run at least once; when there is no log, the main menu shows the version and the user can read it off.
`Game.dll` carries no version of its own, so it is not a fallback.

Done when both versions are in hand.

### 2. Decompile the managed assemblies

The source is the user's own installed game.
Nothing is downloaded, nothing copyrighted changes hands, and the artifact belongs to them.

Agree on a decompile root with the user before starting, and put it outside the game folder so a game update cannot overwrite or delete it.
The output is tens of thousands of files and the run takes minutes, so confirm the location before starting rather than after.

```powershell
dotnet tool install -g ilspycmd

$managed = $env:CSII_MANAGEDPATH
$root = "<decompile root>"
Get-ChildItem $managed -Filter *.dll | ForEach-Object {
  ilspycmd -p -o "$root\src\$($_.BaseName)" -r $managed $_.FullName
}
```

`-p` exports each assembly as a compilable project instead of one flat file, which is what lays the tree out as `src/<assembly>/<namespace>/<Type>.cs` — the shape every navigation recipe in this plugin assumes.
`-r` points at the managed folder so cross-assembly references resolve.

The game's own code is `Game.dll` and the `Colossal.*` assemblies; the rest is Unity, .NET and third-party code, worth having for reference and cheap to include in the same pass.

Done when `src/Game/Game.Modding/IMod.cs` exists and `src/Game` holds a few thousand `.cs` files.

### 3. Turn on the developer launch options

Cheap, independent of everything else, and worth doing for every mod author — so raise it rather than waiting to be asked.

The flags go on the game's launch command.
On Steam that is Properties → General → Launch Options, where `%command%` stands for the executable and the flags follow it:

```
%command% --developerMode --uiDeveloperMode
```

| Flag | What it gives you |
| --- | --- |
| `--developerMode` | The in-game debug menu, opened with TAB. Not required for anything, and the fastest way to inspect what the simulation thinks is true. |
| `--uiDeveloperMode` | UI inspection and debugging on `localhost:9444`; also forces the game to keep running while unfocused. |
| `--burst-disable-compilation` | Turns Burst compilation off. Leave it off for normal play — it slows the game down heavily — and add it when debugging a native crash, or when a debugger must step into the game's own jobified systems. |

Press hardest on `--uiDeveloperMode`: it is what opens the UI debugging port, which is also what the sibling `coherent-gameface` plugin connects to.

Done when the user confirms the flags are on their launch command; record it, since nothing readable from the game side proves it.

### 4. Record it

Write the record described above, then tell the user its path and what it is for.

### 5. Offer the optional extras

A user who wanted a decompile is finished at step 4.
The other three are independent of it and of each other, so offer them and let the user decline:

- Patching the game so a debugger can attach: [debug-patching.md](references/debug-patching.md).
- Cloning community mod source to read: [mod-catalog.md](references/mod-catalog.md).
- Making the game's UI bundle readable, worth offering to anyone working on a mod's interface.
  `Cities2_Data/Content/Game/UI/index.js` under the install ships minified to a single line, so reading around a match or citing one needs a reformatted copy.
  Copy it somewhere of the user's choosing, reformat that copy with prettier at its defaults, and fill both record keys with the path and the resulting line count.
  `index.css` beside it ships the same single-line way; a session working on the game's UI reformats a copy of it next to the script's.

## Refreshing after a game update

A game update leaves the paths valid and the code behind them wrong, which is the failure mode this branch exists to catch.

Re-read the versions as in step 1 and compare them against the record:

Nothing moved → nothing is stale, so say so rather than redoing the work.

A moved game version stales three things, each guarded by its own key in the record:

- `Decompile root` is set → re-decompile, deleting `src/` first so types the update removed do not survive as stale files.
- `Debug patch` reads applied → re-apply [debug-patching.md](references/debug-patching.md), which carries why an update undoes it and what changes when the Unity version moved too.
- `UI bundle copy` is set → remake it from the updated `index.js` as in step 5, and write the new `UI bundle lines` count.
  The frontend is rebuilt every patch, so a stale copy is worse than none: its module paths and line numbers all still look plausible.

A key that is absent or `(none)` means the user never provisioned that one, so leave it alone rather than offering to repair something they declined.

Update the record either way, including when nothing changed, so the next session knows the check happened.

## Recommending reading

Answer from [mod-catalog.md](references/mod-catalog.md), which carries the entries, how to match a question against them, and when a corpus is worth cloning.
