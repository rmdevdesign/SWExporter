using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace SwMultiExport.Model
{
    public static class SceneOptimizer
    {
        public static void Optimize(SceneData scene)
        {
            var shared = new Dictionary<string, MeshData>();
            var optimized = new HashSet<MeshData>();
            foreach (var node in scene.Nodes)
            for (int i = 0; i < node.Meshes.Count; i++)
            {
                var mesh = node.Meshes[i];
                if (optimized.Add(mesh)) Weld(mesh);
                string key = Signature(mesh);
                if (shared.TryGetValue(key, out var existing)) node.Meshes[i] = existing;
                else shared.Add(key, mesh);
            }
        }

        private static void Weld(MeshData mesh)
        {
            var lookup = new Dictionary<Tuple<Vector3, Vector3, Vector2>, int>();
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            bool textured = mesh.TexCoords.Count == mesh.Positions.Count;
            for (int i = 0; i < mesh.Indices.Count; i++)
            {
                int oldIndex = mesh.Indices[i];
                var uv = textured ? mesh.TexCoords[oldIndex] : Vector2.Zero;
                var key = Tuple.Create(mesh.Positions[oldIndex], mesh.Normals[oldIndex], uv);
                if (!lookup.TryGetValue(key, out int index))
                {
                    index = positions.Count;
                    lookup.Add(key, index);
                    positions.Add(key.Item1); normals.Add(key.Item2);
                    if (textured) uvs.Add(uv);
                }
                mesh.Indices[i] = index;
            }
            mesh.Positions.Clear(); mesh.Positions.AddRange(positions);
            mesh.Normals.Clear(); mesh.Normals.AddRange(normals);
            mesh.TexCoords.Clear(); mesh.TexCoords.AddRange(uvs);
        }

        private static string Signature(MeshData mesh)
        {
            using (var hash = SHA256.Create())
            using (var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(mesh.Color.X); writer.Write(mesh.Color.Y); writer.Write(mesh.Color.Z); writer.Write(mesh.Color.W);
                writer.Write(mesh.TexturePath ?? "");
                writer.Write(mesh.Positions.Count); writer.Write(mesh.TexCoords.Count); writer.Write(mesh.Indices.Count);
                foreach (var p in mesh.Positions) { writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); }
                foreach (var n in mesh.Normals) { writer.Write(n.X); writer.Write(n.Y); writer.Write(n.Z); }
                foreach (var uv in mesh.TexCoords) { writer.Write(uv.X); writer.Write(uv.Y); }
                foreach (int i in mesh.Indices) writer.Write(i);
                writer.Flush(); stream.FlushFinalBlock();
                return Convert.ToBase64String(hash.Hash);
            }
        }
    }
}
