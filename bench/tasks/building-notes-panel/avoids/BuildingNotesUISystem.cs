using System.Collections.Generic;
using System.IO;
using System.Text;
using Colossal.PSI.Environment;
using Colossal.UI.Binding;
using Game;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace BuildingNotes
{
    public partial class BuildingNotesUISystem : UISystemBase
    {
        private NameSystem m_NameSystem;
        private SelectedInfoUISystem m_SelectedInfoUISystem;
        private readonly Dictionary<Entity, string> m_Notes = new Dictionary<Entity, string>();
        private ValueBinding<string> m_SelectedNote;

        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();

            // A building without a note has no text at all, and the registered string writer
            // throws on null; the nullable wrapper writes the null the frontend reads as "none".
            AddBinding(m_SelectedNote = new ValueBinding<string>(Mod.Id, "selectedNote", null,
                ValueWriters.Nullable(new StringWriter())));

            AddBinding(new TriggerBinding<string>(Mod.Id, "setNote", SetNote));
            AddBinding(new TriggerBinding(Mod.Id, "clearNote", ClearNote));

            // The export's answer is a one-shot the frontend asked for, so it comes back on the
            // call's own promise rather than on a pushed value, which would skip an answer equal
            // to the last one. The enum resolves no reader of its own, hence the explicit one.
            AddBinding(new CallBinding<ExportFormat, string>(Mod.Id, "export", Export,
                new EnumReader<ExportFormat>()));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            var selected = m_SelectedInfoUISystem.selectedEntity;

            m_Notes.TryGetValue(selected, out var note);
            m_SelectedNote.Update(note);
        }

        private void SetNote(string text)
        {
            var selected = m_SelectedInfoUISystem.selectedEntity;

            if (selected == Entity.Null)
            {
                return;
            }

            m_Notes[selected] = text;
        }

        private void ClearNote()
        {
            m_Notes.Remove(m_SelectedInfoUISystem.selectedEntity);
        }

        private string Export(ExportFormat format)
        {
            var directory = Path.Combine(EnvPath.kUserDataPath, "ModsData", Mod.Id);
            var path = Path.Combine(directory, "notes" + Extension(format));
            var builder = new StringBuilder();

            if (format == ExportFormat.Csv)
            {
                builder.AppendLine("building,note");
            }

            foreach (var pair in m_Notes)
            {
                var name = m_NameSystem.GetRenderedLabelName(pair.Key);

                switch (format)
                {
                    case ExportFormat.Csv:
                        builder.AppendLine(Quote(name) + "," + Quote(pair.Value));
                        break;
                    case ExportFormat.Markdown:
                        builder.AppendLine("- **" + name + "**: " + pair.Value);
                        break;
                    default:
                        builder.AppendLine(name + ": " + pair.Value);
                        break;
                }
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, builder.ToString());

            return $"Wrote {m_Notes.Count} notes to {path}";
        }

        private static string Extension(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Csv:
                    return ".csv";
                case ExportFormat.Markdown:
                    return ".md";
                default:
                    return ".txt";
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
