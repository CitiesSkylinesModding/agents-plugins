using Game.Debug;

namespace AbandonedWatch
{
    // The mod's counters, in one place so the menu and the systems share them.
    public static class DebugStats
    {
        // One sample per in-game hour: a day is 262144 simulation frames.
        [DebugWatchValue(updateInterval = 262144 / 24, historyLength = 256)]
        public static int AbandonedCount;
    }
}
