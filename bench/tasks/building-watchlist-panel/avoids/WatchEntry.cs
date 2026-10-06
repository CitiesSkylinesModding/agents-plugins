using System;
using Colossal.UI.Binding;
using Unity.Entities;

namespace BuildingWatchlist
{
    // The binding layer resolves a writer only from its registry, from IJsonWritable, or for a
    // collection of either; a type with neither has no writer and the binding throws when built.
    public struct WatchEntry : IJsonWritable, IEquatable<WatchEntry>
    {
        public Entity entity;

        public string name;

        public bool powered;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin(Mod.Id + ".WatchEntry");
            writer.PropertyName("entity");
            writer.Write(entity);
            writer.PropertyName("name");
            writer.Write(name);
            writer.PropertyName("powered");
            writer.Write(powered);
            writer.TypeEnd();
        }

        public bool Equals(WatchEntry other)
        {
            return entity == other.entity && name == other.name && powered == other.powered;
        }
    }
}
