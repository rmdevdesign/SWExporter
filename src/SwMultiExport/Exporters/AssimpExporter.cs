using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assimp;
using Assimp.Unmanaged;
using SwMultiExport.Model;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace SwMultiExport.Exporters
{
    public sealed class AssimpExporter : IExporter
    {
        public string Extension => "fbx";

        public void Export(SceneData source, string path, ExportOptions options)
        {
            string native = Path.Combine(Path.GetDirectoryName(typeof(AssimpExporter).Assembly.Location),
                "runtimes", "win-x64", "native", "assimp.dll");
            AssimpLibrary.Instance.LoadLibrary(native);
            using (var context = new AssimpContext())
            {
                if (!context.GetSupportedExportFormats().Any(f => f.FormatId == "fbx"))
                    throw new NotSupportedException("Cette version d'Assimp ne propose pas l'export FBX.");
                var scene = new Scene { RootNode = new Node(source.Name) };
                var meshes = new Dictionary<Tuple<MeshData, bool>, int>();
                var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dataNode in source.Nodes)
                {
                    var transform = dataNode.Transform * Matrix4x4.CreateScale(options.Scale);
                    bool mirrored = transform.GetDeterminant() < 0;
                    if (mirrored) transform = Matrix4x4.CreateScale(-1, 1, 1) * transform;
                    // Assimp 5 ecrit UnitScaleFactor=1 : une unite FBX = un centimetre.
                    // Convertir sommets et translations, sans modifier l'echelle utilisateur.
                    transform.M41 *= 100; transform.M42 *= 100; transform.M43 *= 100;
                    var node = new Node(dataNode.Name) { Transform = ToAssimp(transform) };
                    scene.RootNode.Children.Add(node);
                    foreach (var data in dataNode.Meshes)
                    {
                        var key = Tuple.Create(data, mirrored);
                        if (!meshes.TryGetValue(key, out int meshIndex))
                        {
                            var material = new Material
                            {
                                Name = data.Name,
                                ColorDiffuse = new Color4D(data.Color.X, data.Color.Y, data.Color.Z, 1),
                                Opacity = data.Color.W
                            };
                            if (data.TexturePath != null)
                            {
                                if (data.TexCoords.Count != data.Positions.Count) throw new InvalidOperationException("Texture sans UV : " + dataNode.Name);
                                if (!textures.TryGetValue(data.TexturePath, out var texturePath))
                                {
                                    texturePath = TextureAssets.CopyForFbx(data.TexturePath, path);
                                    textures.Add(data.TexturePath, texturePath);
                                }
                                material.TextureDiffuse = new TextureSlot(texturePath, TextureType.Diffuse, 0,
                                    TextureMapping.FromUV, 0, 1, TextureOperation.Multiply,
                                    TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0);
                            }
                            var mesh = new Mesh(data.Name, PrimitiveType.Triangle) { MaterialIndex = scene.Materials.Count };
                            scene.Materials.Add(material);
                            foreach (var p in data.Positions) mesh.Vertices.Add(new Vector3D(p.X * (mirrored ? -100 : 100), p.Y * 100, p.Z * 100));
                            foreach (var n in data.Normals) mesh.Normals.Add(new Vector3D(mirrored ? -n.X : n.X, n.Y, n.Z));
                            if (data.TexturePath != null)
                            {
                                mesh.UVComponentCount[0] = 2;
                                foreach (var uv in data.TexCoords) mesh.TextureCoordinateChannels[0].Add(new Vector3D(uv.X, uv.Y, 0));
                            }
                            for (int i = 0; i < data.Indices.Count; i += 3)
                                mesh.Faces.Add(new Face(new[] { data.Indices[i], data.Indices[i + (mirrored ? 2 : 1)], data.Indices[i + (mirrored ? 1 : 2)] }));
                            meshIndex = scene.Meshes.Count;
                            scene.Meshes.Add(mesh);
                            meshes.Add(key, meshIndex);
                        }
                        node.MeshIndices.Add(meshIndex);
                    }
                }
                if (!context.ExportFile(scene, path, "fbx")) throw new IOException("Assimp n'a pas pu ecrire le fichier FBX.");
            }
        }

        private static Assimp.Matrix4x4 ToAssimp(Matrix4x4 m) => new Assimp.Matrix4x4(
            m.M11, m.M21, m.M31, m.M41,
            m.M12, m.M22, m.M32, m.M42,
            m.M13, m.M23, m.M33, m.M43,
            m.M14, m.M24, m.M34, m.M44);
    }
}
