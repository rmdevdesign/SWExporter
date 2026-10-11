using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SwMultiExport.Model;

namespace SwMultiExport.Exporters
{
    public sealed class GltfExporter : IExporter
    {
        public string Extension { get; }
        public GltfExporter(bool binary) { Extension = binary ? "glb" : "gltf"; }

        public void Export(SceneData scene, string path, ExportOptions options)
        {
            var builder = new SceneBuilder(scene.Name);
            var meshIds = new Dictionary<MeshData, int>();
            var meshes = new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>>();
            var materials = new Dictionary<MeshData, MaterialBuilder>();
            var images = new Dictionary<string, ImageBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in scene.Nodes)
            {
                var transform = node.Transform * Matrix4x4.CreateScale(options.Scale);
                bool mirrored = transform.GetDeterminant() < 0;
                if (mirrored) transform = Matrix4x4.CreateScale(-1, 1, 1) * transform;
                if (Math.Abs(transform.GetDeterminant()) < 1e-30f) throw new InvalidOperationException("Transformation non inversible : " + node.Name);
                foreach (var data in node.Meshes) if (!meshIds.ContainsKey(data)) meshIds.Add(data, meshIds.Count);
                string key = (mirrored ? "mirror:" : "normal:") + string.Join(",", node.Meshes.Select(m => meshIds[m]));
                if (!meshes.TryGetValue(key, out var mesh))
                {
                    mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(node.Name);
                    foreach (var data in node.Meshes)
                    {
                        if (!materials.TryGetValue(data, out var material))
                        {
                            material = new MaterialBuilder(data.Name).WithMetallicRoughnessShader()
                                .WithBaseColor(data.Color).WithMetallicRoughness(0, 0.7f);
                            if (data.Color.W < 1) material.WithAlpha(AlphaMode.BLEND);
                            if (data.TexturePath != null)
                            {
                                if (data.TexCoords.Count != data.Positions.Count) throw new InvalidOperationException("Texture sans UV : " + node.Name);
                                if (!images.TryGetValue(data.TexturePath, out var image))
                                {
                                    byte[] bytes = TextureAssets.ReadPng(data.TexturePath);
                                    image = ImageBuilder.From(new SharpGLTF.Memory.MemoryImage(bytes), TextureAssets.Name(bytes));
                                    images.Add(data.TexturePath, image);
                                }
                                material.WithChannelImage(KnownChannel.BaseColor, image);
                            }
                            materials.Add(data, material);
                        }
                        var primitive = mesh.UsePrimitive(material);
                        for (int i = 0; i < data.Indices.Count; i += 3)
                            primitive.AddTriangle(Vertex(data, data.Indices[i], mirrored),
                                Vertex(data, data.Indices[i + (mirrored ? 2 : 1)], mirrored),
                                Vertex(data, data.Indices[i + (mirrored ? 1 : 2)], mirrored));
                    }
                    meshes.Add(key, mesh);
                }
                var pivot = new NodeBuilder(node.Name) { LocalMatrix = transform };
                builder.AddRigidMesh(mesh, pivot);
            }
            var model = builder.ToGltf2();
            if (Extension == "glb") model.SaveGLB(path);
            else model.SaveGLTF(path);
        }

        private static VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Vertex(MeshData data, int index, bool mirrored)
        {
            var p = data.Positions[index]; var n = data.Normals[index];
            if (mirrored) { p.X = -p.X; n.X = -n.X; }
            var uv = data.TexCoords.Count == data.Positions.Count ? data.TexCoords[index] : Vector2.Zero;
            // glTF utilise une origine UV en haut de l'image.
            uv.Y = 1 - uv.Y;
            return new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(new VertexPositionNormal(p, n), new VertexTexture1(uv));
        }
    }
}
