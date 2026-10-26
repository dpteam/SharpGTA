using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.Collision;

namespace SharpGTA.World
{
    public enum ModelKind
    {
        Simple,
        Timed,
        Clump,
        Vehicle,
        Ped,
        Weapon
    }

    /// <summary>Metadata loaded from IDE; it does not contain RenderWare geometry.</summary>
    public sealed class ModelDefinition
    {
        private readonly ReadOnlyCollection<float> lodDistances;

        internal ModelDefinition(int id, ModelKind kind, string name, string txd,
            IList<float> lodDistances, uint flags)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Txd = txd;
            this.lodDistances = new List<float>(lodDistances).AsReadOnly();
            Flags = flags;
        }

        public int Id { get; private set; }
        public ModelKind Kind { get; private set; }
        public string Name { get; private set; }
        public string Txd { get; private set; }
        public IList<float> LodDistances { get { return lodDistances; } }
        public uint Flags { get; private set; }

        /// <summary>The latest collision attached by model name, or null.</summary>
        public ColModel Collision { get; internal set; }
    }
}
