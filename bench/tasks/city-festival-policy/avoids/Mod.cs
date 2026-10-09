using System.Collections.Generic;
using Colossal;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CityFestival
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(CityFestival));

        public void OnLoad(UpdateSystem updateSystem)
        {
            GameManager.instance.localizationManager.AddSource("en-US", new PolicyLocale());
            updateSystem.UpdateAt<CityFestivalSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
        }
    }

    // The policy panel titles a policy from the locale key carrying its prefab name.
    public class PolicyLocale : IDictionarySource
    {
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            yield return new KeyValuePair<string, string>(
                $"Policy.TITLE[{CityFestivalSystem.kPolicyName}]",
                "City Festival");

            yield return new KeyValuePair<string, string>(
                $"Policy.DESCRIPTION[{CityFestivalSystem.kPolicyName}]",
                "Tourists find the city 20% more attractive "
                + "and residents enjoy 10% more entertainment.");
        }

        public void Unload()
        {
        }
    }
}
