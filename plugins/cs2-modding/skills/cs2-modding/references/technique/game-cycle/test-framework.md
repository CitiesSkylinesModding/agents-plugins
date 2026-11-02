# The game's test framework

Verified against game version 1.6.2f1.

**Read this with the decompile open.**
The technique holds without one, but every game symbol named below is checkable only there.
`cs2-modding-setup` provisions it.

## What a mod writes

Debug-only is the mod's own arrangement: nothing in the framework's run path gates on a build type.
Take the scenarios out of a Release build in the csproj, and wrap the registration, its `using` included, in `#if DEBUG`:

```xml
<ItemGroup Condition="'$(Configuration)' != 'Debug'">
  <Compile Remove="Scenarios\**" />
</ItemGroup>
```

A Release build then carries none.
(UNVERIFIED: that scenarios run on a stock, unpatched game — a scenario run there would settle it.)

Reference `Colossal.TestFramework.dll` from `Cities2_Data/Managed`, as you do `Game`.

**A mod's scenarios register themselves, from `OnLoad`.**
The game discovers its own once, before mods load.
`TestScenarioSystem.instance.scenarios` is the live dictionary, so writing to it needs no reflection.
A write after the automation took its enumerator makes `MoveNext` throw in `Update` each frame, which `OnLoad` runs well before.
Entries run after the game's, in insertion order, ignoring `testPhase`.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenarioSystem.cs`.

Enumerate the assembly's non-abstract `TestScenario` types, read each one's `TestDescriptorAttribute`, skipping a type with none, and write it in:

```csharp
TestScenarioSystem.instance.scenarios[descriptor.description] = new TestScenarioSystem.Scenario
{
    category = descriptor.category,
    testPhase = descriptor.testPhase,
    test = type,
    disabled = descriptor.disabled
};
```

A base class answers the pending-errors and cleanup traps below, and loads a city.

```csharp
public abstract class ModTestScenario : TestScenario
{
    protected ModTestScenario()
    {
        log.Info($"Dismissed {TestScenarioSystem.ConsumeErrorCount()} pending errors.");
    }

    protected async Task LoadNewCity(Colossal.Hash128 map)
    {
        await this.WaitFrames(2);

        var purpose = Colossal.Serialization.Entities.Purpose.NewGame;

        if (!await GameManager.instance.Load(GameMode.Game, purpose, map))
        {
            throw new InvalidOperationException("The city did not load.");
        }
    }

    protected override async Task OnCleanup()
    {
        if (GameManager.instance.gameMode is GameMode.Game)
        {
            await GameManager.instance.MainMenu();
        }
    }
}
```

A scenario derives from it, with a public constructor and its own descriptor, which does not inherit: `[TestDescriptor(string description, Category category, bool skipBeta = false, TestPhase testPhase = TestPhase.Default, bool disabled = false)]`.
`[Test]`, `[TestPrepare]` and `[TestCleanup]` methods return `void` or `Task` and take no parameters; an `async void` one is never awaited, so the run goes on at its first `await`.
Run scenarios with no `--startGame` and let each load what it needs: **only a load ending in the main menu releases the automation**, which starts 5 s later, so a load at launch leaves it waiting.
Source: `src/Game/Game.SceneFlow/GameManager.cs`.

## The false success

**The game's `Success` says nothing of a scenario that reached no test.**
`TestScenario.Run` catches everything from its start checks, `OnPrepare` and the test methods, and counts rise only per test.
So each of these ends in `FINISH TEST SCENARIO (Success, exceptions: 0, errors: 0)`: a throw in `OnPrepare`, the errors-at-start abort, two `[TestPrepare]` methods, an error logged rather than thrown in `OnPrepare`, and an error logged after the last test.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`.
(VOLATILE: the finish lines — `Colossal.TestFramework.TestScenarioSystem`, `Colossal.TestFramework.TestScenario`.)

`test` fails the first three, and a scenario with no `[Test]`, because no test ran.
**The last two pass that rule too: `test` catches one only if it sits in `TestScenarios.log` at error level or names the mod.**
An error logged in `OnPrepare` is also cleared before the first test, so a failed preparation throws rather than logs.

**`disabled: true` is reported passed.**
It skips the `[TestPrepare]`, `[Test]` and `[TestCleanup]` bodies, while `OnPrepare` and `OnCleanup` still run.
The test logs `FINISH TEST (Success)`, and the automation does not filter on the flag.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`.

**Errors pending from before a scenario abort it, as a false success.**
`CheckForErrorsAtStart` throws on any error since the last scenario, boot errors of the game and of other mods included, as is a system's exception at any time, which names the mod only in its stack, unless the constructor consumed them.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`, `src/Game/Game/UpdateSystem.cs`.

**One `[TestPrepare]` and one `[TestCleanup]` per scenario, inherited ones counted.**
Two throw after `OnPrepare` ran, run no test, and report Success.
A protected method on a base class counts toward the two, and a private `[Test]` or `[TestPrepare]` there silently never runs.
Source: `src/Colossal.Core/Colossal.Reflection/ReflectionUtils.cs`, `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`.

## Cleanup

**A throw in `OnCleanup` hangs the run.**
`IsFinished` is never set, so `FINISH` is never logged, the automation stays in `RunningScenario`, and `test` ends on a scenario that logged nothing.
`OnCleanup` runs when `OnPrepare` never did, so guard it against what was never built.
A throwing `[TestCleanup]` skips the later tests, leaves its test uncounted and logs no `FINISH TEST`.
A constructor that throws logs `START` and no `FINISH`.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`, `src/Colossal.TestFramework/Colossal.TestFramework/TestScenarioSystem.cs`.

## Categories

**`--categoryFilter` runs a category whole, so a second local Debug mod on the same bit joins the run and its failures fail it.**
`test` judges every scenario in the log, with no filter by mod.
Give each mod a bit of its own, passed as `test`'s `category`: `[TestDescriptor("Places a road", (Category)(1 << 21))]`.
With a filter set, a scenario runs only if all its category bits lie in the filter, `Category.Debug` is skipped outright, and `Category.None` runs under every filter.
Skips go to `Automation.log` as `Skipping <category> scenario: <name>`.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/AutomationClientSystem.cs`.
(VOLATILE: the enum's values — `Colossal.TestFramework.Category`; the filter expression — `Colossal.TestFramework.AutomationClientSystem`.)

**Under `IgnoreLogForTestResult`, a logged error does not fail the scenario, and fails `test` only if it sits in `TestScenarios.log` at error level or names the mod.**
Nothing in the game sets it.
Source: `src/Colossal.TestFramework/Colossal.TestFramework/TestScenario.cs`.
