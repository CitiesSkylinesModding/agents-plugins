using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace MilestoneNotifier
{
    public class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(MilestoneNotifier));

        private class Translation
        {
            public Dictionary<string, string> Entries;
            public MemorySource Source;
        }

        // One source per language file, kept so a reload can take it out and put it back.
        private readonly Dictionary<string, Translation> m_Translations =
            new Dictionary<string, Translation>();

        private string m_TranslationsDirectory;

        public void OnLoad(UpdateSystem updateSystem)
        {
            var modManager = GameManager.instance.modManager;

            if (modManager.TryGetExecutableAsset(this, out ExecutableAsset asset))
            {
                m_TranslationsDirectory = Path.Combine(
                    Path.GetDirectoryName(asset.path),
                    "Translations");
            }

            LoadTranslations();
            GameManager.instance.onGameLoadingComplete += OnGameLoadingComplete;

            updateSystem.UpdateAt<MilestoneSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            GameManager.instance.onGameLoadingComplete -= OnGameLoadingComplete;
        }

        private void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            // Translators edit their file, then load a city to see the result.
            LoadTranslations();
        }

        private void LoadTranslations()
        {
            if (m_TranslationsDirectory == null || !Directory.Exists(m_TranslationsDirectory))
            {
                return;
            }

            var manager = GameManager.instance.localizationManager;

            foreach (var file in Directory.GetFiles(m_TranslationsDirectory, "*.json"))
            {
                // Files are named after the language they hold: en-US.json, fr-FR.json, nl-NL.json.
                var localeId = Path.GetFileNameWithoutExtension(file);
                var entries = JSON.Load(File.ReadAllText(file)).Make<Dictionary<string, string>>();

                if (m_Translations.TryGetValue(localeId, out var translation))
                {
                    // Swap the source out, refresh what it holds, and put it back.
                    manager.RemoveSource(localeId, translation.Source);
                    translation.Entries.Clear();

                    foreach (var entry in entries)
                    {
                        translation.Entries[entry.Key] = entry.Value;
                    }

                    manager.AddSource(localeId, translation.Source);
                }
                else
                {
                    translation = new Translation
                    {
                        Entries = entries,
                        Source = new MemorySource(entries),
                    };

                    m_Translations[localeId] = translation;
                    manager.AddSource(localeId, translation.Source);
                }

                Log.Info($"Loaded {entries.Count} strings for {localeId}.");
            }
        }
    }
}
