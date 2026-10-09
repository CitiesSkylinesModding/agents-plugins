using System;
using System.Collections.Generic;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Json;
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

        // The source registered per locale, so a reload can remove it before adding the next.
        private readonly Dictionary<string, TranslationFileSource> m_Sources =
            new Dictionary<string, TranslationFileSource>();

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
                // Files are named after the language they hold: en-US.json, fr-FR.json.
                var localeId = Path.GetFileNameWithoutExtension(file);

                // Adding a source under a locale the game has not registered is a silent no-op,
                // so a file for a language the game does not ship is reported rather than dropped.
                if (!manager.SupportsLocale(localeId))
                {
                    var available = string.Join(", ", manager.GetSupportedLocales());
                    var name = Path.GetFileName(file);

                    Log.Warn(
                        $"{name}: the game has no '{localeId}' language, so it is not loaded."
                        + $" Languages available: {available}.");
                    continue;
                }

                Dictionary<string, string> entries;

                try
                {
                    entries = JSON.Load(File.ReadAllText(file)).Make<Dictionary<string, string>>();
                }
                catch (Exception e)
                {
                    // A broken file keeps its previous strings; the other languages still load.
                    Log.Error(e, $"{Path.GetFileName(file)} could not be read; keeping the old.");
                    continue;
                }

                // The manager remembers every (locale, source) pair it was ever handed and ignores
                // a second AddSource for one, even after RemoveSource, so a reload goes through a
                // fresh instance each time.
                if (m_Sources.TryGetValue(localeId, out var previous))
                {
                    manager.RemoveSource(localeId, previous);
                }

                var source = new TranslationFileSource(localeId, entries);
                m_Sources[localeId] = source;
                manager.AddSource(localeId, source);

                Log.Info($"Loaded {entries.Count} strings for {localeId} from {source}.");
            }
        }
    }
}
