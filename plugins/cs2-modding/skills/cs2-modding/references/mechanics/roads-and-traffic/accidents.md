# Accidents

Verified against game version 1.6.0f1.

**Read this with the decompile open.**
Without one you cannot check anything below.
`cs2-modding-setup` provisions it.

`RoadSafetySystem` starts random road accidents; disasters drive their own accident events elsewhere.
It runs in `SystemUpdatePhase.GameSimulation` at a 4096-frame interval, over every live edge carrying `Edge`, `Composition` and `Road`, and its job discards 63 chunks in 64 by a random roll before doing anything (`src/Game/Game.Simulation/RoadSafetySystem.cs`).

## The safety formula

Source: `src/Game/Game.Simulation/RoadSafetySystem.cs`, `src/Game/Game.Net/NetUtils.cs`.

```
duration = dot(m_TrafficFlowDuration0 + m_TrafficFlowDuration1, timeFactors) * 2.6666667
distance = dot(m_TrafficFlowDistance0 + m_TrafficFlowDistance1, timeFactors) * 2.6666667
if distance < 0.01, or the composition prefab has no RoadComposition: skip     // no traffic, no accidents
flowSpeed = NetUtils.GetTrafficFlowSpeed(duration, distance)      // 0..1

safety  = 500 / sqrt(distance)                    // volume: more traffic, less safe
safety *= lerp(0.5, 1, flowSpeed)                 // congestion: slow flow, less safe
safety *= lerp(1, 0.75, csum(NetCondition.m_Wear) * 0.05)         // wear
safety *= lerp(lit, 1, min(1, dayLightBrightness * 2))            // darkness
          where lit = 0.9 if Game.Prefabs.RoadFlags.HasStreetLights and not Game.Net.RoadFlags.LightsOff
                    = 0.7 otherwise
safety *= 1.1 if RoadFlags.SeparatedCarriageways
apply district StreetTrafficSafety per side, then keep only the weaker side's net effect — opposing sides, or a modifier-carrying district on one side alone, change nothing                 // unless UseHighwayRules, and only on an edge carrying BorderDistrict
apply city HighwayTrafficSafety modifier          // if UseHighwayRules

TryStartAccident:
  for each accident prefab with TrafficAccidentData, not Locked, whose m_RandomSiteType == EventTargetType.Road:
    if random(1) < m_OccurenceProbability / max(1, safety):
      create the event entity with a TargetElement naming this edge; return
```

Traffic volume and congestion are the dominant inputs — safety falls with the square root of measured distance and halves again at zero flow speed — and a road carrying no traffic cannot have an accident at all.
The flags tested are `RoadComposition.m_Flags` off the edge's composition prefab, plus the runtime lights-off bit — the two same-named `RoadFlags` enums, on one line.

## Which prefabs qualify

Which events can start here is asset data: enumerate it with `ecs_query` on `Game.Prefabs.TrafficAccidentData` — a superset, since the system's own query also requires `EventData` and excludes `Locked`.
Read live at 1.6.0f1 the only carrier whose `m_RandomSiteType` is `Road` is the lose-control accident; the other carriers declare no random site (`m_RandomSiteType = None`) and are unreachable from this system.
**The authoring class and the baked component spell the probability field differently.**
`TrafficAccident.m_OccurrenceProbability` is copied into `TrafficAccidentData.m_OccurenceProbability` — one `r` short — so one search finds half the sites, and the field is a prefab value like any other.
Source: `src/Game/Game.Prefabs/TrafficAccident.cs`, `src/Game/Game.Prefabs/TrafficAccidentData.cs`.

(VOLATILE: every component, field, enum, system, method and `Source:` path this file names — their declarations under `src/Game/` in `Game.Simulation`, `Game.Net`, `Game.Prefabs` and `Game.Areas`, at the files cited beside each; the accident-prefab shape, against the `ecs_query` it states; the listing, against `RoadSafetySystem.cs`.)
