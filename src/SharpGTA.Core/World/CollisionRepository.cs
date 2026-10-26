using System;
using System.Collections.Generic;
using SharpGTA.Collision;

namespace SharpGTA.World
{
    /// <summary>Case-insensitive collision store attached to a model registry.</summary>
    public sealed class CollisionRepository
    {
        private readonly ModelRegistry registry;
        private readonly Dictionary<string, ColModel> byName =
            new Dictionary<string, ColModel>(StringComparer.OrdinalIgnoreCase);

        public CollisionRepository(ModelRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException("registry");
            this.registry = registry;
        }

        public int FileCount { get; private set; }
        public int LoadedModelCount { get; private set; }
        public int ReplacementCount { get; private set; }
        public int LastFileModelCount { get; private set; }
        public int ModelCount { get { return byName.Count; } }
        public int Count { get { return ModelCount; } }

        /// <summary>
        /// Reads one COLL file and assigns its level to every model. If a name occurs
        /// again, the last model wins and is reattached to the registry.
        /// </summary>
        public int Add(byte[] collBytes, byte level)
        {
            if (collBytes == null)
                throw new ArgumentNullException("collBytes");
            return Add(ColFile.Read(collBytes), level);
        }

        public int Add(List<ColModel> models, byte level)
        {
            if (models == null)
                throw new ArgumentNullException("models");
            LastFileModelCount = models.Count;
            for (int i = 0; i < models.Count; i++)
            {
                ColModel model = models[i];
                model.Level = level;
                if (byName.ContainsKey(model.ModelName))
                    ReplacementCount++;
                byName[model.ModelName] = model;
                registry.AttachCollision(model);
            }
            FileCount++;
            LoadedModelCount += models.Count;
            return models.Count;
        }

        public ColModel Find(string name)
        {
            if (name == null)
                throw new ArgumentNullException("name");
            ColModel model;
            return byName.TryGetValue(name, out model) ? model : null;
        }

        public bool TryGet(string name, out ColModel model)
        {
            model = Find(name);
            return model != null;
        }
    }
}
