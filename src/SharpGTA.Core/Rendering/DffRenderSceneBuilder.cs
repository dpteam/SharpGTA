using System;
using System.Collections.Generic;
using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;

namespace SharpGTA.Rendering
{
    /// <summary>Frame hierarchy and placement for static, non-skinned DFF atomics.</summary>
    public static class DffFrameTransforms
    {
        public static Matrix[] Resolve(DffClump clump)
        {
            if (clump == null)
                throw new ArgumentNullException("clump");
            if (clump.Frames == null || clump.Atomics == null)
                throw new ArgumentException("DFF clump has no frame or atomic arrays.", "clump");
            Matrix[] frameWorld = new Matrix[clump.Frames.Length];
            byte[] states = new byte[clump.Frames.Length];
            for (int i = 0; i < clump.Frames.Length; i++)
                frameWorld[i] = ResolveFrame(clump.Frames, frameWorld, states, i);
            Matrix[] result = new Matrix[clump.Atomics.Length];
            for (int i = 0; i < clump.Atomics.Length; i++)
            {
                if (clump.Atomics[i] == null)
                    throw new AssetFormatException("DFF atomic is null.");
                int frame = clump.Atomics[i].FrameIndex;
                if (frame < 0 || frame >= frameWorld.Length)
                    throw new AssetFormatException("Atomic frame index is outside the frame list.");
                result[i] = frameWorld[frame];
            }
            return result;
        }

        public static Matrix Concatenate(Matrix child, Matrix parent)
        {
            return new Matrix(
                parent.TransformDirection(child.Right),
                parent.TransformDirection(child.Forward),
                parent.TransformDirection(child.Up),
                parent.TransformPoint(child.Position));
        }

        private static Matrix ResolveFrame(DffFrame[] frames, Matrix[] world, byte[] states, int index)
        {
            if (states[index] == 1)
                return world[index];
            if (states[index] == 2)
                throw new AssetFormatException("Cycle in DFF frame hierarchy.");
            states[index] = 2;
            DffFrame frame = frames[index];
            if (frame == null)
                throw new AssetFormatException("DFF frame is null.");
            Matrix result = frame.LocalTransform;
            if (frame.ParentIndex < -1)
                throw new AssetFormatException("Frame parent must be -1 or refer to another frame.");
            if (frame.ParentIndex >= 0)
            {
                if (frame.ParentIndex >= frames.Length)
                    throw new AssetFormatException("Frame parent is outside the frame list.");
                result = Concatenate(result, ResolveFrame(frames, world, states, frame.ParentIndex));
            }
            world[index] = result;
            states[index] = 1;
            return result;
        }
    }

    public static class DffRenderSceneBuilder
    {
        public static RenderSceneSnapshot Build(GeometryRenderData[] atomics,
            Matrix[] atomicTransforms, Matrix placement)
        {
            if (atomics == null)
                throw new ArgumentNullException("atomics");
            if (atomicTransforms == null)
                throw new ArgumentNullException("atomicTransforms");
            if (atomics.Length != atomicTransforms.Length)
                throw new ArgumentException("Atomic transforms must match converted atomics.", "atomicTransforms");
            List<RenderDrawCall> calls = new List<RenderDrawCall>();
            for (int i = 0; i < atomics.Length; i++)
            {
                GeometryRenderData model = atomics[i];
                if (model == null)
                    throw new ArgumentException("Converted atomic is null.", "atomics");
                Matrix world = DffFrameTransforms.Concatenate(atomicTransforms[i], placement);
                for (int j = 0; j < model.Meshes.Length; j++)
                {
                    MeshData mesh = model.Meshes[j];
                    MaterialData material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Length
                        ? model.Materials[mesh.MaterialIndex]
                        : null;
                    calls.Add(new RenderDrawCall(model, mesh, material, world));
                }
            }
            return new RenderSceneSnapshot(calls);
        }

        public static BoundingBox ComputeBounds(GeometryRenderData[] atomics, Matrix[] atomicTransforms)
        {
            if (atomics == null)
                throw new ArgumentNullException("atomics");
            if (atomicTransforms == null)
                throw new ArgumentNullException("atomicTransforms");
            if (atomics.Length != atomicTransforms.Length)
                throw new ArgumentException("Atomic transforms must match converted atomics.", "atomicTransforms");
            bool initialized = false;
            Vector3 minimum = Vector3.Zero;
            Vector3 maximum = Vector3.Zero;
            for (int i = 0; i < atomics.Length; i++)
            {
                RenderVertex[] vertices = atomics[i].Vertices;
                Matrix transform = atomicTransforms[i];
                for (int j = 0; j < vertices.Length; j++)
                {
                    Vector3 point = transform.TransformPoint(vertices[j].Position);
                    if (!initialized)
                    {
                        minimum = point;
                        maximum = point;
                        initialized = true;
                    }
                    else
                    {
                        minimum = Vector3.Min(minimum, point);
                        maximum = Vector3.Max(maximum, point);
                    }
                }
            }
            if (!initialized)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);
            return new BoundingBox(minimum, maximum);
        }
    }
}
