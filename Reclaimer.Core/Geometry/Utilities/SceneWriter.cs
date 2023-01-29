﻿using Reclaimer.IO;

namespace Reclaimer.Geometry.Utilities
{
    internal abstract class SceneWriter<T>
    {
        protected readonly EndianWriter Writer;

        protected SceneWriter(EndianWriter writer)
        {
            Writer = writer;
        }

        public abstract void Write(T obj);

        protected BlockMarker BlockMarker(BlockCode code) => new BlockMarker(Writer, code);
        protected BlockMarker BlockMarker(int identifier) => new BlockMarker(Writer, new BlockCode("NULL", "Unknown"));

        protected void WriteList<TItem>(IList<TItem> list, Action<TItem> writeFunc, BlockCode code)
        {
            using (new ListBlockMarker(Writer, code, list.Count))
            {
                foreach (var item in list)
                    writeFunc(item);
            }
        }
    }

    internal class SceneWriter : SceneWriter<Scene>
    {
        private static readonly Version version = new Version();

        //TODO: treat ID 0 as always unique? ie dont force creator to provide unique ids

        private readonly LazyList<int, Material> materialPool = new(m => m.Id);
        private readonly LazyList<int, Texture> texturePool = new(t => t.Id);
        private readonly LazyList<Model> modelPool = new();

        public SceneWriter(EndianWriter writer)
            : base(writer)
        { }

        public override void Write(Scene scene)
        {
            materialPool.Clear();
            texturePool.Clear();
            modelPool.Clear();

            using (BlockMarker(SceneCodes.FileHeader))
            {
                Writer.Write((byte)version.Major);
                Writer.Write((byte)version.Minor);
                Writer.Write((byte)Math.Max(0, version.Build));
                Writer.Write((byte)Math.Max(0, version.Revision));
                Writer.Write(scene.CoordinateSystem.UnitScale);
                Writer.WriteMatrix3x3(scene.CoordinateSystem.WorldMatrix);
                Writer.Write(scene.Name);

                //everything from here on must be a block

                Write(scene.RootNode);

                //TODO (global markers such as bsp markers)
                using (new ListBlockMarker(Writer, SceneCodes.Marker, 0))
                { }

                var modelWriter = new ModelWriter(Writer, materialPool);
                WriteList(modelPool, modelWriter.Write, SceneCodes.Model);
                WriteList(materialPool, Write, SceneCodes.Material);
                WriteList(texturePool, Write, SceneCodes.Texture);
            }
        }

        private void Write(SceneGroup sceneGroup)
        {
            using (BlockMarker(SceneCodes.SceneGroup))
            {
                Writer.Write(sceneGroup.Name);
                Writer.Write(sceneGroup.ChildGroups.Count + sceneGroup.ChildObjects.Count);

                foreach (var child in sceneGroup.ChildGroups)
                    Write(child);

                foreach (var child in sceneGroup.ChildObjects)
                    Write(child);
            }
        }

        private void Write(SceneObject sceneObject)
        {
            if (sceneObject is Model model)
            {
                using (BlockMarker(SceneCodes.ModelReference))
                    Writer.Write(modelPool.IndexOf(model));
            }
            else
                throw new NotImplementedException();
        }

        private void Write(Material material)
        {
            using (BlockMarker(SceneCodes.Material))
            {
                Writer.Write(material.Name);
                WriteList(material.TextureMappings, Write, SceneCodes.TextureMapping);
                WriteList(material.Tints, Write, SceneCodes.Tint);
            }
        }

        private void Write(TextureMapping mapping)
        {
            using (BlockMarker(mapping.Usage))
            {
                Writer.Write(texturePool.IndexOf(mapping.Texture));
                Writer.Write(mapping.Tiling);
                Writer.Write((int)mapping.ChannelMask);
            }
        }

        private void Write(MaterialTint tint)
        {
            using (BlockMarker(tint.Usage))
            {
                Writer.Write(tint.Color.R);
                Writer.Write(tint.Color.G);
                Writer.Write(tint.Color.B);
                Writer.Write(tint.Color.A);
            }
        }

        private void Write(Texture texture)
        {
            using (BlockMarker(SceneCodes.Texture))
            {
                Writer.Write(texture.Name);
                Writer.Write(0); //binary size if embedded
            }
        }
    }

    internal class ModelWriter : SceneWriter<Model>
    {
        private readonly LazyList<VertexBuffer> vertexBufferPool = new();
        private readonly LazyList<IIndexBuffer> indexBufferPool = new();
        private readonly LazyList<Mesh> meshPool = new();

        private readonly LazyList<int, Material> materialPool;

        private Model model;

        public ModelWriter(EndianWriter writer, LazyList<int, Material> materialPool)
            : base(writer)
        {
            this.materialPool = materialPool;
        }

        public override void Write(Model model)
        {
            vertexBufferPool.Clear();
            indexBufferPool.Clear();
            this.model = model;

            using (BlockMarker(SceneCodes.Model))
            {
                Writer.Write(model.Name);
                WriteList(model.Regions, Write, SceneCodes.Region);
                WriteList(model.Markers, Write, SceneCodes.Marker);
                WriteList(model.Bones, Write, SceneCodes.Bone);
                WriteList(meshPool, Write, SceneCodes.Mesh);
                WriteList(vertexBufferPool, Write, SceneCodes.VertexBuffer);
                WriteList(indexBufferPool, Write, SceneCodes.IndexBuffer);
            }
        }

        private void Write(ModelRegion region)
        {
            using (BlockMarker(SceneCodes.Region))
            {
                Writer.Write(region.Name);
                WriteList(region.Permutations, Write, SceneCodes.Permutation);
            }
        }

        private void Write(ModelPermutation permutation)
        {
            var meshes = permutation.MeshIndices.Select(i => model.Meshes.ElementAtOrDefault(i));

            var meshRange = permutation.MeshRange;
            if (!meshes.Any() || meshes.Any(m => m == null))
                meshRange = (0, 0); //normalize to 0,0 in case of negatives or bad indices
            else
            {
                meshPool.AddRange(meshes);
                meshRange.Index = meshPool.IndexOf(meshes.First());
            }

            using (BlockMarker(SceneCodes.Permutation))
            {
                Writer.Write(permutation.Name);
                Writer.Write(permutation.IsInstanced);
                Writer.Write(meshRange.Index);
                Writer.Write(meshRange.Count);
                Writer.WriteMatrix3x4(permutation.GetFinalTransform());
            }
        }

        private void Write(Marker marker)
        {
            using (BlockMarker(SceneCodes.Marker))
            {
                Writer.Write(marker.Name);
                WriteList(marker.Instances, Write, SceneCodes.MarkerInstance);
            }
        }

        private void Write(MarkerInstance instance)
        {
            using (BlockMarker(SceneCodes.MarkerInstance))
            {
                Writer.Write(instance.RegionIndex);
                Writer.Write(instance.PermutationIndex);
                Writer.Write(instance.BoneIndex);
                Writer.Write(instance.Position);
                Writer.Write(instance.Rotation);
            }
        }

        private void Write(Bone bone)
        {
            using (BlockMarker(SceneCodes.Bone))
            {
                Writer.Write(bone.Name);
                Writer.Write(bone.ParentIndex);
                Writer.WriteMatrix4x4(bone.Transform);
            }
        }

        private void Write(Mesh mesh)
        {
            using (BlockMarker(SceneCodes.Mesh))
            {
                Writer.Write(vertexBufferPool.IndexOf(mesh.VertexBuffer));
                Writer.Write(indexBufferPool.IndexOf(mesh.IndexBuffer));
                Writer.Write(mesh.BoneIndex ?? -1);
                Writer.WriteMatrix3x4(mesh.PositionBounds.CreateExpansionMatrix());
                Writer.WriteMatrix3x4(mesh.TextureBounds.CreateExpansionMatrix());

                WriteList(mesh.Segments, Write, SceneCodes.MeshSegment);
            }
        }

        private void Write(MeshSegment segment)
        {
            using (BlockMarker(SceneCodes.MeshSegment))
            {
                Writer.Write(segment.IndexStart);
                Writer.Write(segment.IndexLength);
                Writer.Write(materialPool.IndexOf(segment.Material));
            }
        }

        private void Write(VertexBuffer vertexBuffer)
        {
            using (BlockMarker(SceneCodes.VertexBuffer))
            {
                Writer.Write(vertexBuffer.Count);

                WriteChannels(vertexBuffer.PositionChannels, VertexChannelCodes.Position);
                WriteChannels(vertexBuffer.TextureCoordinateChannels, VertexChannelCodes.TextureCoordinate);
                WriteChannels(vertexBuffer.NormalChannels, VertexChannelCodes.Normal);
                WriteChannels(vertexBuffer.BlendIndexChannels, VertexChannelCodes.BlendIndex);
                WriteChannels(vertexBuffer.BlendWeightChannels, VertexChannelCodes.BlendWeight);
            }

            void WriteChannels(IList<IReadOnlyList<IVector>> vertexChannel, BlockCode code)
            {
                foreach (var vectorBuffer in vertexChannel)
                {
                    using (BlockMarker(code))
                        WriteBuffer(vectorBuffer);
                }
            }

            void WriteBuffer(IReadOnlyList<IVector> vectorBuffer)
            {
                var vb = vectorBuffer as IVectorBuffer;
                var typeCode = VectorTypeCodes.FromType(vb?.VectorType);
                if (typeCode == null)
                {
                    //no choice but to assume float, use known vector width if possible
                    var width = vb?.Dimensions ?? 4;
                    Writer.Write(VectorTypeCodes.FromDimensions(width).Value);
                    foreach (var vec in vectorBuffer)
                    {
                        Writer.Write(vec.X);
                        Writer.Write(vec.Y);
                        if (width > 2)
                            Writer.Write(vec.Z);
                        if (width > 3)
                            Writer.Write(vec.W);
                    }
                }
                else
                {
                    Writer.Write(typeCode.Value);
                    for (var i = 0; i < vb.Count; i++)
                        Writer.Write(vb.GetBytes(i));
                }
            }
        }

        private void Write(IIndexBuffer indexBuffer)
        {
            using (BlockMarker(SceneCodes.IndexBuffer))
            {
                var max = indexBuffer.DefaultIfEmpty().Max();
                var width = max <= byte.MaxValue ? sizeof(byte) : max <= ushort.MaxValue ? sizeof(ushort) : sizeof(int);
                Action<int> writeFunc = width switch
                {
                    sizeof(byte) => i => Writer.Write((byte)i),
                    sizeof(ushort) => i => Writer.Write((ushort)i),
                    _ => Writer.Write
                };

                Writer.Write((byte)indexBuffer.Layout);
                Writer.Write((byte)width);
                Writer.Write(indexBuffer.Count);

                foreach (var index in indexBuffer)
                    writeFunc(index);
            }
        }
    }
}
