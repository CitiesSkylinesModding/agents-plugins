# Burst: what it costs at debug time, and how to gate it

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
The technique holds without one, but the attribute and the two off switches named below are checkable only there.
`cs2-modding-setup` provisions it.

**The post-processor runs on every build and Burst-compiles for Windows, macOS and Linux.**
It is invoked from an after-build target with no configuration condition — Debug and Release alike, with a debug flag passed in both — and there is no toolchain switch that turns the pass off.
So Burst output ships as three native libraries beside the managed assembly, and is produced whether or not the mod has any Burst jobs.
The one thing that skips it is the publisher's update command, which turns off the toolchain's own targets — the output clear, the post-processing pass and the deploy — while the ordinary compile still runs, so an update can ship a freshly compiled assembly beside native libraries left from the previous build.
`cs2-mod-project` owns the build pipeline itself.
Source: `%CSII_TOOLPATH%/Mod.targets`.

**The cost is that a Burst-compiled job cannot be stepped.**
The managed body is still in the assembly and a breakpoint in it still binds; the native compilation runs instead, so the breakpoint never fires.

**Reach for the runtime switch first.**
Burst compilation is disabled at launch, with no rebuild and no change to the mod:

```
--burst-disable-compilation
```

or the environment variable `UNITY_BURST_DISABLE_COMPILATION`, set to anything other than empty or `0`.
The managed body is always in the assembly, so it runs instead of the native one.
It is read from the game process's own command line, which is the route to reach for; the environment variable is the fallback for a launcher that will not pass an argument through.
**The double dash is mandatory**: this is not a game option, and Burst string-compares the raw argument list itself, so the dash freedom the game's own options enjoy does not apply and a single-dash spelling is a silent no-op.
Source: `src/Unity.Burst/Unity.Burst/BurstCompilerOptions.cs`.

**Reach for a compile-time gate only if you will do this often enough that a launch argument becomes tiresome**, and then treat it as the more dangerous of the two.
The form is `[BurstCompile]` wrapped in `#if`, with the symbol defined in the Release configuration only:

```csharp
#if USE_BURST
[BurstCompile]
#endif
private partial struct MyJob : IJobEntity { }
```

The hazard is plain C#: **a preprocessor symbol defined nowhere produces no warning, no error and a build indistinguishable from a working one.**
The `#if` compiles, the attribute vanishes, and the mod ships unbursted with nothing to tell you.
If you write one, verify the symbol reaches the compiler in the configuration you meant.
The attribute belongs on the job struct: on a system class whose only Burst code is its nested jobs it is legal and inert, so an audit that counts attributes to check the gate has to count the ones on jobs.
Source: `src/Unity.Burst/Unity.Burst/BurstCompileAttribute.cs`.

(VOLATILE: the `[BurstCompile]` attribute's spelling, the launch argument and the environment variable name — the Burst package's attribute declarations and `BurstCompilerOptions`.)
