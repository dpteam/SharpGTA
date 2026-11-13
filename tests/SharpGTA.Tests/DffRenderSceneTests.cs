using SharpGTA.Assets;
using SharpGTA.IO;
using SharpGTA.Mathematics;
using SharpGTA.Rendering;

namespace SharpGTA.Tests
{
    public sealed class DffRenderSceneTests
    {
        [Test]
        public void AtomicFrameIndicesAndParentTransformsSurviveConversion()
        {
            DffClump source = DffReader.ReadClump(DffTestData.Create(0x34003u));
            GeometryRenderData[] converted = DffRenderConverter.ConvertClump(source, "model");
            Assert.Equal(source.Atomics[0].FrameIndex, converted[0].AtomicFrameIndex);

            Matrix parent = Matrix.CreateRotationZ((float)(System.Math.PI / 2.0));
            parent.Position = new Vector3(10, 0, 0);
            Matrix child = Matrix.CreateTranslation(1, 0, 0);
            Matrix world = DffFrameTransforms.Concatenate(child, parent);
            Vector3 transformed = world.TransformPoint(Vector3.Zero);
            Assert.Near(10, transformed.X);
            Assert.Near(1, transformed.Y);
            Assert.Near(0, transformed.Z);

            DffClump hierarchy = new DffClump
            {
                Frames = new[] {
                    new DffFrame { LocalTransform = parent, ParentIndex = -1 },
                    new DffFrame { LocalTransform = child, ParentIndex = 0 }
                },
                Atomics = new[] {
                    new DffAtomic { FrameIndex = 1, GeometryIndex = 0, Geometry = source.Geometries[0] }
                }
            };
            Matrix[] resolved = DffFrameTransforms.Resolve(hierarchy);
            Assert.Near(10, resolved[0].Position.X);
            Assert.Near(1, resolved[0].Position.Y);
            RenderSceneSnapshot snapshot = DffRenderSceneBuilder.Build(converted, resolved, Matrix.Identity);
            Assert.Equal(1, snapshot.DrawCalls.Count);
            BoundingBox bounds = DffRenderSceneBuilder.ComputeBounds(converted, resolved);
            Assert.True(bounds.Contains(resolved[0].TransformPoint(converted[0].Vertices[0].Position)));
            Assert.True(bounds.Min != bounds.Max);
        }

        [Test]
        public void EveryClumpOfAMultiClumpModelIsConvertedAndAligned()
        {
            DffClump source = DffReader.ReadClump(DffTestData.Create(0x34003u));
            // GTA III ped models are two clumps (body and head). A single-clump loader would
            // silently drop the head, so both conversion and transforms must walk every clump.
            DffClump body = new DffClump
            {
                Frames = new[] { new DffFrame { LocalTransform = Matrix.Identity, ParentIndex = -1 } },
                Atomics = new[] {
                    new DffAtomic { FrameIndex = 0, GeometryIndex = 0, Geometry = source.Geometries[0] }
                }
            };
            DffClump head = new DffClump
            {
                Frames = new[] {
                    new DffFrame { LocalTransform = Matrix.CreateTranslation(0, 0, 50), ParentIndex = -1 }
                },
                Atomics = new[] {
                    new DffAtomic { FrameIndex = 0, GeometryIndex = 0, Geometry = source.Geometries[0] }
                }
            };
            DffDocument document = new DffDocument { Clumps = new[] { body, head } };

            Matrix[] transforms = DffFrameTransforms.ResolveAll(document);
            Assert.Equal(2, transforms.Length);

            RenderResourceManager resources = new RenderResourceManager();
            GeometryRenderData[] atomics = resources.GetModel("cop", document, null);
            Assert.Equal(2, atomics.Length);
            Assert.Equal(transforms.Length, atomics.Length);
            // The head clump keeps its own transform instead of inheriting the body's frames.
            Assert.Near(50, transforms[1].Position.Z);
            Assert.Near(0, transforms[0].Position.Z);
            RenderSceneSnapshot snapshot = DffRenderSceneBuilder.Build(atomics, transforms, Matrix.Identity);
            Assert.True(snapshot.DrawCalls.Count >= 2);
            Assert.Throws<AssetFormatException>(delegate
            {
                resources.GetClump("cop-single", document, null);
            });
        }

        [Test]
        public void InvalidFrameCyclesAndAtomicIndicesAreRejected()
        {
            DffClump cycle = new DffClump
            {
                Frames = new[] { new DffFrame { LocalTransform = Matrix.Identity, ParentIndex = 0 } },
                Atomics = new DffAtomic[0]
            };
            Assert.Throws<AssetFormatException>(delegate { DffFrameTransforms.Resolve(cycle); });

            DffClump invalid = new DffClump
            {
                Frames = new[] { new DffFrame { LocalTransform = Matrix.Identity, ParentIndex = -1 } },
                Atomics = new[] { new DffAtomic { FrameIndex = 2 } }
            };
            Assert.Throws<AssetFormatException>(delegate { DffFrameTransforms.Resolve(invalid); });
        }
    }
}
