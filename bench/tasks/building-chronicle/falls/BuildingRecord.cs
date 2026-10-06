using Colossal.Serialization.Entities;
using Game;
using Unity.Entities;

namespace BuildingChronicle
{
    /// <summary>
    /// Stamped onto every building the chronicle sees, so its age survives a reload.
    /// </summary>
    public struct BuildingRecord : IComponentData, ISerializable
    {
        public const int kUnknownDay = -1;

        public int m_BuiltDay;

        // Added after the first saves were made: the city's population on the day it was built.
        public int m_BuiltPopulation;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_BuiltDay);
            writer.Write(m_BuiltPopulation);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            try
            {
                reader.Read(out m_BuiltDay);

                // Saves written before the population field existed stop after the day, so the
                // read is gated the way the game gates its own fields: on the version of the
                // build that wrote the save, against the newest build stamp the game declares.
                if (reader.context.version >= Version.garbageFeeReset)
                {
                    reader.Read(out m_BuiltPopulation);
                }
            }
            catch
            {
                // A record this build cannot read falls back to "unknown" rather than failing.
                m_BuiltDay = kUnknownDay;
                m_BuiltPopulation = 0;
            }
        }
    }
}
