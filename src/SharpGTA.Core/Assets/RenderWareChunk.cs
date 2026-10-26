using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SharpGTA.Assets
{
    /// <summary>Bounds and child inventory for one RenderWare stream chunk.</summary>
    public sealed class RenderWareChunk
    {
        private static readonly ReadOnlyCollection<RenderWareChunk> NoChildren =
            new List<RenderWareChunk>().AsReadOnly();

        private readonly ReadOnlyCollection<RenderWareChunk> children;

        internal RenderWareChunk(uint id, uint version, int chunkOffset, int payloadOffset, int dataLength, List<RenderWareChunk> children)
        {
            Id = id;
            Version = version;
            ChunkOffset = chunkOffset;
            PayloadOffset = payloadOffset;
            DataLength = dataLength;
            IsKnownContainer = children != null;
            this.children = children == null ? NoChildren : children.AsReadOnly();
        }

        public uint Id { get; private set; }
        public uint Version { get; private set; }
        public int ChunkOffset { get; private set; }
        public int PayloadOffset { get; private set; }
        public int DataLength { get; private set; }

        /// <summary>
        /// True only when the generic chunk-child layout is known and was traversed.
        /// False for STRUCT, leaves, and unknown/plugin payloads (which are not guessed at).
        /// </summary>
        public bool IsKnownContainer { get; private set; }

        public ReadOnlyCollection<RenderWareChunk> Children
        {
            get { return children; }
        }
    }
}
