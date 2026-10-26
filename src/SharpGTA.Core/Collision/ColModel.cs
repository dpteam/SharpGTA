using SharpGTA.Mathematics;

namespace SharpGTA.Collision
{
    /// <summary>A legacy GTA III/Vice City collision model in local coordinates.</summary>
    public sealed class ColModel
    {
        internal ColModel(string modelName, BoundingSphere boundingSphere, BoundingBox boundingBox,
            ColSphere[] spheres, ColLine[] lines, ColBox[] boxes, Vector3[] vertices,
            ColTriangle[] triangles)
        {
            ModelName = modelName;
            BoundingSphere = boundingSphere;
            BoundingBox = boundingBox;
            Spheres = spheres;
            Lines = lines;
            Boxes = boxes;
            Vertices = vertices;
            Triangles = triangles;
        }

        public string ModelName { get; private set; }
        public BoundingSphere BoundingSphere { get; private set; }
        public BoundingBox BoundingBox { get; private set; }
        public ColSphere[] Spheres { get; private set; }
        public ColLine[] Lines { get; private set; }
        public ColBox[] Boxes { get; private set; }
        public Vector3[] Vertices { get; private set; }
        public ColTriangle[] Triangles { get; private set; }

        /// <summary>The collision-store level, assigned by the caller.</summary>
        public byte Level { get; set; }

        /// <summary>Alias for Level, matching the COLL store terminology.</summary>
        public byte Slot
        {
            get { return Level; }
            set { Level = value; }
        }

        /// <summary>Returns the closest local-space sphere, box, or triangle hit.</summary>
        public bool RayCast(Ray ray, out ColHit hit)
        {
            return CollisionGeometry.RayCast(this, ray, out hit);
        }

        /// <summary>
        /// Casts downward from maxZ to minZ at local x/y and returns the closest hit.
        /// The two z bounds may be supplied in either order.
        /// </summary>
        public bool QueryVertical(float x, float y, float minZ, float maxZ, out ColHit hit)
        {
            return CollisionGeometry.QueryVertical(this, x, y, minZ, maxZ, out hit);
        }
    }
}
