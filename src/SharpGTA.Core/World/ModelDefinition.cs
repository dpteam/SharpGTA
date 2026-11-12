using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpGTA.Collision;
using SharpGTA.Runtime;

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
            IList<float> lodDistances, uint flags, int timeOn, int timeOff)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Txd = txd;
            this.lodDistances = new List<float>(lodDistances).AsReadOnly();
            FirstDamaged = FindFirstDamaged(this.lodDistances);
            Flags = flags;
            TimeOn = timeOn;
            TimeOff = timeOff;
        }

        public int Id { get; private set; }
        public ModelKind Kind { get; private set; }
        public string Name { get; private set; }
        public string Txd { get; private set; }
        public IList<float> LodDistances { get { return lodDistances; } }
        public uint Flags { get; private set; }

        /// <summary>True for IDE tobj models, whose visibility follows the game clock.</summary>
        public bool IsTimed { get { return Kind == ModelKind.Timed; } }

        /// <summary>IDE tobj hour window start; unused for non-timed models.</summary>
        public int TimeOn { get; private set; }

        /// <summary>IDE tobj hour window end, exclusive as in CClock::GetIsTimeInRange.</summary>
        public int TimeOff { get; private set; }

        /// <summary>The _nt/_dy counterpart of a timed model, resolved by name.</summary>
        public ModelDefinition OtherTimeModel { get; internal set; }

        /// <summary>Non-timed models are always in range; timed ones follow the clock.</summary>
        public bool GetIsTimeInRange(GameClock clock)
        {
            if (clock == null)
                throw new ArgumentNullException("clock");
            return !IsTimed || clock.IsTimeInRange(TimeOn, TimeOff);
        }

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
