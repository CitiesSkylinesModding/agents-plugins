using System;
using Colossal.UI.Binding;
using Unity.Entities;

namespace PowerOutages
{
    public struct OutageEntry : IJsonWritable, IEquatable<OutageEntry>
    {
        public Entity entity;

        public string name;

        public OutageEntry(Entity entity, string name)
        {
            this.entity = entity;
            this.name = name;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin(typeof(OutageEntry).FullName);
            writer.PropertyName("entity");
            writer.Write(entity);
            writer.PropertyName("name");
            writer.Write(name);
            writer.TypeEnd();
        }

        public bool Equals(OutageEntry other)
        {
            return entity == other.entity && name == other.name;
        }
    }
}
