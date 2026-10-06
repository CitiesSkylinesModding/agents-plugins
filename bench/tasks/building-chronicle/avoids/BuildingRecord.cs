using Colossal.Serialization.Entities;
using Unity.Entities;

namespace BuildingChronicle
{
    /// <summary>
    /// Stamped onto every building the chronicle sees, so its age survives a reload.
    /// </summary>
    public struct BuildingRecord : IComponentData, ISerializable
    {
        // The layout version, written ahead of the fields. The game's own reader.context.version
        // is the game build that wrote the save and says nothing about this mod's revision.
        // Bump it on every change to the fields below.
        public const int kVersion = 1;

        public const int kUnknownDay = -1;

        public int m_BuiltDay;

        public int m_BuiltPopulation;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write(m_BuiltDay);
            writer.Write(m_BuiltPopulation);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);

            reader.Read(out m_BuiltDay);
            reader.Read(out m_BuiltPopulation);

            // A field added later reads behind its own `if (version >= 2)` block, and nothing
            // here ever returns early or catches: the block is framed once per archetype, so a
            // record read short shifts every later entity's bytes, and a size mismatch is
            // detected by the caller after this method has returned.
        }
    }
}
