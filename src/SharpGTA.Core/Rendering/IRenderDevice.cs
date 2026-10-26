using System;

namespace SharpGTA.Rendering
{
    public interface IRenderDevice : IDisposable
    {
        string Name { get; }
        void Render(RenderSceneSnapshot snapshot);
    }

    /// <summary>Validates managed render data; no window, native library or GPU is touched.</summary>
    public sealed class NullRenderDevice : IRenderDevice
    {
        private bool disposed;

        public string Name { get { return "null"; } }
        public RenderFrameStatistics LastStatistics { get; private set; }
        public int FrameCount { get; private set; }

        public void Render(RenderSceneSnapshot snapshot)
        {
            if (disposed)
                throw new ObjectDisposedException("NullRenderDevice");
            if (snapshot == null)
                throw new ArgumentNullException("snapshot");
            RenderFrameStatistics statistics = new RenderFrameStatistics
            {
                DrawCallCount = snapshot.DrawCalls.Count
            };
            statistics.MeshCount = snapshot.DrawCalls.Count;
            for (int i = 0; i < snapshot.DrawCalls.Count; i++)
            {
                RenderDrawCall drawCall = snapshot.DrawCalls[i];
                if (drawCall.Geometry == null || drawCall.Mesh == null ||
                    drawCall.Material == null || drawCall.Mesh.MaterialIndex < 0)
                    statistics.InvalidReferenceCount++;
                else
                {
                    statistics.TriangleCount += drawCall.Mesh.Indices.Length / 3;
                    statistics.VertexCount += drawCall.Mesh.Indices.Length;
                }
            }
            LastStatistics = statistics;
            FrameCount++;
        }

        public void Dispose()
        {
            disposed = true;
        }
    }
}
