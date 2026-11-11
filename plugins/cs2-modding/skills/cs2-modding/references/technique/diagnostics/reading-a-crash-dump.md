# Reading a crash dump

Verified against game version 1.6.4f1.

**Read this with the decompile open.**
Reading a dump needs none, since what follows rests on the dumps and their tools; only the `Source:` lines into game C# go uncheckable.
`cs2-modding-setup` provisions it.

Where a crashed run's minidump sits, how to read one with no symbols, and whether it records a crash at all.
It starts where [the log order](diagnostics.md) ends, for a run that stopped with no managed exception to read.

## Read `Player.log` first

**For a Burst job that threw, `Player.log` holds the cause, and the dump adds only the thread.**
In a captured run it ended on the exception, `This Exception was thrown from a job compiled with Burst…`, and a native stack naming the mod's module.
A heap overrun is the opposite case: in a captured run `Player.log` stopped on ordinary lines with no crash text, and the dump was the one record.
The dump is what to reach for when the log is silent, and the report does not carry the log: its attachments folder stayed empty in a captured run.

## Where a dump lives, and how long

A report is `<uuid>.dmp` in `.cache/backtrace/crashpad/reports/` under the user data path, a flat folder with no pending or completed split.
The name carries no date, and the file's modified time is the report's creation.
A `metadata` file beside `reports/` holds each report's upload state in binary.
Unity's own `Temp/Colossal Order/Cities Skylines II/crashes` is a second place a dump could sit, and under Proton that folder does not exist.
(VOLATILE: this folder layout and the `metadata` file — `crashpad_handler.dll` and `BacktraceConfiguration.CrashpadDatabasePath`.)

**A report stands until a run reaches about 10 minutes with it already on disk, so read or copy a dump before that.**
That can be days.
The crashpad handler prunes on its own timer, about 10 minutes after it starts and daily after that, whatever the game is doing: in a captured run a game idling at the main menu did it.
It keeps a 128 MiB budget counted newest first, and one dump usually exceeds that alone, so a prune takes nearly everything it may touch, the same run's earlier reports included.
A crash ends its run, so its dump waits for the next run long enough to reach the timer.
A report stuck in upload outlasts the prune, which considers only pending and completed reports, and uploading removes nothing.
Source: `crashpad_handler.dll` (the constants); in `backtrace-labs/crashpad`, `client/prune_crash_reports.cc` (the rule) and `client/crash_report_database_win.cc` (upload only changes state).
(VOLATILE: the 10-minute delay, the daily period and the 128 MiB budget — `crashpad_handler.dll`.)

**The evidence copy is not always the older record.**
[`game-cycle`](../game-cycle/game-cycle.md)'s kept run holds the reports dated within it, in `.cs2-dev/previous-run/crashpad/reports/` under the user data path.
It misses `metadata`, so it cannot say whether a report uploaded.
It misses the date, since a copied file, a log as much as a report, is dated by the copy; `Process uptime` inside the dump and that run's logs still place it.
It misses a report of an earlier run, gone from the copy by the second `restart` while the original may still stand in `reports/`.
It misses a report pruned during the run, so an empty copy says only that none was left at the stop.
On Windows it misses a report another process held open while it copied.
Source: `mcp/src/evidence.ts`.

## Reading one with no symbols

rust-minidump's prebuilt releases carry the tool, and `cargo install minidump-stackwalk` builds it where a Rust toolchain exists; it opens no connection unless given `--symbols-url`.
On Windows, WinDbg and Visual Studio are the native alternatives, and the commands here want a POSIX shell and `jq`: WSL or Git Bash. (UNVERIFIED: Visual Studio opening these dumps — a Visual Studio install there settles it.)

```sh
minidump-stackwalk --no-color --brief "$dump"
minidump-stackwalk --no-color "$dump" > out.txt
minidump-stackwalk --no-color --json --pretty "$dump" > out.json
grep '^Thread' out.txt
awk '/^Thread /{p=($2=="0")} p' out.txt
```

In order: the verdict and the crashing thread, every thread then the module list, the same as data, every thread's name, Thread 0 alone.

**Thread 0 is the main thread, and it prints without a name.**
The `thread.main` annotation matched it in every dump read, crashes and snapshots alike.
The crashing thread prints first and the rest follow by index, so the first heading of the full output is Thread 0 only when Thread 0 crashed.
In a real crash `(crashed)` marks the thread that faulted: Thread 0 for a fault on the main thread, a `Job.Worker N` for a Burst job.
(VOLATILE: thread names and the `thread.main` annotation — Unity's and the game's threads in a dump, and `BacktraceCrashpadWindows.dll`.)

**With no symbols, trust frame 0 and little below it.**
`Found by: given as instruction pointer in context` is the real position.
`Found by: stack scanning` is any stack word pointing into a module's code, stale return addresses included, so one address can appear twice in a stack and a module can look busy on noise.
Read the module, since the offset alone tells nothing.

| Module | Layer |
| --- | --- |
| `BacktraceCrashpadWindows.dll` | the watchdog: a hang snapshot, see below |
| `UnityPlayer.dll` | the engine, its native allocator included |
| `v8.dll`, `v8_libbase.dll`, `v8_libplatform.dll` | the UI's JavaScript engine |
| `cohtml.WindowsDesktop.dll`, `cohtml_Unity3DPlugin.dll` | the UI middleware |
| `mono-2.0-bdwgc.dll` | the Mono runtime |
| no module, a bare address | Mono JIT code, so managed code |
| `lib_burst_generated.dll` | the game's Burst code |
| `<Mod>_win_x86_64.dll` | a mod's Burst code |
| `d3d11.dll`, `dxgi.dll` | graphics; under Proton these are DXVK, on `dxvk-*` and `UnityGfxDeviceWorker` threads with `winevulkan.dll`, and on Windows the vendor's driver sits below them, `nvwgf2umx.dll` for NVIDIA |
| `KERNELBASE.dll` | Windows raising an exception: at frame 0 of a crash, the frame below names the module that raised it |
| `ntdll.dll` | a thread at rest; on the main thread, a wait; never the crashing thread in a captured run |

(VOLATILE: these module names — `Cities2_Data/Plugins/x86_64/` and `MonoBleedingEdge/EmbedRuntime/`.)
(UNVERIFIED: what a crash inside Mono or a graphics driver shows at frame 0, and that a bare address is always JIT code rather than Wine's Unix side — no dump read shows either; one such crash, or Mono's address ranges in a live process, settles it.)

In a captured run a V8 crash read Thread 0, frame 0 in `v8.dll`, with `cohtml.WindowsDesktop.dll` below it: a script or binding crash, which sends the reader to [`binding-layer`](../../../../cs2-modding-ui/references/binding-layer/binding-layer.md).
The tool's `Crashing address may be the result of a flipped bit` is its stock guess on a low address, and a null read is the plain reading.

**A dump names no managed frame.**
`Player.log`'s `(Mono JIT Code) Class:Method` lines matched none of a dump's bare addresses in a captured run, and they exist only for a stack the engine logged.
The last native frame before the bare addresses is what names the managed edge: with symbols it reads as the call the managed code made, such as `UnsafeUtility_CUSTOM_FreeTracked`.

Under Proton, `Operating system` still reads `Windows NT`, because that is what Wine reports.
`winex11.drv` in the module list or a `wine_*` thread is what tells Proton apart.

## Crash or hang

**A report alone proves nothing, because a `Simulated Exception` is a stall report from a live game.**
`cs2-dev`'s `A new crash report appeared` says no more than that.
A watchdog thread compares the main loop's update time across a sleep, and on no change writes one dump while the process carries on.
So a main-loop stall of about 5 to 10 s writes one, a slow boot step above all: most boots wrote one in the captured runs, some did not, and later stalls do too.
No quit wrote one on any way of ending a game tried, and the watchdog is the only caller of that report path.
The sleep is 5 s read live on the native client, and the 15000 in `BacktraceHelper.cs` never reaches it.
Source: `src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Windows/NativeClient.cs` (the sleep), `src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Base/NativeClientBase.cs` (the copy).
(VOLATILE: the 5 s sleep — `_nativeClient.AnrWatchdogTimeout` on a running game's `BacktraceClient.Instance`.)

The test is one step, the `Crash reason:` line of `--brief`.
It decided every dump read, and the known cause agreed each time.

| `Crash reason:` | Frame 0 | Means |
| --- | --- | --- |
| `Simulated Exception` | `BacktraceCrashpadWindows.dll` | hang snapshot, no crash |
| `EXCEPTION_ACCESS_VIOLATION_READ`, `EXCEPTION_BREAKPOINT`, any other | the faulting module | real crash, read frame 0's module |

The dump's exception code is `0x0517a7ed`, which `minidump-stackwalk` prints as `Simulated Exception`.
`Crash address` in an `EXCEPTION_BREAKPOINT` is the breakpoint itself: in a captured run the engine stopped there after logging a Burst exception.

**`--brief` on a hang snapshot shows the watchdog, and nothing there says so.**
It prints the crashing thread, in the captured runs an unnamed one with frame 0 in `BacktraceCrashpadWindows.dll`, and nothing about the stall.
Thread 0 of the full output is what stalled; read it, with symbols where `ntdll.dll` alone tops it.
In a captured run a symbolised Thread 0 waited in `GfxDeviceClient::WaitForPendingPresent`, a main thread waiting on the render thread.

**`error.type` is no test.**
`Crash` is set once at start and `Hang` at each report, and nothing sets it back, so a crash after an earlier stall in the same process carries `Hang`.
`_mod_faulting_tid` sticks the same way.
Source: `src/Backtrace.Unity/Backtrace.Unity.Runtime.Native.Windows/NativeClient.cs`.
(VOLATILE: this annotation handling — `NativeClient` and `BacktraceCrashpadWindows.dll`.)

**A dump in the allocator shows where the corruption was found, not what wrote it.**
An access violation in `UnityPlayer.dll` at `0xffffffffffffffff`, reading `tlsf_free` or `block_remove` once symbolised, with a silent `Player.log`, is the shape a heap overrun left in a captured run.
The fault comes later than the write, on a block the overrun trampled, so hunt the writer and leave the crashing frame alone.
[`ecs-in-this-game`](../ecs-in-this-game/ecs-in-this-game.md) has the safety checks whose absence lets a job write out of bounds with nothing logged.

## Real stacks, when frame 0 is not enough

`cargo install dump_syms` gets the second tool.
Run on a shipped DLL alone it gives unwinding, turning scan noise into real frames.
A PDB from Unity's symbol server gives the engine's names with no line numbers, and Mono's with them.
The two make one symbol file, and a PDB alone names the frames and keeps the false ones.
`--symbols-url` fails against that server, which holds PDBs where the tool asks for Breakpad `.sym`, so copy the DLL into a working folder and build the file by hand:

```sh
( set -e
dll=UnityPlayer.dll; m=".modules[]|select(.filename==\"$dll\")"
pdb=$(jq -r "$m.debug_file" out.json); id=$(jq -r "$m.debug_id" out.json)
curl -f -A 'Microsoft-Symbol-Server/10.0' -o "$pdb" "https://symbolserver.unity3d.com/$pdb/$id/$pdb"
dump_syms "$dll" > x.sym
read -r _ _ _ id pdb < x.sym
mkdir -p "syms/$pdb/$id"; mv -f x.sym "syms/$pdb/$id/${pdb%.pdb}.sym"
minidump-stackwalk --no-color --symbols-path syms "$dump" > out.txt
)
```

`read` takes `x.sym`'s `MODULE` line, so the file lands under what the DLL is rather than what the dump asked for.
The same steps serve `mono-2.0-bdwgc.dll`, with its name in `dll`.
`dump_syms` alone gives `v8.dll`, cohtml and Mono partial names from their export tables.
`lib_burst_generated.dll` unwinds and stays unnamed, since no PDB ships for it.

**The DLL must be the build that crashed, and `debug_file` says which player ran.**
It reads `UnityPlayer_Win64_player_development_mono_x64.pdb` for a debug-patched install and `UnityPlayer_Win64_player_mono_x64.pdb` for the stock one.
An offset means nothing across the two, so a stock run's dump takes nothing from a patched install's DLL, and `dump_syms` on the dump's own PDB alone gives names and no unwinding.
Match a DLL to the dump by `code_id`.
(VOLATILE: the player's PDB names — `modules[].debug_file` in a dump of the installed player.)

**A rebuilt mod no longer matches its dump.**
`dump_syms` on a mod's `<Mod>_win_x86_64.pdb` names its Burst jobs, and only for the build that ran, so keep the crashing build's PDB.
Getting a Burst job into a debugger instead is [`performance-and-memory`](../performance-and-memory/performance-and-memory.md).

## Picking the right report for a run

**Take neither the only report nor the first one listed as the run's.**
A crash can sit beside a snapshot of its own run, a running game beside an old report, and UUID order is random.

1. Take the run's window: the first stamp in `Logs/SceneFlow.log` to the next run's first stamp, or to now with no later run, since a silent crash's dump lands after its last log line.
2. List `reports/` by modified time and keep those inside it.
3. Run the test on the newest, since a real crash ends its process and nothing of that run comes after it; a snapshot there means no crash report of that run stands.
4. Cross-check: modified time minus `Process uptime` is the process start.

After a `restart` or `test` the live `Logs/` is the new run's, so take the first stamp from the kept `Logs/SceneFlow.log`.
Inside the kept copy the reports are already the run's and every date is the copy's, so `Process uptime` orders them in place of steps 2 and 4.

**A death with no report is no evidence of a clean exit.**
A killed game writes none: SIGTERM and SIGKILL left no report, healthy or stuck, in captured runs, since under Wine the signal never becomes a Windows exception, and `Player.log` did not grow either.
A kernel out-of-memory kill is a SIGKILL, so expect the same.
An uncaught managed exception on a thread is no crash at all, and in a captured run the game went on.
`cs2-dev`'s `The game went away with no cs2-dev stop` is the signal that remains.
(UNVERIFIED: whether a fault on Wine's Unix side, in a Linux driver, a `__fastfail` or a stack overflow writes a dump — a deliberate fault in each settles it.)

## Keeping a dump private

**A dump holds the process's environment block, and `--dump` prints the hostname.**
The `minidump-stackwalk` outputs above print neither, so filter `--dump` to the keys you came for, and keep their values out of any report:

```sh
minidump-stackwalk --dump --brief "$dump" | grep -E 'thread\.main|_mod_faulting_tid|error\.(type|message)'
```

The same view holds `device.name`, `guid` and `application.data_path`, and `strings` on a dump shows the environment.
A dump is not shareable as is: the environment block, the annotations, the module list and process memory all carry the machine's name or the user's paths, so hand one over only blanked.
