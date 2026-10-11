using System.Collections.Generic;
using System.Numerics;

namespace SwMultiExport.Model
{
    public sealed class SceneData
    {
        public string Name { get; set; }
        public List<SceneNode> Nodes { get; } = new List<SceneNode>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class SceneNode
    {
        public string Name { get; set; }
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
        public List<MeshData> Meshes { get; } = new List<MeshData>();
    }

    public sealed class MeshData
    {
        public string Name { get; set; }
        public Vector4 Color { get; set; } = new Vector4(0.7f, 0.7f, 0.7f, 1);
        public string TexturePath { get; set; }
        public List<Vector3> Positions { get; } = new List<Vector3>();
        public List<Vector3> Normals { get; } = new List<Vector3>();
        public List<Vector2> TexCoords { get; } = new List<Vector2>();
        public List<int> Indices { get; } = new List<int>();
    }

    public enum SimplificationMode { None, Automatic, Adjustable }

    public sealed class ExportOptions
    {
        public string OutputDirectory { get; set; }
        public string FileName { get; set; }
        public float Scale { get; set; } = 1;
        public SimplificationMode Simplification { get; set; } = SimplificationMode.Automatic;
        public int KeepTrianglePercent { get; set; } = 50;
        public List<string> Formats { get; } = new List<string>();
    }
}
