using System.Collections.Generic;
using Colossal;

namespace MilestoneNotifier
{
    /// <summary>
    /// The strings of one translation file, as read at one point in time.
    /// A reload constructs a new instance rather than refreshing this one.
    /// </summary>
    public class TranslationFileSource : IDictionarySource
    {
        private readonly string m_LocaleId;
        private readonly Dictionary<string, string> m_Entries;

        public TranslationFileSource(string localeId, Dictionary<string, string> entries)
        {
            m_LocaleId = localeId;
            m_Entries = entries;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return m_Entries;
        }

        public void Unload()
        {
        }

        // The localization log names a source by this alone when an import fails.
        public override string ToString() => $"MilestoneNotifier.Translations.{m_LocaleId}";
    }
}
