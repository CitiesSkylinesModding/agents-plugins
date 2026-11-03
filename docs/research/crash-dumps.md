# Crash dumps: reading a crashed session's minidump with no symbols, and telling a crash from a hang snapshot

**Baseline.** Game version 1.6.2f1 (767.21d1, `src/Game/Properties/AssemblyInfo.cs:19`), Unity 2022.3.71f1, Linux under Proton, decompile at HEAD `43da1009`, read 2026-11-02 at `~/Documents/Dev/cs2-decompile`.
Mod corpus at `~/Documents/Dev/cs2-mods`, read 2026-11-02. `cs2-dev` read at `9497679`.
`minidump-stackwalk 0.27.0` and `dump_syms 2.3.9`, both from crates.io through `cargo install` (`~/.cargo/.crates.toml:11/9`, `minidump-stackwalk-0.27.0/README.md:14-17`); `mise` only shims the binaries in `~/.cargo/bin`.
Crashpad sources read 2026-11-02 from `backtrace-labs/crashpad` branch `backtrace` (pushed 2026-10-26) and compared with `chromium/crashpad` `main`, cited as `crashpad/<path>:<line>` at the fork's lines. The shipped handler's source tag is unknown, so what matters was re-checked in the shipped binary (finding 2).

Every dump named here lives on one machine, one install, under Proton; a claim backed by dumps states its count, and the shipped reference says "in a captured run" for any of them.
`<user data>` is `%CSII_USERDATAPATH%`. `<crashpad>` is `<user data>/.cache/backtrace/crashpad`. Reports are named by the first eight characters of their UUID.

**The set.** 27 files, 23 distinct dumps: four of the files are copies of another (same name or same size and fields: `7a787f3c` in the live folder and among ticket 01's kept dumps; the three dumps of the lane-cull bug report are `71a61138`, `aa0f97fe`, `711f9633` with the environment blanked). Counts below are of the 23.

| Shape | Distinct dumps | Known cause |
|---|---|---|
| Hang snapshot | 11 | The watchdog, by construction |
| Real crash, access violation | 11 | Heap overrun in `TerrainSystem`, diagnosed and reported by the maintainer |
| Real crash, breakpoint | 1 | A Burst job told to throw, the error survey's `job-burst` case: `new BurstThrowingJob().Schedule().Complete()` |
| V8 crash | 0 kept | One stackwalk output survives in a session transcript, no dump (finding 12) |

No dump on this machine is a Mono crash, a graphics or driver crash, or a crash on Windows.

---

## Findings

### 1. Where dumps live, and what sits beside them

**The folder.** `<crashpad>/reports/<uuid>.dmp`. On this machine `<user data>` is `<Steam library>/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II`. The path is what `plugins/cs2-modding/mcp/src/hint.ts:56-58` builds, and the database is the `crashpad` child of the client's database path (`src/Backtrace.Unity/Backtrace.Unity.Model/BacktraceConfiguration.cs:145-155`). The parent path itself comes from the configuration asset, so C# does not state it.

On Windows the folder is `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\backtrace\crashpad\reports\`, listed in ticket 10's run (`game-cycle.md`, section 8).

**The name.** `<uuid>.dmp`, where the UUID is the crashpad report id: the first `metadata` record's bytes decode to `7a787f3c-42f6-4cbf-b551-4149e8a4f523`, the file name. The dump's date is in its mtime; the name carries none.

**Flat folder.** The Windows database has no `pending/`, `completed/` or `new/` folder. State lives in `metadata`. Listing of `<crashpad>`: `attachments/`, `metadata`, `reports/`, `settings.dat`; the strings `reports`, `metadata`, `settings.dat` and `attachments` sit in `crashpad_handler.dll` and `BacktraceCrashpadWindows.dll`.

**`metadata`.** Binary, header `CPAD` (bytes `44 41 50 43`), version 1, then 56-byte records, then a string table (`crashpad/client/crash_report_database_win.cc:106-113` and the record struct below it). Record: UUID, file-name index, id index, `creation_time`, `last_upload_attempt_time`, `upload_attempts`, `state`, `attributes` (bit 0 uploaded). `state` is 0 pending, 1 uploading, 2 completed. A `{"response":"ok","_rxid":"…"}` string in the table is the server's answer and is the record's id. The receipt id is of no use to a reader: leave it out of reports.

Decoded on 2026-11-02:

| Report | `creation_time` | Last attempt | Attempts | State | Uploaded | Id |
|---|---|---|---|---|---|---|
| `7a787f3c` | 2026-10-29 20:05:39 | none | 0 | 1 uploading | 0 | empty |
| `a7062766` | 2026-10-31 09:00:15 | none | 0 | 1 uploading | 0 | empty |
| `51339fa6` | 2026-11-02 14:42:53 | 14:42:54 | 1 | 2 completed | 1 | `ok` |
| `e89f11dd` | 2026-11-02 16:53:24 | 16:53:26 | 1 | 2 completed | 1 | `ok` |

A dump's mtime equals its `creation_time` to the second in all four. Decoding method: `struct` over the header, `<IIqq` at offset 16 of each record, `<4I` at offset 40, string table after the last record.

**`settings.dat`.** 40 bytes, magic `sdPC`, version 1, options 1, `last_upload_attempt_time` (16:53:26 here, matching the last record), then the client UUID. Its mtime (18:17:58) is the last boot's first log stamp, one observation, and later than its content, so it says nothing of an upload. Nothing a reader of a dump needs.

**`attachments/`.** Empty, mtime 2026-09-10, the day the database was created: no report on this install has ever carried an attachment. `Player.log` is therefore not in the crashpad report: crashpad's handler copies each attachment into `attachments/<uuid>/` at report time (`handler/win/crash_report_exception_handler.cc:115-132` on `backtrace-labs/crashpad`'s `backtrace` branch, whose head is not proven to be the shipped build), so a report with one would have left a folder there. The dump is the whole record.

**Rots:** the crashpad folder layout and `metadata` format, both the handler's; re-check against `Cities2_Data/Plugins/x86_64/crashpad_handler.dll` strings and a decode of `metadata`.

### 2. How long a dump lasts

**No fixed lifetime exists, and none ships.**

Verdict: "Crashpad prunes them within the hour" (`gfd-game-cycle/SKILL.md:44`) and "within minutes" are wrong as statements of how long a report lasts. The maintainer reports (2026-11-02, first-hand on this install) that crash reports are sometimes days old or more. The folder agrees: `7a787f3c` is dated Oct 29 and `a7062766` Oct 31, on Nov 2. What wins is the mechanism below, settled by the prune run. Same verdict on the overrun's write-up, "that is also why dumps vanish within the hour": uploading removes nothing (below).

**What prunes.** The crashpad handler, on a timer of its own, in a run. Nothing else touches `reports/`.

- `crashpad/handler/prune_crash_reports_thread.cc:26` repeats every 24 h, `:33` starts 600 s after the thread starts, `:41` first runs `CleanDatabase` with a 3-day lockfile age, then prunes. The thread starts with the handler unless `--no-periodic-tasks` is passed (`crashpad/handler/handler_main.cc:1103-1105`).
- `crashpad/client/prune_crash_reports.cc:81-82`: `DatabaseSizePruneCondition(1024 * 128)` OR `AgePruneCondition(365)`. The size is in KiB, so 128 MiB (134,217,728 bytes), measured cumulatively over reports newest first (`:49`, `:107-109`), with a report's size its file plus its attachments (`crash_report_database_win.cc:504-507`).
- Only pending and completed reports are considered (`prune_crash_reports.cc:34`, `:41`; `crash_report_database_win.cc:785`, `:794`).
- A report left in state 1 is in neither list, and `CleanDatabase` keeps any report whose file exists: nothing in `crash_report_database_win.cc` resets it. `state` reaches disk through `Metadata::~Metadata` (`:310-312`), so one taken for upload (`:812`) stays state 1 if the handler dies before `RecordUploadAttempt`.

**The shipped binary holds the same rule.** `crashpad_handler.dll` carries RTTI for `PruneCrashReportThread` and its `--no-periodic-tasks` help text; `objdump -d` shows `mov QWORD PTR [rbx+0x8],0x20000` (128 MiB in KiB), `lea rcx,[rax-0x1e13380]` (365 days in seconds) directly before it, and `mov edx,0x3f480` (3 days) in the prune thread's work function. `0x258` (600) appears as well, at four sites; the prune run below shows the timing it sets. `86400.0` sits beside `600.0` in its data, at `0x14006b590` and `0x14006b588`, each with a load in code. `BacktraceCrashpadWindows.dll` holds the strings `--no-rate-limit` and `--attachment=` among its handler arguments and not `--no-periodic-tasks`.

**What the rule predicts, against this machine.**

| Prediction | Evidence | Fits |
|---|---|---|
| Runs under 10 minutes prune nothing | Ticket 01's runs under 10 minutes removed none; its listings of `reports/` show reports only accumulating across the 16:00-16:33 launches | Yes |
| A run past 10 minutes empties the folder of pending and completed reports over the budget | At 11:53:27, about 10 minutes into launch 2, three reports went, launch 2's own boot report among them; the listing before lists `a70ca282`, `bd7329bc`, `7a787f3c`, the listing after only `7a787f3c` | Yes |
| The survivor is a state 1 report, of any age | The survivor of that event is `7a787f3c`, state 1 today and the one report of eight without `ok` in ticket 01's reading of `metadata` | Yes |
| Almost no dump survives a prune alone | 21 of 23 dumps exceed 134,217,728 bytes (130,265,864 to 149,094,472), so each exhausts the budget by itself; the two below it are `8782e9a0` and `98bb4b09`. `7a787f3c` at 135,453,432 bytes is over budget and stayed | Yes |
| Reports stay for days when no run reaches the timer | `51339fa6` (completed, 137,554,120 bytes) stood after runs at 14:53, 14:55, 16:53, 16:55 and 18:17, all short. The maintainer's report | Yes, nothing contradicts it |

**The prune run, 2026-11-02, a captured run.** With two state 1 and two state 2 reports in `reports/`, the game went to the main menu with no save and sat there. The handler's process started at 20:01:46 (`ps` etime). A 5 s poll of `reports/` logged all four until 20:11:47, 601 s after that start, then only `7a787f3c` and `a7062766`, both state 1: `51339fa6` and `e89f11dd`, both state 2 and over the budget, went in the one poll, and their records left `metadata`. The handler lived on to the stop at 20:14. Any run alive past that point would do: the handler's own start sets the clock, not the game's state.

So the rule is: **a report stands until a run reaches about 10 minutes with it already on disk**, the prune firing once at about 600 s and then daily, a report stuck in state 1 outlasts that, and the prune can take the run's own earlier reports. A real crash ends its run, so its dump waits for the next run long enough to prune. No duration ships as how long a report lasts: it lasts days when runs are short, and ends 10 minutes into the next run that is not.

Unconfirmed: how a report comes to rest in state 1. Both here have no attempt recorded, while the two completed ones took 1 to 2 s. `7a787f3c` was written 294 s into its run and the next report on record, `bd7329bc`, came 3 m 58 s later; `a7062766` 88 s into its run. A handler dying mid-upload is the only path in the source, and the logs of those runs no longer exist. Watching `metadata` across a `stop` soon after a report settles it.

Unconfirmed: whether a state 1 report is ever removed. The network-tools note cites a hang dump of 2026-09-18 with "uploaded 0, attempts 0" (`game-dies-lane-cull-overflow.md:37`), a record shaped like these, and no such record is in `metadata` today. Removal by hand is not excluded and the note's "for a month" cannot be right for a note dated 2026-09-23, so it is a lead only.

**Does uploading remove a dump? No.** `51339fa6` and `e89f11dd` are uploaded (state 2, `ok`) and on disk. `RecordUploadAttempt` only changes state (`crash_report_database_win.cc:852`/`:855`). A failed upload is not retried on this platform: `crashpad/handler/crash_report_upload_thread.cc:254-276` has retries for iOS only and skips it elsewhere, and a skipped report is completed and prunable (`crash_report_database_win.cc:902`).

Verdict: the Backtrace client's database settings do not govern crashpad's folder. `MaxRecordCount` 8, `MaxDatabaseSize` 0, `RetryLimit` 3 (`BacktraceConfiguration.cs:29/31/21`) belong to the managed offline database of `*-record.json` files (`src/Backtrace.Unity/Backtrace.Unity.Services/BacktraceDatabaseFileContext.cs:25`), whose file walk is `GetFiles()` on the database directory alone, non-recursive (`:78`), two levels above `reports/`. The tooltip's "will remove all existing files on database start" (`BacktraceConfiguration.cs:108`, its field `DatabasePath`) binds that level. The match between `DefaultMaxRecordCount = 8` and ticket 01's eight records is a coincidence.

**A second folder, empty here.** `SendMinidumpOnStartup` (`src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Windows/NativeClient.cs:178-187`) walks `Temp/<company>/<product>/crashes`, Unity's own crash folder, a second place a dump could be. `AppData/Local/Temp/Colossal Order/Cities Skylines II/` exists in the prefix and holds no `crashes` folder, and no `*crash*` file sits under the user profile outside the backtrace folder except mods' own. Under Proton the crashpad folder is the only one.

**Rots:** the prune constants, the 24 h period among them, in `crashpad_handler.dll`; re-check by `objdump` as above, since the install does not state a crashpad version.

### 3. What the `cs2-dev` evidence copy keeps

`keepPreviousRun` copies `Logs/`, `Player.log` and `crashpad/reports` holding the reports with mtime at or after the run's start, told from the logs (`plugins/cs2-modding/mcp/src/evidence.ts:23-53`, `:79-107`), after the stop and before the launch, one generation.

What it misses:

- **`metadata` and `settings.dat`.** The copy cannot say whether a report uploaded or is stuck.
- **The date, a log's as much as a report's.** `copyFile` and `cp` carry no mtime (a file dated 2020 came out dated now through each), so everything in `previous-run/` is dated by the copy, its reports after its logs. `Process uptime` inside the dump and the crashing run's logs still place a report.
- **A report of an earlier run.** A real crash's dump in run A is in the copy only until the second `restart` or `test`; in `reports/` it stands until a long run prunes it, which can be longer than the copy lasts. So the copy is not always the older of the two records.
- **A report pruned within the run.** A boot snapshot in a run past the first prune is gone before the stop.
- **An empty run.** `previous-run/crashpad/reports/` is empty today: the run of 16:55 wrote none, and that is a true record.

Size: each dump is 130 to 149 MB. The copy doubles it on disk, one generation's worth.

### 4. Reading one with no symbols

`minidump-stackwalk` is the Rust crate from rust-minidump, `cargo install minidump-stackwalk`; `--version` prints `minidump-stackwalk 0.27.0`.

```bash
minidump-stackwalk --no-color --brief <dump>            # verdict and crashing thread
minidump-stackwalk --no-color <dump> > out.txt          # every thread, then the module list
minidump-stackwalk --no-color --json --pretty <dump>    # the same as data, worth it for queries
```

**Cost.** 0.04 to 0.14 s of wall time on all 27 files, 130 to 149 MB. `--brief`: 29 lines for a snapshot, 224 for a crash on the main thread. Full: 11,715 to 14,165 lines. JSON: about 1 MB. Set `--no-color` when piping.

**No network by default.** `strace -f -e trace=connect,socket` on a default run shows zero `connect` or `socket` calls (one dump, `e89f11dd`). The tool reaches a server only when given `--symbols-url`. Nothing to turn off. Its `--help` lists Microsoft's and Mozilla's servers only as examples.

`--brief`, a snapshot, trimmed of registers:

```
Operating system: Windows NT
                  10.0.19045 5796
CPU: amd64
     family 6 model 186 stepping 2
     20 CPUs

Crash reason:  Simulated Exception
Crash address: 0x00006ffff8572dcd
Process uptime: 44 seconds

Thread 80  (crashed) - tid: 756
 0  BacktraceCrashpadWindows.dll + 0x2dcd
    Found by: given as instruction pointer in context
 1  BacktraceCrashpadWindows.dll + 0x1b707
    Found by: stack scanning
 2  BacktraceCrashpadWindows.dll + 0x2dcc
    Found by: stack scanning
```

`--brief`, a real crash, trimmed:

```
Crash reason:  EXCEPTION_ACCESS_VIOLATION_READ
Crash address: 0xffffffffffffffff
Process uptime: 103 seconds

Thread 0  (crashed) - tid: 312
 0  UnityPlayer.dll + 0x1ed5d52
    Found by: given as instruction pointer in context
 1  UnityPlayer.dll + 0x1ed61c3
    Found by: stack scanning
 2  UnityPlayer.dll + 0x4f3732
    Found by: stack scanning
```

**The full output.** The crashing thread comes first and the others follow by index, so the first `Thread` heading is not Thread 0 in a snapshot: `Thread 80 (crashed)`, then `Thread 0`, `Thread 1`, and 81 follows 79. Then `Loaded modules:` (`0x140000000 - 0x1400a7fff  Cities2.exe  2022.3.71.48915  (main)`) and `Unloaded modules:`. `grep '^Thread' out.txt` lists every thread and its name; `sed -n '/^Thread 0 /,/^Thread 1 /p' out.txt` cuts the main thread's stack in a snapshot (Thread 1 is the next one printed unless it is the crashing one).

**The JSON** gives `crash_info.type`, `.address`, `.crashing_thread`, `threads[].thread_name`, `threads[].frames[].{module,module_offset,trust}`, `modules[].{filename,debug_file,debug_id,version}`. It holds no annotations. Tested:

```bash
jq -r '.crash_info|"\(.type) \(.address) \(.crashing_thread)"' out.json
jq -r '.threads[.crash_info.crashing_thread].frames[0]|"\(.module) \(.module_offset) \(.trust)"' out.json
```

**Annotations** come only from the raw view:

```bash
minidump-stackwalk --dump --brief <dump> | grep -E 'thread\.main|_mod_faulting_tid|error\.(type|message)'
```

```
  simple_annotations["thread.main"] = 312
  module_list[0].simple_annotations["_mod_faulting_tid"] = 312
  module_list[0].simple_annotations["error.message"] = ANRException: Blocked thread detected.
  module_list[0].simple_annotations["error.type"] = Hang
```

Always filter that view: the same block holds `hostname`, `device.name`, `guid`, `application.data_path` and the like. The block's keys, all 27 files: `_mod_faulting_tid`, `api.compatibility`, `application*`, `atl.version`, `audio.supported`, `backtrace.version`, `cohtml.version`, `cpu.*`, `device.*`, `error.message`, `error.type`, `format`, `game.assetmods`, `game.codemods`, `game.moddingRuntime`, `game.version`, `graphic.*`, `guid`, `hostname`, `pdxsdk.version`, `scripting.backend`, `thread.main`, `uname.*`, `vm.rss.size`. Only names were read for this file, plus the four values above.

**What it prints for PE modules under Proton.** Wine's own DLLs are listed as ordinary modules (`ntdll.dll` 6.1.7601.24059, `kernelbase.dll`, `winex11.drv`, `winevulkan.dll`, `winepulse.drv`, `winealsa.drv`; `wineopenxr.dll` among the unloaded). Their debug ids are all zeros. `Operating system` reads Windows NT 10.0.19045 under Proton: it is what Wine reports, so it cannot tell Proton from Windows, and `winex11.drv` or a thread named `wine_*` can.

**Do frames unwind? Barely, with no symbols.** Across all threads of the 27 files: 93,776 frames found by `scan`, 3,616 by `context`, 2,564 by `frame_pointer`, none by `cfi`. Frame 0 (`given as instruction pointer in context`) is the only one to trust. A `stack scanning` frame is any stack word that points into a module's code, stale return addresses and all: frames 4 and 7 of `aa0f97fe`'s main thread are the same address, and unsymbolised `mono-2.0-bdwgc.dll` shows 47,766 frames against 20,160 for the engine, most of them noise. The next finding removes it.

### 5. Symbols from what ships: a real stack with one extra tool

`dump_syms` (crates.io, `dump_syms 2.3.9`) reads a PE alone and emits `STACK` records from its unwind tables, with no PDB, which gives `minidump-stackwalk` real call frame info. Output counts for what the install ships:

| File | `STACK` | `PUBLIC` |
|---|---|---|
| `UnityPlayer.dll` | 137,856 | 1 |
| `MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll` | 18,932 | 520 |
| `Cities2_Data/Plugins/x86_64/v8.dll` | 61,427 | 21,133 |
| `v8_libbase.dll` | 591 | 566 |
| `v8_libplatform.dll` | 541 | 196 |
| `cohtml.WindowsDesktop.dll` | 21,714 | 122 |
| `cohtml_Unity3DPlugin.dll` | 2,414 | 392 |
| `lib_burst_generated.dll` | 56,209 | 1 |

`PUBLIC` here is the DLL's export table, so `v8.dll`, cohtml and Mono come out with partial names from the install alone.

**Names for the engine and Mono come from Unity's symbol server, by the name and id the dump states** (`modules[].debug_file`, `.debug_id`):

```bash
curl -A 'Microsoft-Symbol-Server/10.0' -o <pdb> \
  https://symbolserver.unity3d.com/<pdb>/<debug_id>/<pdb>
```

Tested 2026-11-02, all 200: `UnityPlayer_Win64_player_development_mono_x64.pdb` / `707317FD687D48A6AB705738D28F39E11` (53 MB), `UnityPlayer_Win64_player_mono_x64.pdb` / `8A02748B82224263AB52CA5DFBEB414F1` (21 MB), `mono-2.0-bdwgc.pdb` / `524A5DAAA02A4F5880CB67BE070D6ECD1` (27 MB). The header's user agent is the one the source note used; other agents were not tried. Stripped: `FUNC` plus `PUBLIC`, no line numbers for the engine.

**The two make one symbol file.** With the DLL and the PDB in one folder, `dump_syms UnityPlayer.dll` writes both (137,856 `STACK`, 82,751 `PUBLIC`, 24,975 `FUNC`). The stackwalker's folder layout is `<root>/<debug_file>/<debug_id>/<stem>.sym`:

```bash
read -r _ _ _ id file < x.sym             # MODULE windows x86_64 <id> <debug_file>
mkdir -p "syms/$file/$id"; mv -f x.sym "syms/$file/$id/${file%.pdb}.sym"
minidump-stackwalk --no-color --brief --symbols-path syms <dump>
```

Result on `aa0f97fe`, 0.16 s, no `connect` call:

```
 0  UnityPlayer.dll!block_remove + 0x72
    Found by: given as instruction pointer in context
 1  UnityPlayer.dll!tlsf_free + 0x83
    Found by: call frame info
 2  UnityPlayer.dll!DynamicHeapAllocator::Deallocate(void*) + 0x1b3
 3  UnityPlayer.dll!DualThreadAllocator<DynamicHeapAllocator>::TryDeallocate(void*) + 0x13f
 4  UnityPlayer.dll!MemoryManager::Deallocate(void*, MemLabelId const&, char const*, int) + 0x4d7
 5  UnityPlayer.dll!free_alloc_internal(void*, MemLabelId const&, char const*, int) + 0x1d
 6  UnityPlayer.dll!UnsafeUtility::Free + 0x199
 7  UnityPlayer.dll!UnsafeUtility_CUSTOM_FreeTracked(void*, NativeCollection::Allocator) + 0xb0
 8  0x73ddcb3b
    Found by: call frame info
 9  0x6fadfa12
    Found by: previous frame's frame pointer
```

Same dump, without symbols: 30 frames of scan noise in the first screen. Trust over that dump's threads becomes `cfi` 533, `frame_pointer` 509, `scan` 695. A PDB alone, without the DLL's `STACK` records, names the frames and keeps the scan, so `<unknown in …pdb>` lines and false frames stay.

Verdict: `minidump-stackwalk`'s `--symbols-url https://symbolserver.unity3d.com/` does not work: the server holds PDBs, the tool asks for Breakpad `.sym`, the cache stayed empty, and the output stayed unsymbolised. Build the `.sym` by hand.

Trap: **the file must be the build that crashed.** Match the DLL by `code_id`. `UnityPlayer.dll` on this install is the debug-patched development player (code id `6958595f36e2000`, debug file `UnityPlayer_Win64_player_development_mono_x64.pdb`), and 26 of 27 files ran it. `51339fa6` ran the stock release player (`UnityPlayer_Win64_player_mono_x64.pdb`), for which the install no longer held a DLL, so its stack got names and no unwinding. An offset means nothing across the two. `debug_file` is how a dump says which it was.

Trap: **a rebuilt mod no longer matches.** Local mods keep `<Mod>_win_x86_64.pdb` beside the DLL, and `dump_syms` there gives C# job names (`Unity.Jobs.IJobExtensions.JobStruct`1<ErrorSurvey.BurstThrowingJob>.Execute(…)`), so a mod's Burst frames can be named. The ErrorSurvey PDBs on disk have ids `3D0B9B07…` and `6D0436DC…` while `51339fa6` loaded `B6100061…`: the build it ran was overwritten. Keep the crashing build's Burst PDB to use this.

**Burst library symbols.** `lib_burst_generated.dll`: 1 `PUBLIC`, no PDB shipped (no `*.pdb` anywhere in the install) and none looked for on Unity's server, as the id is this game's own build. It unwinds and does not name.

**Rots:** the debug ids and code ids, per game and Unity version; read them from the dump each time. The Unity symbol server URL and user agent. `dump_syms` and `minidump-stackwalk` flags.

### 6. Telling frames apart by module

| Frame 0 module | Layer | Evidence |
|---|---|---|
| `BacktraceCrashpadWindows.dll` | The watchdog | 11 of 11 snapshots, offset `+0x2dcd` |
| `UnityPlayer.dll` | Engine, native allocator included | 11 access violations, 1 breakpoint |
| `v8.dll`, `v8_libbase.dll`, `v8_libplatform.dll` | The UI's JavaScript engine | The one transcript stackwalk (finding 12); no frame in any of the 27 files, including by scan |
| `cohtml.WindowsDesktop.dll`, `cohtml_Unity3DPlugin.dll` | The UI middleware | 2 and 134 frames, all on unnamed threads, never a crash here |
| `mono-2.0-bdwgc.dll` | The Mono runtime | Top frame of 4 threads in snapshots; never a crash here |
| No module, a bare address such as `0x78626d10` | Mono JIT code, so managed code | Above `UnsafeUtility_CUSTOM_FreeTracked`, a managed-to-native call boundary, in `aa0f97fe`; main thread frames of `e89f11dd` |
| `lib_burst_generated.dll` | The game's Burst code | Top frame of 2 worker threads; frames on `Job.Worker 1` and `Job.Worker 10` |
| `<Mod>_win_x86_64.dll` | A mod's Burst code | `ErrorSurvey_win_x86_64.dll + 0xfff` by scan in `51339fa6` |
| `d3d11.dll`, `dxgi.dll` under Proton | DXVK | 5,444 frames, on `dxvk-*` and `UnityGfxDeviceWorker` threads; `version` 10.0.17763.1, debug id zeros |
| `winevulkan.dll` | Wine's Vulkan layer | `dxvk-*` and `UnityGfxDeviceWorker` threads |
| `ntdll.dll` | A thread at rest | Top frame of 3,504 threads. On the main thread it is a wait |

Evidence is frame 0 only where the table says so; the point stands that a module anywhere below frame 0 without `cfi` or `context` trust proves little.

Unconfirmed: what a real crash in the graphics driver looks like. No dump shows one. Under Proton a Linux driver is not a PE and is not in the module list, so a fault there may show a bare address; on Windows `d3d11.dll` is Microsoft's and the vendor's driver is a module of its own. `igd10iumd64.dll` (10 files), `nvapi64.dll` and `_nvngx.dll` (17) appear loaded and in no frame. A crash in a driver would settle it.

Unconfirmed: that a bare address is always JIT code. On Proton it could be Wine's Unix side too; no dump separates them. Mono's address ranges in a live process would.

Unconfirmed: what a crash in Mono itself looks like. None here.

**Recovering managed names.** No route from the dump.
Verdict: the idea that `Player.log`'s `(Mono JIT Code) Class:Method` lines name a dump's JIT frames fails. On `8782e9a0` with its own log, 45 such addresses against 15 module-less frames gave 0 exact hits; `aa0f97fe`'s log holds none. Those lines exist only for a stack the engine logged, and a real crash logs none (next item).
What does name the managed edge is the last native frame: `UnsafeUtility_CUSTOM_FreeTracked` names the icall the managed code made, which is what got the `FreeTracked` in the network-tools write-up.

**What `Player.log` adds.**

- The access violations: nothing. Each crash run's `Player.log` ends on shader upload lines with no crash text, and the mod's own log stops at its last step (last line `Sawtooth … apply`). A heap overrun is silent; `LANE CULL OVERFLOW …` in one such log is the mod's own probe.
- The Burst crash: the cause, whole. Survey run 11's `Player.log`, on the stock game with no PDB: `System.InvalidOperationException: Survey: a Burst job threw`, `This Exception was thrown from a job compiled with Burst…`, then a native stack that names the module (`errorsurvey_win_x86_64`). The dump adds only the thread. So **read `Player.log` first**; the dump is for when it is silent. Its native lines are absolute addresses, not module offsets.

### 7. Which thread is which

**Thread 0 is the main thread in every dump.** The `thread.main` annotation equals thread 0's id in 27 of 27 files, snapshots and crashes alike, with tids 312, 316 or 320 varying run to run.

Verdict: ticket 07's "the main thread being Thread 0 of the full output in a hang snapshot" holds, and it is not limited to snapshots.

The main thread has no name in the stackwalk's output: `Thread 0  - tid: 312`.

**The crashing thread.** In a snapshot, the watchdog: unnamed, `Thread 80` in 11 of 11 (a stable index in this game's boot, not a rule), with frame 0 in `BacktraceCrashpadWindows.dll`. Nothing in the name says watchdog; the module and the crash reason do. In a real crash, the thread that faulted: Thread 0 in 11 of 12, `Job.Worker 1` (thread 22) in the Burst crash.

**`--brief` on a snapshot shows nothing about the stall.** Read Thread 0 of the full output; `_mod_faulting_tid` names the same thread in all 11, which is what `setMainThreadAsFaultingThread: true` becomes (`NativeClient.cs:43/140`; the annotation is set in the native library, whose strings hold `_mod_faulting_tid` and `thread.main`, neither in the decompile). rust-minidump ignores it, so `--brief` keeps showing the watchdog. A symbolised Thread 0 reads as a cause: `8782e9a0` (579 s) waits in `GfxDeviceClient::WaitForPendingPresent` from `PlayerRender`, a main thread waiting on the render thread. Eleven snapshots, four top-frame families: `ntdll`, `UnityPlayer`, `mono-2.0-bdwgc`, bare JIT addresses.

**Named threads seen** (`grep '^Thread'`, `e89f11dd`): `Job.Worker N` 19, `Background Job.Worker N` 16, `Thread Pool Worker` 23, `AssetGarbageCollectorHelper` 19, `dxvk-shader-{n,h,l}`, `dxvk-submit`, `dxvk-queue`, `dxvk-descriptor`, `dxvk-cs`, `dxvk-frame`, `dxvk-cache`, `UnityGfxDeviceWorker`, `Loading.PreloadManager`, `Profiler.Dispatcher`, `Finalizer`, `HIDInput`, `Timer-Scheduler`, `audio_client_main`, `[GraniteSDK] Transcoder Thread`, `[GraniteSDK] Resolver Thread`, and `wine_*` threads under Proton.

**Rots:** thread names, Unity's and the game's; `_mod_faulting_tid` and `thread.main`, in `BacktraceCrashpadWindows.dll`.

### 8. What a `Simulated Exception` is, and the one-step test

**The mechanism.** `NativeClient.HandleAnr` (`NativeClient.cs:114-156`) runs a background thread that compares `LastUpdateTime` over two samples a sleep apart, and on no change calls `DumpWithoutCrash` once per stall (`:133-142`). `NativeReport` is `DumpWithoutCrash` in `BacktraceCrashpadWindows` (`:42-43`). The dump's exception code is `0x0517a7ed`, which rust-minidump prints as `Simulated Exception` (`minidump-common-0.27.0/src/errors/windows.rs:46`, `SIMULATED`). The process is alive and carries on. It is a stall report: the game did not crash.

`LastUpdateTime` comes from `Update(float)` (`src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Base/NativeClientBase.cs:49-51`), which `BacktraceClient.cs:471` feeds with `Time.unscaledTime` from the main thread, so any stall of the main loop past the sleep qualifies, a slow boot step above all.

Verdict: `AnrWatchdogTimeout = 15000` (`src/Colossal.Logging/Colossal.Logging.Backtrace/BacktraceHelper.cs:20`), taken for the game's effective value by ticket 01, does not reach the watchdog. The sleep reads a field copied from the configuration in the native client's constructor (`NativeClientBase.cs:46`, `NativeClient.cs:154`). The constructor runs in `BacktraceClient.Refresh` (`src/Backtrace.Unity/Backtrace.Unity/BacktraceClient.cs:383`), the pass that sets `_instance` (`:356`), and `SetDefaultAttributes` runs later, needing that instance (`src/Game/Game.SceneFlow/GameManager.cs:534`, `BacktraceHelper.cs:13-20`); a second `Refresh` returns at once (`BacktraceClient.cs:342`). Read by `unity` eval on the running game at the main menu, in the prune run: `BacktraceClient.Instance.Configuration.AnrWatchdogTimeout` is 15000 and `_nativeClient.AnrWatchdogTimeout` is 5000, so the watchdog sleeps 5 s. It compares two samples a sleep apart (`NativeClient.cs:125-142`, `:154`), so a stall of between about 5 and 10 s of the main loop fires it.

**Which ones the game writes.**

- At a boot: 9 of 9 launches in ticket 01, 38 to 44 s of uptime in the seven read.
- Verdict: "**Every boot does**" (ticket 01) is too strong. No prune has run since `51339fa6` was written, as it stands and is over budget, so `reports/` holds every report since. The runs the logs and the survey's captures show since are at 14:53, 14:55, 16:53, 16:55 and 18:17, and they wrote one, `e89f11dd`, 44 s into the 16:53 run. The 18:17 run reached `GameManager initialized!` at 97 s with none. `51339fa6` itself carries `error.type` `Crash` and no `_mod_faulting_tid`, so no hang fired in that run in its 69 s; the 14:40 run left no report either. The prune run reached the main menu at 43 s and wrote none in the 12 minutes it lived. Ticket 01's 9 of 9 plus these 8: 10 of 17 launches wrote a boot snapshot. Common, and no fixed event.
- On a later stall: `a7062766` 88 s, `7a787f3c` 294 s, `8782e9a0` 579 s. In seven of the access-violation runs a hang had fired earlier, by the annotations in finding 9, at unknown moments.
- At a quit: none. Ticket 01 ended the game 8 ways, reading `reports/` after each, and none wrote a snapshot at exit: `wine taskkill` (launches 1, 5), an X11 close (4), a quit from the eval (6) and from the UI (3), all with both shutdown lines; SIGTERM on a healthy game (7) and a stuck one (2), SIGSTOP then close, SIGTERM and SIGKILL (8), and SIGKILL on a healthy game (9), all with neither.

Verdict: the roadmap's "the snapshots it takes on a quit or a hang" (`docs/ROADMAP.md:1121` at `a89b064`) is half right: hang yes, quit no, on 8 ways of ending. No code path in the decompile writes one on quit: the only `NativeReport` call is `HandleAnr`'s.

Verdict: `hint.ts:246`, "A new crash report appeared, which may be a hang snapshot", holds. `hint.ts:330`'s comment said a report comes for a hang "and at boot on some machines": the fact held, the boot case being a hang, and "on some machines" was unmeasured, one machine both ways, so ticket 07 dropped it.

**A report alone proves nothing.** Eleven of 23 are snapshots of a live game. And a running game can have an old report beside it.

**The test.** One step, on the `Crash reason:` line of `--brief`:

| `Crash reason:` | Frame 0 | Means |
|---|---|---|
| `Simulated Exception` | `BacktraceCrashpadWindows.dll + 0x2dcd` | Hang snapshot. No crash. Read Thread 0 for what stalled |
| `EXCEPTION_ACCESS_VIOLATION_READ`, `EXCEPTION_BREAKPOINT`, any other | The faulting module | Real crash. Read frame 0's module |

**Tested on all 27 files; `Crash reason` alone decided each, and the known cause agrees in all 12 real ones and in all 11 snapshots' `error.message`.**

| Dump | Where | Reason, code | Uptime | Crashing thread, frame 0 | `error.type` | `_mod_faulting_tid` | `error.message` | Verdict |
|---|---|---|---|---|---|---|---|---|
| `7a787f3c` | live, spike | Simulated, `0x517a7ed` | 294 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `a7062766` | live | Simulated | 88 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `e89f11dd` | live | Simulated | 44 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `271a0aa4` | spike | Simulated | 44 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `2c2fa929` | spike | Simulated | 38 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `930a848b` | spike | Simulated | 38 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `98bb4b09` | spike | Simulated | 38 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `9dc85565` | spike | Simulated | 39 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `cdc14f00` | spike | Simulated | 38 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `ed7bf8fd` | spike | Simulated | 38 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `8782e9a0` | forced-1707 | Simulated | 579 s | 80, Backtrace `+0x2dcd` | Hang | main | ANRException | Hang |
| `51339fa6` | live | `EXCEPTION_BREAKPOINT`, `0x80000003` | 69 s | 22 `Job.Worker 1`, UnityPlayer `+0x1093511` | Crash | absent | absent | Crash, Burst |
| `09b88fcf` | crashes | AV read, `0xc0000005`, at `-1` | 129 s | 0, UnityPlayer `+0x1ed5fc6` | Crash | absent | absent | Crash |
| `63a29eb6` | crashes | AV read, at `-1` | 167 s | 0, UnityPlayer `+0x1ed5fc6` | **Hang** | main | absent | Crash |
| `711f9633` | crashes, bug-3 | AV read, at `-1` | 315 s | 0, UnityPlayer `+0x1ed5fc6` | **Hang** | main | absent | Crash |
| `3c0f9116` | crashes | AV read, at `0x9c68ed7e4` | 318 s | 0, UnityPlayer `+0x1ed5fc6` | **Hang** | main | absent | Crash |
| `aa0f97fe` | crashes, bug-2 | AV read, at `-1` | 103 s | 0, UnityPlayer `+0x1ed5d52` | **Hang** | main | absent | Crash |
| `3ed7aedd` | crashes | AV read, at `-1` | 91 s | 0, UnityPlayer `+0x1ed5d52` | Crash | absent | absent | Crash |
| `71a61138` | crashes, bug-1 | AV read, at `-1` | 90 s | 0, UnityPlayer `+0x1ed5d52` | Crash | absent | absent | Crash |
| `27586770` | crashes | AV read, at `-1` | 91 s | 0, UnityPlayer `+0x1ed5d52` | Crash | absent | absent | Crash |
| `29ca62ac` | crashes | AV read, at `-1` | 90 s | 0, UnityPlayer `+0x1ed5d52` | **Hang** | main | absent | Crash |
| `455f8b0f` | crashes | AV read, at `-1` | 89 s | 0, UnityPlayer `+0x1ed5d52` | **Hang** | main | absent | Crash |
| `497e6e0a` | crashes | AV read, at `-1` | 89 s | 0, UnityPlayer `+0x1ed5d52` | **Hang** | main | absent | Crash |

`Where`: `live`, the crashpad folder; `spike`, ticket 01's kept dumps; `forced-1707`, ticket 02's forced stop of a stuck game; `crashes`, the kept crash runs of the `TerrainSystem` overrun; `bug-1` to `bug-3`, the three dumps of its bug report.

The `-1` is `0xffffffffffffffff`. Symbolised: `+0x1ed5d52` is `block_remove` under `tlsf_free`, and `+0x1ed5fc6` is `tlsf_check_pool` under `CheckIntegrity`, the mod's heap probe faulting on the trampled block. Both are the one known bug, so neither names `TerrainSystem`: a dump in the allocator shows where the corruption was found, not what wrote it (`game-dies-lane-cull-overflow.md`, "Prevention").

The breakpoint's frame 0, with the release player's PDB: `DebugStringToFilePostprocessedStacktrace + 0x9e1`, the engine stopping after logging the Burst exception. `Crash address` in an `EXCEPTION_BREAKPOINT` is the breakpoint itself.

**Do not use `error.type`.** Verdict: ticket 01's "annotation `error.type` = `Hang`" is true of snapshots and is no test. **7 of 11 access violations read `Hang`.** The code explains it: `Crash` is set once at start (`NativeClient.cs:101`) and `Hang` at each report (`:139`, `:141`), and nothing sets it back, so a crash after any earlier stall in the same process carries `Hang`. `_mod_faulting_tid` sticks the same way, in exactly those 7. Only `error.message` does not stick, present in snapshots and absent from all 12 crashes. So three of the four signals agree with the crash reason and one lies, and the crash reason is the one `--brief` prints.

**Rots:** `BacktraceCrashpadWindows.dll + 0x2dcd`, the offset, per library build (the module is what counts); `error.type` handling, `NativeClient.cs`; the exception code spelling in `minidump-stackwalk`.

### 9. Picking the right report for a run

**A crash can sit beside a snapshot of its own run.** In 7 of 11 access violations `_mod_faulting_tid` proves a snapshot fired earlier in the same process, so that run wrote at least two reports. The boot snapshots of those runs were not kept beside them, so no pair is on disk to show. So never take the only report, or the first one listed, as the run's: UUID order is random.

**The steps that held on what is here.**

1. Run window: `Logs/SceneFlow.log` holds the run's first stamp, which is what `evidence.ts` uses, and the next run's first stamp is its end, since a silent crash's dump lands after its last log line (a captured stuck game: `Player.log` 17:06:52, dump 17:07:01).
2. List `reports/` by mtime, or `creation_time` in `metadata`. Keep those within the window.
3. The newest one is the candidate: a real crash ends its process, so nothing of that run comes after it. Run the test on it.
4. Cross-check: mtime minus `Process uptime` is the process start.

After a `restart` or `test` the live `Logs/` is the new run's, so the first stamp comes from `previous-run/Logs/SceneFlow.log`. Inside `previous-run/` the reports are already the run's and every date is the copy's (finding 3), so `Process uptime` orders them in place of steps 2 and 4.

Checked on `51339fa6`: mtime 14:42:53, uptime 69 s, start 14:41:44; survey run 11's last mark `job-burst` at 13:42:52.6 UTC, 0.4 s before the dump; `Player.log` (mtime 14:43) ends in that exception. This is one clean case: the crash runs' dumps carry their copies' mtimes (the folders are named for them), so they cannot be checked the same way.

**Does a real crash always write a dump?** Not known to, and not every death is one.

- A killed game writes none: SIGTERM and SIGKILL, healthy or stuck, launches 2, 7, 8, 9 of the endings in finding 8. Under Wine the signal never becomes a Windows exception, and `Player.log` did not even grow. A kernel OOM kill is a SIGKILL, so expect the same.
- All 12 real crashes here left a dump, by selection: a crash with no dump leaves nothing to count, so this is no rate.
- An uncaught managed exception in a thread is no crash at all: survey run 11 survived `thread` and went on to `job-burst` 4 s later. Runs 12 and 13 have `thread` as their last mark and no report because it was the last case the harness fired: run 12's capture read the game alive after it, and run 13 records no death.

Unconfirmed: crashes outside crashpad's reach. A fault on Wine's Unix side, in a Linux driver, `__fastfail` or a stack overflow may write nothing. A deliberate fault in each, or the first one that happens with its logs, settles it. The reference should say "a death with no report is no evidence of a clean exit": `The game went away with no cs2-dev stop` from `hint.ts:245` is the signal there.

### 10. Windows

Ticket 10's run on real Windows settled what this section held open (`game-cycle.md`, section 8):

- The folder (finding 1) holds there, and the prune fired about 11 minutes into a run, keeping the newest reports.
- A boot stall writes a snapshot there: every boot wrote at least one.
- The module picture: native `d3d11.dll` and `dxgi.dll`, with NVIDIA's `nvwgf2umx.dll` in place of DXVK; none of the crash dump's 155 threads is named `dxvk-*` or `wine_*`.
- Thread 0 is the main thread: in snapshots it equals `thread.main`, and a crash `UnityEngine.Diagnostics.Utils.ForceCrash` raised on the main thread read `EXCEPTION_ACCESS_VIOLATION` with Thread 0 `(crashed)`, the thread its `thread.main` annotation names.
- `minidump-stackwalk` runs there, from the project's prebuilt Windows release. WinDbg's `cdb` opens these dumps, names the threads, and walked that crash as `KERNELBASE!RaiseException` under `UnityPlayer!Utils_CUSTOM_ForceCrash`, then a bare JIT address.

Unconfirmed: Visual Studio opening these dumps; a fault the game does not raise itself, such as a dereference in native code. An access violation on a thread injected into the game from outside wrote no report, no Unity crash folder and no WER dump, which says nothing of the game's own threads.

### 11. The Backtrace pieces, and what rots

**Rots:**

- `Colossal.Logging.Backtrace.BacktraceHelper`, `src/Colossal.Logging/Colossal.Logging.Backtrace/BacktraceHelper.cs`.
- `Backtrace.Unity.Runtime.Native.Windows.NativeClient.HandleAnr` and `HandleNativeCrashes`, `src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Windows/NativeClient.cs`.
- `BacktraceConfiguration.CrashpadDatabasePath`.
- `BacktraceCrashpadWindows.dll`, `crashpad_handler.dll` in `Cities2_Data/Plugins/x86_64/`, install-dated 2026-09-10 and without a stated version. `DllImport` entry names `Initialize`, `AddAttribute`, `DumpWithoutCrash` (`NativeClient.cs:36-43`).
- Module names `v8.dll`, `cohtml.WindowsDesktop.dll`, `cohtml_Unity3DPlugin.dll`, `RenoirCore.WindowsDesktop.dll`, `lib_burst_generated.dll`, `mono-2.0-bdwgc.dll`, in that folder and `MonoBleedingEdge/EmbedRuntime/`.

### 12. The V8 crash

The dump is gone. Its `minidump-stackwalk` output survives in the HallOfFame session transcript `b6281e1d-cfef-4c62-848f-3c9d1cc36902`, 2026-10-13 (`~/.claude/projects/-home-morgan-Documents-Dev-HallOfFame/`), a captured run:

```
Crash reason:  EXCEPTION_ACCESS_VIOLATION_READ
Crash address: 0x0000000000000000
Crashing address may be the result of a flipped bit:
  0. Valid address: 0x0000000000020000 (0.250)
  …
Process uptime: 75 seconds

Thread 0  (crashed) - tid: 312
 0  v8.dll + 0x7bcfd7
    Found by: given as instruction pointer in context
 1  v8.dll + 0x95f73e
    Found by: stack scanning
 2  v8.dll + 0x4eaa71
 3  v8.dll + 0x4d3b43
 4  v8.dll + 0x2e1439
 5  v8.dll + 0x33be61
 6  v8.dll + 0x10e5d2f
 7  cohtml.WindowsDesktop.dll + 0x5d741d
```

So a V8 crash is Thread 0, frame 0 in `v8.dll`, `cohtml.WindowsDesktop.dll` below it. One sample. `Crashing address may be the result of a flipped bit` is the tool's stock guess on a low address and no finding; a null read is the plain reading. A later session attributes the cause to a Gameface data-binding shape, "the same superset order" (`9044e84e-33c4-4a50-8a52-ea0583b35b2d.jsonl:3509`), a lead that was not rechecked; the roadmap's "left `Player.log` without its last seconds and no managed exception anywhere" (`docs/ROADMAP.md:1119` at `a89b064`) is that report's own observation, not rechecked. Verdict: the roadmap's "module offsets alone tell a V8 frame called from cohtml from Mono (a script or binding crash) from an engine one" holds on the module name, with a real sample for V8 and the engine and none for Mono; the offsets add nothing unsymbolised, and the module names do the telling.

### 13. What this corrected elsewhere

`docs/research/game-cycle.md` and the shipped `game-cycle.md` took the prune, the watchdog period and the boot count from here. `plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md:77-80` holds of the logs it speaks of, and a relaunch does not touch dumps. `plugins/cs2-modding/mcp/src/hint.ts:246` holds as written.

### 14. `SOURCES.md` gaps

- **Entry 14, the crashpad sentence** named the folder only. Correction, landed:  `metadata` is a decodable record of each report's creation, upload attempt and state (format in finding 1), and that `.cs2-dev/previous-run/crashpad/reports` loses it.
- **New entry: crashpad's own source**, `backtrace-labs/crashpad` branch `backtrace` and `chromium/crashpad`. Authoritative for the report database and pruning, which the decompile does not hold, since `src/Backtrace.Unity/` is only the client. It is a component the game ships, read like Harmony: not the game, and owning its subject. The shipped `crashpad_handler.dll` outranks it where they could differ.
- **New entry: `Cities2_Data/Plugins/x86_64/` as a whole.** Entry 16 reaches `lib_burst_generated.dll` only and states that entry 5 stops at `Content/`. The handler and the native UI and JS libraries there, with `MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll`, are readable by `dump_syms` and `objdump`.
- **New entry: Unity's symbol server**, `https://symbolserver.unity3d.com/`, first-party for the player's and Mono's PDBs by the ids a dump states.
- **`docs/SOURCES.md` "What looks like a source and is not":** no change needed.

### 15. Catalog gaps

None. See the corpus sweep under Dead ends.

### 16. Secrets, what a reader of a dump needs

A dump holds the process's environment block (`docs/SOURCES.md:190`, `docs/research/game-cycle.md:85`). The stackwalker's outputs, human and JSON, do not print it. `--dump` without a filter prints annotations carrying `hostname` and `device.name`. The module list and CodeView paths are outside both: `51339fa6`, `a7062766` and `e89f11dd` have 7, 10 and 4 modules with a full path under a user's AppData, `steamuser` under Proton, and 24, 18 and 21 `steamuser` hits in stacks and other memory outside those records, so a blanking list cannot close. `application.temporary_cache` and `application.session` are annotations beyond the four above. All counted with no value printed. Do not run `strings` on a dump and show it, and do not hand a dump to anyone unblanked. The network-tools bug report blanked the environment first, with its own script, anchoring on `WINEPREFIX=`.

---

## Bridge

- `diagnostics` (`plugins/cs2-modding/skills/cs2-modding/references/technique/diagnostics/diagnostics.md`): this is its sibling. `diagnostics` owns the log catalogue and the order of reading; the crash path here starts where it ends, "copy `Player.log` first" (`:77-79`). Its `Player.log` read comes first for a Burst crash, finding 6.
- `game-cycle` (`.../technique/game-cycle/game-cycle.md`): owns the launch, the restart and the evidence procedure; this topic uses `previous-run/` (finding 3). The tool's `A new crash report appeared` and `The game went away with no cs2-dev stop` lines are where a reader arrives from.
- `performance-and-memory` (`.../technique/performance-and-memory/burst-at-debug-time.md`): a Burst job cannot be stepped, and its `<Mod>_win_x86_64.pdb` is what names a Burst frame after a crash (finding 5).
- `ecs-in-this-game` (`.../technique/ecs-in-this-game/ecs-in-this-game.md:262`): the safety checks are compiled out, which is why an out-of-bounds write in a job shows up as an allocator crash with nothing logged, the `tlsf_free` shape in finding 8.
- `binding-layer` (`plugins/cs2-modding/skills/cs2-modding-ui/references/binding-layer/`): the one V8 crash traces to a binding's shape (finding 12).
- **Mechanics topics:** none truly exercises this. A dump reads the same whichever system corrupted the heap, and no mechanics reference names `TerrainSystem`, the one game bug in the set. The network-tools case is a worked example of this technique, not a fact about terrain.

## Dead ends

- **More dumps.** `find` over `~/Documents/Dev` and `~/Documents/Handoffs` for `*.dmp`, `*.mdmp`, `*.minidump`, `.scratch/` folders included: the 27 listed, and nothing among the survey's captures or in `~/Documents/Handoffs`. The Handoffs files mention no crashpad, minidump or V8. The V8 dump does not survive; the transcripts hold its output.
- **Other crash folders.** `AppData/Local/Temp/Colossal Order/Cities Skylines II/` holds no `crashes` (finding 2).
- **Symbols through `--symbols-url`.** Finding 5.
- **`--use-local-debuginfo`.** Not run: the paths a dump holds are Windows paths, and `dump_syms` had already answered the question.
- **Naming JIT frames from `Player.log`.** Finding 6.
- **`strings` on a dump.** Not run, under the secrets rule. The annotations come from `--dump --brief` filtered, and from a parser reading only the crashpad info stream.
- **Exception code in the decompile.** `0x517a7ed` is not there; it is crashpad's, and the name is rust-minidump's.
- **`thread.main` and `_mod_faulting_tid` in the decompile.** No hit under `src/Backtrace.Unity/`; both are strings of the native library.
- **Mod corpus.** `rg -i 'minidump|\.dmp|crashpad|stackwalk|backtrace'` over `~/Documents/Dev/cs2-mods` `*.cs`, `*.md`, `*.json`, build output excluded: one file, `CS2-Platter/Platter/PlatterMod.cs:112`, `.SetBacktraceEnabled(true)`, a logger setting with nothing on dumps. No mod reads or handles a dump, so no `Demonstrates` gap against `plugins/cs2-modding/skills/cs2-modding-setup/references/mod-catalog.md`.
- **Seed surveys.** `survey-decompile-moddable-surface.md:52` names `Backtrace.Unity` as crash reporting, nothing more; `survey-mods-techniques.md` and `survey-wiki-inventory.md` carry nothing on the topic (their `dump` hits are locale exports and data tables).
- **The wiki.** Not fetched: no survey lead on crash dumps, and this subject has first-party answers on disk.
- **`docs/research/conflicts.md`.** No entry names the topic, ruled or open (grep for crash, dump, minidump). Nothing appended: each disagreement above has a winner, and what is open is `Unconfirmed:` with a named run.
