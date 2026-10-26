using System;
using System.Collections.Generic;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Assets
{
    /// <summary>
    /// CPU-side reader for platform-independent GTA III/Vice City DFF clumps.
    /// Native geometry and plugin behavior are not guessed; unknown extensions are skipped.
    /// </summary>
    public static class DffReader
    {
        public const int MaximumInputLength = 256 * 1024 * 1024;
        private const int MaximumCollectionCount = 1000000;
        private const uint GeometryNative = 0x01000000;
        private const uint GeometryTextured = 0x04;
        private const uint GeometryPrelit = 0x08;
        private const uint GeometryTextured2 = 0x80;

        public static DffDocument Read(byte[] data)
        {
            return Read(data, 0, data == null ? 0 : data.Length);
        }

        public static DffDocument Read(byte[] data, int offset, int count)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(offset < 0 ? "offset" : "count");
            if (count > MaximumInputLength)
                throw new AssetFormatException("DFF input exceeds the configured size limit.");
            return new Parser(data, offset, count).ReadDocument();
        }

        public static DffClump ReadClump(byte[] data)
        {
            DffDocument document = Read(data);
            if (document.Clumps.Length != 1)
                throw new AssetFormatException("Expected exactly one DFF clump, found " + document.Clumps.Length + ".");
            return document.Clumps[0];
        }

        private sealed class Parser
        {
            private readonly byte[] data;
            private readonly int offset;
            private readonly int count;

            internal Parser(byte[] data, int offset, int count)
            {
                this.data = data;
                this.offset = offset;
                this.count = count;
            }

            internal DffDocument ReadDocument()
            {
                RenderWareChunkReader root = new RenderWareChunkReader(data, offset, count);
                List<DffClump> clumps = new List<DffClump>();
                while (root.Remaining != 0)
                {
                    if (root.Remaining < 12)
                    {
                        if (clumps.Count != 0 && root.IsZeroPadding())
                            break;
                        throw Error("Truncated root RenderWare chunk header.", root.Position);
                    }
                    if (root.PeekId() != RenderWareChunkTypes.Clump)
                    {
                        if (clumps.Count != 0 && root.IsZeroPadding())
                            break;
                        throw Error("DFF root is not a Clump chunk.", root.Position);
                    }
                    clumps.Add(ReadClump(root));
                }
                if (clumps.Count == 0)
                    throw Error("DFF contains no clumps.", offset);
                return new DffDocument { Clumps = clumps.ToArray() };
            }

            private DffClump ReadClump(RenderWareChunkReader root)
            {
                RenderWareStreamChunk clumpChunk = root.ReadExpected(RenderWareChunkTypes.Clump, "clump");
                // Some retail III clumps have a stale parent length. As in librw/re3,
                // counts and child chunks drive the actual end within the outer entry.
                RenderWareChunkReader children = new RenderWareChunkReader(data,
                    clumpChunk.PayloadOffset, root.End - clumpChunk.PayloadOffset);
                RenderWareStreamChunk clumpStruct = children.ReadExpected(RenderWareChunkTypes.Struct, "clump.struct");
                int atomicCount = ReadCount(clumpStruct.Payload, 0, MaximumCollectionCount, "atomics");
                int lightCount = 0;
                int cameraCount = 0;
                if (clumpStruct.Version >= 0x33000)
                {
                    lightCount = ReadCount(clumpStruct.Payload, 0, MaximumCollectionCount, "lights");
                    cameraCount = ReadCount(clumpStruct.Payload, 0, MaximumCollectionCount, "cameras");
                }
                clumpStruct.FinishPayload("clump.struct");

                DffFrame[] frames = ReadFrameList(children);
                List<DffGeometry> geometries = new List<DffGeometry>();
                if (clumpStruct.Version >= 0x30400 || children.PeekId() == RenderWareChunkTypes.GeometryList)
                    ReadGeometryList(children, geometries);
                DffAtomic[] atomics = new DffAtomic[atomicCount];
                for (int i = 0; i < atomics.Length; i++)
                    atomics[i] = ReadAtomic(children, frames, geometries, i);
                SkipLightsAndCameras(children, frames.Length, lightCount, cameraCount);
                SkipExtension(children, "clump.extension");
                int actualLength = children.Position - clumpChunk.ChunkOffset;
                int declaredLength = clumpChunk.PayloadLength + 12;
                root.Seek(clumpChunk.ChunkOffset + actualLength);
                return new DffClump
                {
                    Version = clumpStruct.Version,
                    LightCount = lightCount,
                    CameraCount = cameraCount,
                    Frames = frames,
                    Geometries = geometries.ToArray(),
                    Atomics = atomics,
                    DeclaredLength = declaredLength,
                    ActualLength = actualLength,
                    HasDeclaredLengthMismatch = declaredLength != actualLength
                };
            }

            private static void SkipLightsAndCameras(RenderWareChunkReader children,
                int frameCount, int lightCount, int cameraCount)
            {
                for (int i = 0; i < lightCount + cameraCount; i++)
                {
                    RenderWareStreamChunk frame = children.ReadExpected(RenderWareChunkTypes.Struct, "clump attachment frame");
                    int frameIndex = frame.Payload.ReadInt32();
                    if (frameIndex < 0 || frameIndex >= frameCount)
                        throw Error("Clump light/camera frame index is outside the frame list.", frame.ChunkOffset);
                    frame.FinishPayload("clump attachment frame struct");
                    uint expected = i < lightCount ? RenderWareChunkTypes.Light : RenderWareChunkTypes.Camera;
                    children.ReadExpected(expected, i < lightCount ? "clump light" : "clump camera");
                }
            }

            private DffFrame[] ReadFrameList(RenderWareChunkReader children)
            {
                RenderWareStreamChunk list = children.ReadExpected(RenderWareChunkTypes.FrameList, "frame list");
                RenderWareChunkReader content = Content(list, "frame list");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "frameList.struct");
                int count = ReadCount(header.Payload, 1, MaximumCollectionCount, "frames");
                DffFrame[] frames = new DffFrame[count];
                for (int i = 0; i < count; i++)
                {
                    Vector3 right = ReadVector3(header.Payload);
                    Vector3 forward = ReadVector3(header.Payload);
                    Vector3 up = ReadVector3(header.Payload);
                    Vector3 position = ReadVector3(header.Payload);
                    int parent = header.Payload.ReadInt32();
                    int creationFlags = header.Payload.ReadInt32();
                    if (parent >= i || parent < -1)
                        throw Error("Frame parent must be -1 or refer to an earlier frame.", header.ChunkOffset);
                    frames[i] = new DffFrame
                    {
                        LocalTransform = new Matrix(right, forward, up, position),
                        ParentIndex = parent,
                        CreationFlags = creationFlags
                    };
                }
                header.FinishPayload("frameList.struct");
                for (int i = 0; i < count; i++)
                {
                    RenderWareStreamChunk extension = content.ReadExpected(RenderWareChunkTypes.Extension, "frame extension");
                    RenderWareChunkReader extensions = Content(extension, "frame extension");
                    while (extensions.Remaining != 0)
                    {
                        RenderWareStreamChunk plugin = extensions.Read("frame plugin");
                        if (plugin.Id == RenderWareChunkTypes.NodeName)
                            frames[i].Name = plugin.Payload.ReadFixedAscii(plugin.PayloadLength);
                    }
                }
                content.Finish("frame list");
                return frames;
            }

            private void ReadGeometryList(RenderWareChunkReader children, List<DffGeometry> geometries)
            {
                RenderWareStreamChunk list = children.ReadExpected(RenderWareChunkTypes.GeometryList, "geometry list");
                RenderWareChunkReader content = Content(list, "geometry list");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "geometryList.struct");
                int count = ReadCount(header.Payload, 0, MaximumCollectionCount, "geometries");
                header.FinishPayload("geometryList.struct");
                for (int i = 0; i < count; i++)
                    geometries.Add(ReadGeometry(content, i));
                content.Finish("geometry list");
            }

            private DffAtomic ReadAtomic(RenderWareChunkReader children, DffFrame[] frames,
                List<DffGeometry> geometries, int atomicIndex)
            {
                RenderWareStreamChunk atomic = children.ReadExpected(RenderWareChunkTypes.Atomic, "atomic[" + atomicIndex + "]");
                RenderWareChunkReader content = Content(atomic, "atomic");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "atomic.struct");
                int frameIndex = header.Payload.ReadInt32();
                if (frameIndex < 0 || frameIndex >= frames.Length)
                    throw Error("Atomic frame index is outside the frame list.", header.ChunkOffset);
                int geometryIndex = -1;
                int flags;
                int unused;
                if (header.Version < 0x30400)
                {
                    flags = header.Payload.ReadInt32();
                    unused = header.Payload.ReadInt32();
                }
                else
                {
                    geometryIndex = header.Payload.ReadInt32();
                    flags = header.Payload.ReadInt32();
                    unused = header.Payload.ReadInt32();
                }
                header.FinishPayload("atomic.struct");

                DffGeometry geometry;
                if (header.Version < 0x30400 || childrenNextIsGeometry(content))
                {
                    geometryIndex = geometries.Count;
                    geometry = ReadGeometry(content, geometryIndex);
                    geometries.Add(geometry);
                }
                else
                {
                    if (geometryIndex < 0 || geometryIndex >= geometries.Count)
                        throw Error("Atomic geometry index is outside the geometry list.", header.ChunkOffset);
                    geometry = geometries[geometryIndex];
                }
                SkipExtension(content, "atomic.extension");
                content.Finish("atomic");
                return new DffAtomic
                {
                    FrameIndex = frameIndex,
                    GeometryIndex = geometryIndex,
                    Flags = flags,
                    Unused = unused,
                    Geometry = geometry
                };
            }

            private static bool childrenNextIsGeometry(RenderWareChunkReader content)
            {
                return content.PeekId() == RenderWareChunkTypes.Geometry;
            }

            private DffGeometry ReadGeometry(RenderWareChunkReader children, int geometryIndex)
            {
                RenderWareStreamChunk geometry = children.ReadExpected(RenderWareChunkTypes.Geometry, "geometry[" + geometryIndex + "]");
                RenderWareChunkReader content = Content(geometry, "geometry");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "geometry.struct");
                ByteReader reader = header.Payload;
                uint format = reader.ReadUInt32();
                if ((format & GeometryNative) != 0)
                    throw Error("Native/pre-instanced DFF geometry is not implemented by the static reader.", header.ChunkOffset);
                int triangleCount = ReadCount(reader, 0, MaximumCollectionCount, "triangles");
                int vertexCount = ReadCount(reader, 0, MaximumCollectionCount, "vertices");
                int morphCount = ReadCount(reader, 1, 1024, "morph targets");
                bool hasLegacySurface = header.Version < 0x34000;
                DffSurfaceProperties legacySurface = new DffSurfaceProperties(1.0f, 1.0f, 1.0f);
                if (hasLegacySurface)
                    legacySurface = ReadSurface(reader);
                DffColor[] colors = null;
                if ((format & GeometryPrelit) != 0)
                {
                    colors = new DffColor[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        colors[i] = new DffColor(reader.ReadByte(), reader.ReadByte(),
                            reader.ReadByte(), reader.ReadByte());
                    }
                }
                int uvCount = (int)((format >> 16) & 0xFFu);
                if (uvCount == 0)
                {
                    if ((format & GeometryTextured2) != 0)
                        uvCount = 2;
                    else if ((format & GeometryTextured) != 0)
                        uvCount = 1;
                }
                if (uvCount > 8)
                    throw Error("DFF geometry has more than 8 texture coordinate sets.", header.ChunkOffset);
                Vector2[][] texCoords = new Vector2[uvCount][];
                for (int set = 0; set < uvCount; set++)
                {
                    texCoords[set] = new Vector2[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                        texCoords[set][i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                DffTriangle[] triangles = new DffTriangle[triangleCount];
                for (int i = 0; i < triangleCount; i++)
                {
                    ushort b = reader.ReadUInt16();
                    ushort a = reader.ReadUInt16();
                    short material = unchecked((short)reader.ReadUInt16());
                    ushort c = reader.ReadUInt16();
                    if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                        throw Error("DFF triangle vertex index is outside the vertex array.", header.ChunkOffset);
                    triangles[i] = new DffTriangle(a, b, c, material);
                }
                DffMorphTarget[] morphTargets = new DffMorphTarget[morphCount];
                for (int i = 0; i < morphCount; i++)
                {
                    Vector3 center = ReadVector3(reader);
                    float radius = reader.ReadSingle();
                    bool hasVertices = reader.ReadInt32() != 0;
                    bool hasNormals = reader.ReadInt32() != 0;
                    morphTargets[i] = new DffMorphTarget
                    {
                        BoundingSphere = new BoundingSphere(center, radius),
                        Vertices = hasVertices ? ReadVectors(reader, vertexCount) : null,
                        Normals = hasNormals ? ReadVectors(reader, vertexCount) : null
                    };
                }
                header.FinishPayload("geometry.struct");

                DffMaterial[] materials = ReadMaterialList(content, legacySurface);
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i].MaterialIndex < -1 || triangles[i].MaterialIndex >= materials.Length)
                        throw Error("DFF triangle material index is outside the material list.", geometry.ChunkOffset);
                }
                DffMesh[] meshes = ReadGeometryExtension(content, materials.Length, vertexCount);
                content.Finish("geometry");
                return new DffGeometry
                {
                    Format = format,
                    PrelitColors = colors,
                    TexCoords = texCoords,
                    Triangles = triangles,
                    MorphTargets = morphTargets,
                    Materials = materials,
                    Meshes = meshes,
                    HasLegacySurfaceProperties = hasLegacySurface,
                    LegacySurfaceProperties = legacySurface
                };
            }

            private DffMaterial[] ReadMaterialList(RenderWareChunkReader children,
                DffSurfaceProperties legacySurface)
            {
                RenderWareStreamChunk list = children.ReadExpected(RenderWareChunkTypes.MaterialList, "material list");
                RenderWareChunkReader content = Content(list, "material list");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "materialList.struct");
                int count = ReadCount(header.Payload, 0, MaximumCollectionCount, "materials");
                int[] sources = new int[count];
                for (int i = 0; i < count; i++)
                    sources[i] = header.Payload.ReadInt32();
                header.FinishPayload("materialList.struct");
                DffMaterial[] materials = new DffMaterial[count];
                for (int i = 0; i < count; i++)
                {
                    if (sources[i] < 0)
                        materials[i] = ReadMaterial(content, legacySurface);
                    else if (sources[i] < i)
                        materials[i] = materials[sources[i]];
                    else
                        throw Error("Material list source must refer to an earlier slot.", header.ChunkOffset);
                }
                content.Finish("material list");
                return materials;
            }

            private DffMaterial ReadMaterial(RenderWareChunkReader children,
                DffSurfaceProperties legacySurface)
            {
                RenderWareStreamChunk material = children.ReadExpected(RenderWareChunkTypes.Material, "material");
                RenderWareChunkReader content = Content(material, "material");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "material.struct");
                ByteReader reader = header.Payload;
                int flags = reader.ReadInt32();
                DffColor color = new DffColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                int unused = reader.ReadInt32();
                int textured = reader.ReadInt32();
                DffSurfaceProperties surface = header.Version >= 0x30400 ? ReadSurface(reader) : legacySurface;
                header.FinishPayload("material.struct");
                DffTextureReference texture = textured != 0 ? ReadTexture(content) : null;
                SkipExtension(content, "material.extension");
                content.Finish("material");
                return new DffMaterial
                {
                    Flags = flags,
                    Unused = unused,
                    Textured = textured,
                    Color = color,
                    SurfaceProperties = surface,
                    Texture = texture
                };
            }

            private DffTextureReference ReadTexture(RenderWareChunkReader children)
            {
                RenderWareStreamChunk texture = children.ReadExpected(RenderWareChunkTypes.Texture, "texture reference");
                RenderWareChunkReader content = Content(texture, "texture reference");
                RenderWareStreamChunk header = content.ReadExpected(RenderWareChunkTypes.Struct, "texture.struct");
                uint filter = header.Payload.ReadUInt32();
                header.FinishPayload("texture.struct");
                string name = ReadString(content, "texture name");
                string mask = ReadString(content, "texture mask name");
                SkipExtension(content, "texture.extension");
                content.Finish("texture reference");
                return new DffTextureReference { FilterAddressing = filter, Name = name, MaskName = mask };
            }

            private DffMesh[] ReadGeometryExtension(RenderWareChunkReader children,
                int materialCount, int vertexCount)
            {
                RenderWareStreamChunk extension = children.ReadExpected(RenderWareChunkTypes.Extension, "geometry.extension");
                RenderWareChunkReader content = Content(extension, "geometry.extension");
                DffMesh[] meshes = null;
                while (content.Remaining != 0)
                {
                    RenderWareStreamChunk plugin = content.Read("geometry plugin");
                    if (plugin.Id == RenderWareChunkTypes.BinMesh)
                    {
                        if (meshes != null)
                            throw Error("Geometry contains more than one BinMesh plugin.", plugin.ChunkOffset);
                        meshes = ReadBinMesh(plugin, materialCount, vertexCount);
                    }
                }
                content.Finish("geometry.extension");
                return meshes ?? new DffMesh[0];
            }

            private DffMesh[] ReadBinMesh(RenderWareStreamChunk plugin,
                int materialCount, int vertexCount)
            {
                ByteReader reader = plugin.Payload;
                uint flags = reader.ReadUInt32();
                int meshCount = ReadCount(reader, 0, MaximumCollectionCount, "meshes");
                int totalIndices = ReadCount(reader, 0, MaximumCollectionCount * 8, "mesh indices");
                DffMesh[] meshes = new DffMesh[meshCount];
                int actualIndices = 0;
                for (int i = 0; i < meshCount; i++)
                {
                    int indexCount = ReadCount(reader, 0, totalIndices - actualIndices, "mesh indices");
                    int materialIndex = reader.ReadInt32();
                    if (materialIndex < 0 || materialIndex >= materialCount)
                        throw Error("BinMesh material index is outside the material list.", plugin.ChunkOffset);
                    ushort[] indices = new ushort[indexCount];
                    for (int j = 0; j < indexCount; j++)
                    {
                        uint index = reader.ReadUInt32();
                        if (index >= (uint)vertexCount || index > ushort.MaxValue)
                            throw Error("BinMesh vertex index is outside the vertex array.", plugin.ChunkOffset);
                        indices[j] = (ushort)index;
                    }
                    actualIndices += indexCount;
                    meshes[i] = new DffMesh { Flags = flags, MaterialIndex = materialIndex, Indices = indices };
                }
                if (actualIndices != totalIndices)
                    throw Error("BinMesh total index count does not match its meshes.", plugin.ChunkOffset);
                plugin.FinishPayload("BinMesh");
                return meshes;
            }

            private static string ReadString(RenderWareChunkReader children, string context)
            {
                RenderWareStreamChunk text = children.ReadExpected(RenderWareChunkTypes.String, context);
                return text.Payload.ReadFixedAscii(text.PayloadLength);
            }

            private static void SkipExtension(RenderWareChunkReader children, string context)
            {
                children.ReadExpected(RenderWareChunkTypes.Extension, context);
            }

            private static RenderWareChunkReader Content(RenderWareStreamChunk chunk, string context)
            {
                return new RenderWareChunkReader(chunkPayload(chunk), chunk.PayloadOffset, chunk.PayloadLength);
            }

            // ByteReader does not expose its immutable backing buffer by design. The
            // reader carries it through this payload copy-free side field instead.
            private static byte[] chunkPayload(RenderWareStreamChunk chunk)
            {
                return chunk.Buffer;
            }

            private static int ReadCount(ByteReader reader, int minimum, int maximum, string description)
            {
                int value = reader.ReadInt32();
                if (value < minimum || value > maximum)
                {
                    throw new AssetFormatException("Invalid " + description + " count " + value +
                        "; expected [" + minimum + ", " + maximum + "].");
                }
                return value;
            }

            private static Vector3[] ReadVectors(ByteReader reader, int count)
            {
                Vector3[] values = new Vector3[count];
                for (int i = 0; i < count; i++)
                    values[i] = ReadVector3(reader);
                return values;
            }

            private static Vector3 ReadVector3(ByteReader reader)
            {
                return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            private static DffSurfaceProperties ReadSurface(ByteReader reader)
            {
                return new DffSurfaceProperties(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            private static AssetFormatException Error(string message, int position)
            {
                return new AssetFormatException(message, position);
            }
        }
    }
}
