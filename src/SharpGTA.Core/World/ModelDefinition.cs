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
            FirstDamaged = FindFirstDamaged(this.lodDistances);
            Flags = flags;
        }

        public int Id { get; private set; }
        public ModelKind Kind { get; private set; }
        public string Name { get; private set; }
        public string Txd { get; private set; }
        public IList<float> LodDistances { get { return lodDistances; } }
        public uint Flags { get; private set; }

        /// <summary>Original m_firstDamaged derivation from non-increasing distances.</summary>
        public int FirstDamaged { get; private set; }

        public float LargestLodDistance
        {
            get
            {
                if (lodDistances.Count == 0)
                    return 0.0f;
                int index = FirstDamaged == 0 ? lodDistances.Count - 1 : FirstDamaged - 1;
                return lodDistances[index];
            }
        }

        /// <summary>Non-LOD pair found by the original suffix-after-three rule.</summary>
        public ModelDefinition RelatedModel { get; internal set; }

        /// <summary>Distance at which a LOD proxy replaces its related non-LOD model.</summary>
        public float LodNearDistance { get; internal set; }

        /// <summary>The latest collision attached by model name, or null.</summary>
        public ColModel Collision { get; internal set; }

        private static int FindFirstDamaged(IList<float> distances)
        {
            if (distances.Count <= 1)
                return 0;
            if (distances[0] >= distances[1])
                return 1;
            if (distances.Count >= 3 && distances[1] >= distances[2])
                return 2;
            return 0;
        }
    }
}
