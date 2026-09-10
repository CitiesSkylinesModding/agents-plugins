# The toolchain on Linux

Verified against game version 1.6.0f1.

The game has no Linux build and runs under Proton, and its Options → Modding page cannot install the toolchain there: that installer downloads Windows programs and stores its settings as Windows user environment variables.
This file sets the same toolchain up by hand.
The wiki's [Modding Toolchain on Linux](https://cs2.paradoxwikis.com/Modding_Toolchain_on_Linux) page carries the same procedure written for a person; hand it to a user who would rather run the steps themselves.

Mods then build with the native .NET SDK, so the IDE, the UI build and unit tests all run natively.
Only the toolchain's two Windows-only programs, the post-processor and the publisher, run under the game's own Proton.
Opening the Unity mod project once is done by the native Linux Unity editor, licensed by the native Unity Hub.

## The user scope Linux does not have

`Mod.props`, and every project created from the mod template, read the `CSII_*` variables with `Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)`.
On Windows that reads the registry; on Linux .NET it always returns null, whatever the environment holds.
**An unpatched template project therefore never imports `Mod.props`, and fails with `NETSDK1013: The TargetFramework value '' was not recognized`** rather than with anything naming the toolchain, since `Mod.props` is what sets `net48`.
The toolchain-files step below switches the toolchain's copy of `Mod.props` to the process environment, and the project-changes step does the same for each project.
Any other user-scope read a project adds — a test project's hint paths, a resolver in test code — takes the same process-environment fallback.

## Requirements

- The game installed through Steam and launched at least once, which creates its Proton prefix.
- The .NET SDK installed natively, 8.0 or newer.
- Unity Hub installed natively, the `.deb` or the Flatpak, signed in with a Unity account holding a Personal license.
- Node.js, for UI mods.
- About 15 GB free: two Unity editors of about 6 GB each, plus their downloads.

protontricks, `dotnet48` and `corefonts` are not needed.

## Setup

Keep the game closed throughout.
The commands use these shell variables; `STEAM` moves when the game sits in another Steam library, and Flatpak Steam lives under `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`.

```bash
STEAM="$HOME/.local/share/Steam"
GAME="$STEAM/steamapps/common/Cities Skylines II"
PFX="$STEAM/steamapps/compatdata/949230/pfx"
USERDATA="$PFX/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
TOOLING="$GAME/Cities2_Data/Content/Game/.ModdingToolchain"   # shipped by the game
TOOLPATH="$USERDATA/.cache/Modding"                             # where the installer copies it
```

**Every Windows command runs in the game's prefix with the Proton build the game itself uses** — the game's Properties → Compatibility in Steam.
Any other Wine, the system one included, can upgrade the prefix behind the game's back.
That build's `proton` script names the prefix version it writes, and it must equal the prefix's own:

```bash
PROTON="$STEAM/steamapps/common/Proton - Experimental"
cat "$PFX/../version"                            # the prefix's version...
grep CURRENT_PREFIX_VERSION= "$PROTON/proton"    # ...must match this one
wine() { WINEPREFIX="$PFX" WINEDEBUG=-all "$PROTON/files/bin/wine" "$@"; }
winpath() { local p=${1//\//\\}; printf 'Z:%s' "$p"; }   # Linux path to Windows path
```

### 1. Toolchain files

The game ships the whole toolchain under `$TOOLING`, so nothing is downloaded for this step.
Copy it where the in-game installer would, with the user-scope argument stripped from `Mod.props`:

```bash
mkdir -p "$TOOLPATH" "$USERDATA/Mods"
cp -r "$TOOLING/ModPostProcessor" "$TOOLING/ModPublisher" "$TOOLING/Mod.targets" "$TOOLPATH/"
sed "s/, 'EnvironmentVariableTarget.User'//g" "$TOOLING/Mod.props" > "$TOOLPATH/Mod.props"
```

### 2. Windows .NET runtime

The post-processor and the publisher are .NET 6 programs (VOLATILE: the runtime they target — each one's `.runtimeconfig.json` in `$TOOLING`).
The Windows x64 zip of that runtime unpacks straight into the prefix, and their launchers find it there with no installer:

```bash
curl -LO https://builds.dotnet.microsoft.com/dotnet/Runtime/6.0.36/dotnet-runtime-6.0.36-win-x64.zip
mkdir -p "$PFX/drive_c/Program Files/dotnet"
unzip -o dotnet-runtime-6.0.36-win-x64.zip -d "$PFX/drive_c/Program Files/dotnet"
```

### 3. Windows Unity editor

The post-processor drives the IL post-processor runner of the Windows Unity editor the toolchain pins, locating it through the registry.
The pinned version is the toolchain's editor, read off its Unity mod project — **not** the game's own engine version, which the debug patch needs and which usually differs:

```bash
unzip -p "$TOOLING/UnityModsProject.zip" '*ProjectVersion.txt'
# m_EditorVersionWithRevision: <version> (<changeset>)
UNITY=<version>
CHANGESET=<changeset>
```

Install it silently into the prefix, the way the in-game installer does; the [Unity download archive](https://unity.com/releases/editor/archive) lists every version with its changeset.
This editor is never opened, so it needs no license.
The installer wants `/D=` last and unquoted though the path holds spaces, and Wine quotes any argument containing one, so only a raw `cmd` command line preserves it.
It runs for several minutes and prints nothing:

```bash
curl -LO "https://download.unity3d.com/download_unity/$CHANGESET/Windows64EditorInstaller/UnitySetup64-$UNITY.exe"
wine cmd /c "$(winpath "$PWD/UnitySetup64-$UNITY.exe") /S /D=C:\\Program Files\\Unity $UNITY"
WINEPREFIX="$PFX" "$PROTON/files/bin/wineserver" -w   # waits for the installer to finish
```

The post-processor reads two registry values: the installer's own key, and the prefix's copy of the user environment variable naming the version.

```bash
wine reg add "HKLM\\SOFTWARE\\Unity Technologies\\Installer\\Unity $UNITY" /v "Location x64" /t REG_SZ /d "C:\\Program Files\\Unity $UNITY" /f
wine reg add 'HKCU\Environment' /v CSII_UNITYVERSION /t REG_SZ /d "$UNITY" /f
```

### 4. Unity mod project

The build reads the Entities source generators and the compiled IL post-processors from the Unity mod project's `Library` folder, which appears only once a licensed editor has opened the project.
Use the **Linux** editor of the same version: install it from Unity Hub (Installs → Install Editor → Archive), or unpack it where Unity Hub keeps its editors:

```bash
curl -LO "https://download.unity3d.com/download_unity/$CHANGESET/LinuxEditorInstaller/Unity-$UNITY.tar.xz"
mkdir -p "$HOME/Unity/Hub/Editor/$UNITY"
tar -xJf "Unity-$UNITY.tar.xz" -C "$HOME/Unity/Hub/Editor/$UNITY"
```

Unpack the project and open it once in batch mode.
`unzip` warns that the archive uses backslashes as path separators, and extracts it correctly anyway.

```bash
rm -rf "$TOOLPATH/UnityModsProject"
unzip -q "$TOOLING/UnityModsProject.zip" -d "$TOOLPATH/UnityModsProject"
"$HOME/Unity/Hub/Editor/$UNITY/Editor/Unity" -batchmode -nographics -quit \
  -projectPath "$TOOLPATH/UnityModsProject" -logFile -
```

The Flatpak Unity Hub keeps its license inside its sandbox, so with it the editor has to run there:

```bash
flatpak run --command="$HOME/Unity/Hub/Editor/$UNITY/Editor/Unity" com.unity.UnityHub \
  -batchmode -nographics -quit -projectPath "$TOOLPATH/UnityModsProject" -logFile -
```

Done when `Library/ScriptAssemblies` holds `Unity.Entities.CodeGen.dll`.
The Entities version the environment file asks for is the suffix of the `com.unity.entities@…` folder in `Library/PackageCache`.

### 5. Wrappers for the Windows-only programs

The build runs the post-processor, and publishing runs the publisher, as plain commands with Linux path arguments.
Save this script next to each `.exe`, as `$TOOLPATH/ModPostProcessor/ModPostProcessor.sh` and `$TOOLPATH/ModPublisher/ModPublisher.sh`, make both executable, and point each one's `proton` and `WINEPREFIX` lines at the user's own.
Each copy runs the `.exe` named like itself.

```bash
#!/usr/bin/env bash
# Runs the Windows tool of the same name as this script (ModPostProcessor.exe or ModPublisher.exe,
# next to it) under the game's Proton, with Linux path arguments translated to the Z: drive.
set -euo pipefail

# The Proton build and prefix the game itself uses.
proton="$HOME/.local/share/Steam/steamapps/common/Proton - Experimental"
export WINEPREFIX="$HOME/.local/share/Steam/steamapps/compatdata/949230/pfx" WINEDEBUG=-all

# Wine hands the Linux environment to Windows processes, and the Windows .NET host would follow the
# Linux SDK's DOTNET_ROOT into a runtime it cannot load.
unset "${!DOTNET_@}" "${!MSBuild@}" "${!MSBUILD@}"

args=()
for arg in "$@"; do
  [[ $arg == /* ]] && arg="Z:${arg//\//\\}"
  args+=("$arg")
done

# MSBuild reports every stderr line as a build error, so drop Proton's wineserver startup notice.
exec "$proton/files/bin/wine" "${0%.sh}.exe" "${args[@]}" 2> >(grep -v '^wineserver: ' >&2)
```

Both comments name a failure the script exists to prevent, so keep both lines when adapting it.

### 6. Environment variables

On Windows the installer writes these as user environment variables.
Their Linux counterpart is a file in `~/.config/environment.d/`, which every program of the desktop session inherits, IDEs included.
Write `~/.config/environment.d/60-cs2-modding.conf`, with the first two paths adjusted to the user's Steam library and the two versions read in the Windows-editor and Unity-project steps:

```ini
CSII_INSTALLATIONPATH="${HOME}/.local/share/Steam/steamapps/common/Cities Skylines II"
CSII_USERDATAPATH="${HOME}/.local/share/Steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II"
CSII_MANAGEDPATH="${CSII_INSTALLATIONPATH}/Cities2_Data/Managed"
CSII_MSCORLIBPATH="${CSII_MANAGEDPATH}/mscorlib.dll"
CSII_LOCALMODSPATH="${CSII_USERDATAPATH}/Mods"
CSII_TOOLPATH="${CSII_USERDATAPATH}/.cache/Modding"
CSII_UNITYMODPROJECTPATH="${CSII_TOOLPATH}/UnityModsProject"
CSII_MODPOSTPROCESSORPATH="${CSII_TOOLPATH}/ModPostProcessor/ModPostProcessor.sh"
CSII_MODPUBLISHERPATH="${CSII_TOOLPATH}/ModPublisher/ModPublisher.sh"
CSII_UNITYVERSION="<the toolchain's Unity version>"
CSII_ENTITIESVERSION="<the Entities package version>"
```

**The file reaches programs only after the user logs out and back in**, so tell them to, and to restart any IDE opened before.
A shell can load it at once with `set -a; . ~/.config/environment.d/60-cs2-modding.conf; set +a`.

### 7. Project changes

**C# projects.** The mod template imports the toolchain files through the user-scope read.
Replace the two `Import` lines of the `.csproj` with this, which behaves the same on Windows:

```xml
<PropertyGroup>
	<CSIIToolPath>$([System.Environment]::GetEnvironmentVariable('CSII_TOOLPATH', 'EnvironmentVariableTarget.User'))</CSIIToolPath>
	<CSIIToolPath Condition="'$(CSIIToolPath)' == ''">$(CSII_TOOLPATH)</CSIIToolPath>
</PropertyGroup>

<!--Imports must be after PropertyGroup block-->
<Import Project="$(CSIIToolPath)\Mod.props" />
<Import Project="$(CSIIToolPath)\Mod.targets" />
```

**The mod template.** Install it natively from the game's copy, after which `dotnet new csiimod` works as on Windows:

```bash
dotnet new install "$TOOLING"/ColossalOrder.ModTemplate.*.nupkg
```

**The UI scaffold.** The installer makes `npx create-csii-ui-mod` resolve by running `npm link` inside `$TOOLING/npx-create-csii-ui-mod`; run the same.

**UI mods.** The scaffold's `webpack.config.js` builds its deploy folder with Windows separators, which on Linux creates one folder whose name contains the backslashes, and the game never sees the UI half.
Replace

```javascript
const OUTPUT_DIR = `${CSII_USERDATAPATH}\\Mods\\${MOD.id}`;
```

with

```javascript
const OUTPUT_DIR = path.join(CSII_USERDATAPATH, "Mods", MOD.id);
```

`npm run update` overwrites `webpack.config.js`, so re-apply this after every update.

## Building, testing and publishing

Build as on Windows, from the IDE or with `dotnet build`.
The output shows the post-processor running under Proton — `Burst dll compiled for Windows`, then `processing complete` — and the mod copied to `$CSII_LOCALMODSPATH`, where the game lists it as a local mod on its next start.

Off-engine unit tests target `net48`, and on Linux `dotnet test` runs them on Mono.
They need `mono-devel`, not just `mono-runtime`: without its framework facades the test host crashes before running a single test.
Mono does not raise `AppDomain.AssemblyResolve` for a reference it meets while loading a type — a mod type implementing a game interface, for instance — and caches the failure, so a resolver that loads game assemblies on demand misses exactly those.
Make the game references copy-local on Linux instead, `<Private Condition="'$(OS)' == 'Windows_NT'">false</Private>`, which copies them beside the tests with the non-framework dependencies MSBuild finds next to them.
Pointing `MONO_PATH` at the game's managed folder makes Mono load the game's own `mscorlib` and abort.

`dotnet publish` with a publish profile runs the publisher through its wrapper, and its automatic sign-in finds the game's login with no `CSII_PDXCACHEPATH` set.

## After a game update

- Redo the toolchain files.
- When `UnityModsProject.zip` changed, redo the Windows Unity editor and the Unity mod project for its Unity version, and update both versions in the environment file.
- Re-apply the debug patch, if the user has it.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `NETSDK1013: The TargetFramework value '' was not recognized` | The project reads `CSII_TOOLPATH` through the user scope, so it never imports `Mod.props`. | The C# project change. |
| `User environment variable 'CSII_…' has incorrect path(s)` | The variables are not in the build's environment, or `Mod.props` is the unpatched copy. | The toolchain files and the environment file, then log out and back in. |
| `the required library hostfxr.dll could not be found in [Z:\…]` | Wine handed the Linux SDK's `DOTNET_ROOT` to the Windows post-processor. | The wrapper, which unsets it. |
| A build error reading `wineserver: using server-side synchronization.` | MSBuild reports the post-processor's stderr as errors. | The wrapper, which filters that line. |
| `Modding toolchain is incomplete, please reinstall it` | The post-processor cannot find the Unity editor, its version, or the Unity mod project's packages. | The Windows editor's registry values, then the Unity mod project. |
| The UI bundle deploys to a folder named `Cities Skylines II\Mods\…` | The UI scaffold's Windows path separators. | The UI mods change. |
| `net48` tests abort with `Test host process crashed` | Mono lacks the framework facades the test host loads. | Install `mono-devel`. |
