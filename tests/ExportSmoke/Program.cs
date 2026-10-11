using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Assimp;
using Assimp.Unmanaged;
using SwMultiExport.Exporters;
using SwMultiExport.Extraction;
using SwMultiExport.Model;
using Matrix4x4 = System.Numerics.Matrix4x4;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            // Meme resolution que le plugin COM, y compris dans un hote sans redirects.
            var resolver = typeof(SceneData).Assembly.GetType("SwMultiExport.Util.DependencyResolver", true);
            resolver.GetMethod("Register").Invoke(null, null);
            resolver.GetMethod("BeginExport").Invoke(null, null);
            try { return Run(args); }
            finally { resolver.GetMethod("EndExport").Invoke(null, null); }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--analyze")
        {
            AssimpLibrary.Instance.LoadLibrary(Path.Combine(Path.GetDirectoryName(typeof(AssimpExporter).Assembly.Location), "runtimes", "win-x64", "native", "assimp.dll"));
            using (var context = new AssimpContext())
            {
                var imported = context.ImportFile(Path.GetFullPath(args[1]));
                var scene = new SceneData();
                foreach (var input in imported.Meshes)
                {
                    var mesh = new MeshData { Name = input.Name };
                    mesh.Positions.AddRange(input.Vertices.Select(p => new Vector3(p.X, p.Y, p.Z)));
                    mesh.Normals.AddRange(input.Normals.Select(p => new Vector3(p.X, p.Y, p.Z)));
                    if (input.HasTextureCoords(0)) mesh.TexCoords.AddRange(input.TextureCoordinateChannels[0].Select(p => new Vector2(p.X, p.Y)));
                    foreach (var face in input.Faces) { if (face.IndexCount != 3) throw new Exception("Non triangular input"); mesh.Indices.AddRange(face.Indices); }
                    var node = new SceneNode(); node.Meshes.Add(mesh); scene.Nodes.Add(node);
                }
                var report = MeshSimplification.Apply(scene, new ExportOptions { Simplification = SimplificationMode.Automatic });
                Console.WriteLine("ANALYSIS unique meshes: " + report.Before + " -> " + report.After + " triangles (" + report.Reduction.ToString("F2") + " %)");
            }
            return 0;
        }
        try
        {
            string output = Path.GetFullPath(args.Length == 0 ? "smoke-output" : args[0]);
            Directory.CreateDirectory(output);
            VerifyWelding();
            VerifySimplification();
            var scene = new SceneData { Name = "Tetrahedron" };
            var data = new MeshData { Name = "Red", Color = new Vector4(0.8f, 0.1f, 0.2f, 0.8f) };
            data.TexturePath = Path.Combine(output, "checker.png");
            using (var bitmap = new System.Drawing.Bitmap(2, 2))
            {
                bitmap.SetPixel(0, 0, System.Drawing.Color.Red); bitmap.SetPixel(1, 0, System.Drawing.Color.Green);
                bitmap.SetPixel(0, 1, System.Drawing.Color.Blue); bitmap.SetPixel(1, 1, System.Drawing.Color.White);
                bitmap.Save(data.TexturePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            var points = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            var faces = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            for (int i = 0; i < faces.Length; i += 3)
            {
                var a = points[faces[i]]; var b = points[faces[i + 1]]; var c = points[faces[i + 2]];
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                int start = data.Positions.Count;
                data.Positions.AddRange(new[] { a, b, c });
                data.Normals.AddRange(new[] { n, n, n });
                data.TexCoords.AddRange(new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY });
                data.Indices.AddRange(new[] { start, start + 1, start + 2 });
            }
            // SW row-vector rotation: 90 degrees about Z, followed by a translation.
            double[] swTransform = { 0, 1, 0, -1, 0, 0, 0, 0, 1, 3, 4, 5, 1 };
            var transform = SwSceneExtractor.FromSolidWorksTransform(swTransform);
            Near(Vector3.Transform(Vector3.UnitX, transform), new Vector3(3, 5, 5), "SW transform");
            var node = new SceneNode { Name = "TranslatedPart", Transform = transform };
            node.Meshes.Add(data); scene.Nodes.Add(node);
            var mirrored = new SceneNode { Name = "MirroredPart", Transform = Matrix4x4.CreateScale(-1, 1, 1) * Matrix4x4.CreateTranslation(-2, 0, 0) };
            mirrored.Meshes.Add(data); scene.Nodes.Add(mirrored);
            var repeated = new SceneNode { Name = "RepeatedPart", Transform = Matrix4x4.CreateTranslation(6, 0, 0) };
            var copy = new MeshData { Name = "Red copy", Color = data.Color, TexturePath = data.TexturePath };
            copy.Positions.AddRange(data.Positions); copy.Normals.AddRange(data.Normals);
            copy.Indices.AddRange(data.Indices); copy.TexCoords.AddRange(data.TexCoords);
            repeated.Meshes.Add(copy); scene.Nodes.Add(repeated);
            SceneOptimizer.Optimize(scene);
            if (!ReferenceEquals(node.Meshes[0], repeated.Meshes[0])) throw new Exception("Identical geometry was not shared");
            var options = new ExportOptions { Scale = 2 };
            var expected = scene.Nodes.SelectMany(n => points.Select(p => Vector3.Transform(p, n.Transform * Matrix4x4.CreateScale(options.Scale)))).ToArray();
            var min = expected.Aggregate(Vector3.Min); var max = expected.Aggregate(Vector3.Max);
            IExporter[] exporters = { new GltfExporter(true), new GltfExporter(false), new AssimpExporter() };
            foreach (var exporter in exporters)
            {
                string path = Path.Combine(output, "tetrahedron." + exporter.Extension);
                exporter.Export(scene, path, options);
                if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new Exception("Empty export " + path);
                if (exporter.Extension != "fbx")
                {
                    var gltf = SharpGLTF.Schema2.ModelRoot.Load(path);
                    if (gltf.LogicalMeshes.Count != 2 || gltf.LogicalMaterials.Count == 0 || gltf.LogicalImages.Count != 1) throw new Exception("Missing glTF meshes/materials/textures or instancing");
                }
                string native = Path.Combine(Path.GetDirectoryName(typeof(AssimpExporter).Assembly.Location), "runtimes", "win-x64", "native", "assimp.dll");
                AssimpLibrary.Instance.LoadLibrary(native);
                using (var context = new AssimpContext())
                {
                    var raw = context.ImportFile(path);
                    if (raw.Meshes.Count != 2) throw new Exception("Mesh instances were duplicated: " + exporter.Extension);
                    foreach (var mesh in raw.Meshes)
                        if (!mesh.HasTextureCoords(0)) throw new Exception("Missing UV: " + exporter.Extension);
                    if (exporter.Extension == "fbx")
                    {
                        if (!raw.Metadata.TryGetValue("UnitScaleFactor", out var units) || Math.Abs(Convert.ToDouble(units.Data) - 1) > 1e-9)
                            throw new Exception("FBX centimetre unit declaration is missing or invalid");
                        if (!raw.Materials.Any(m => m.HasTextureDiffuse)) throw new Exception("Missing FBX texture link");
                        foreach (var material in raw.Materials.Where(m => m.HasTextureDiffuse))
                        {
                            string imagePath = material.TextureDiffuse.FilePath;
                            if (!Path.IsPathRooted(imagePath)) imagePath = Path.Combine(output, imagePath);
                            if (!File.Exists(imagePath)) throw new Exception("Missing FBX texture image: " + imagePath);
                        }
                    }
                    var imported = context.ImportFile(path, PostProcessSteps.PreTransformVertices | PostProcessSteps.Triangulate);
                    int triangles = imported.Meshes.Sum(m => m.Faces.Count);
                    if (triangles != 12) throw new Exception("Wrong triangle count: " + triangles);
                    var vertices = imported.Meshes.SelectMany(m => m.Vertices).Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
                    // FBX positions are in centimetres; GLB/glTF positions are in metres.
                    float unit = exporter.Extension == "fbx" ? 100 : 1;
                    Near(vertices.Aggregate(Vector3.Min), min * unit, exporter.Extension + " min");
                    Near(vertices.Aggregate(Vector3.Max), max * unit, exporter.Extension + " max");
                    foreach (var mesh in imported.Meshes)
                    foreach (var face in mesh.Faces)
                    {
                        var a = mesh.Vertices[face.Indices[0]]; var b = mesh.Vertices[face.Indices[1]]; var c = mesh.Vertices[face.Indices[2]];
                        var cross = Vector3.Cross(new Vector3(b.X-a.X,b.Y-a.Y,b.Z-a.Z), new Vector3(c.X-a.X,c.Y-a.Y,c.Z-a.Z));
                        var normal = mesh.Normals[face.Indices[0]];
                        if (Vector3.Dot(cross, new Vector3(normal.X,normal.Y,normal.Z)) <= 0) throw new Exception("Winding/normal mismatch: " + exporter.Extension);
                    }
                    Console.WriteLine("PASS " + exporter.Extension + ": " + triangles + " triangles, units/transforms/normals/textures/instances verified, " + new FileInfo(path).Length + " bytes");
                }
            }
            Console.WriteLine("PASS all exports: " + output);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Near(Vector3 actual, Vector3 expected, string label)
    {
        if (Vector3.Distance(actual, expected) > 0.001f) throw new Exception(label + ": " + actual + " expected " + expected);
    }

    private static void VerifyWelding()
    {
        var scene = new SceneData(); var node = new SceneNode(); var mesh = new MeshData();
        scene.Nodes.Add(node); node.Meshes.Add(mesh);
        mesh.Positions.AddRange(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitX, Vector3.One, Vector3.UnitY });
        mesh.Normals.AddRange(Enumerable.Repeat(Vector3.UnitZ, 6));
        mesh.Indices.AddRange(new[] { 0, 1, 2, 3, 4, 5 });
        SceneOptimizer.Optimize(scene);
        if (mesh.Positions.Count != 4 || mesh.Indices.Count != 6) throw new Exception("Vertex welding failed");
        // Une couture UV doit rester distincte, meme avec position/normale identiques.
        var seam = new MeshData(); node.Meshes.Add(seam);
        seam.Positions.AddRange(new[] { Vector3.Zero, Vector3.Zero });
        seam.Normals.AddRange(new[] { Vector3.UnitZ, Vector3.UnitZ });
        seam.TexCoords.AddRange(new[] { Vector2.Zero, Vector2.One }); seam.Indices.AddRange(new[] { 0, 1 });
        SceneOptimizer.Optimize(scene);
        if (seam.Positions.Count != 2) throw new Exception("UV seam was lost");
        Console.WriteLine("PASS welding: 6 vertices -> 4, UV seams preserved");
    }

    private static void VerifySimplification()
    {
        var plane = Grid(20, false); var scene = SceneOf(plane);
        var report = MeshSimplification.Apply(scene, new ExportOptions { Simplification = SimplificationMode.Automatic });
        if (report.Before != 800 || report.After != 2) throw new Exception("Plane not maximally simplified: " + report.Before + " -> " + report.After);
        if (Math.Abs(SurfaceArea(plane) - 400) > 1e-5) throw new Exception("Automatic mode changed plane area");
        if (plane.TexturePath == null || plane.TexCoords.Count != 4) throw new Exception("Automatic mode lost texture/UV");
        foreach (var uv in plane.TexCoords) if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) throw new Exception("Invalid plane UV");
        Console.WriteLine("PASS automatic plane: 800 -> 2 triangles, same contour/area/UV");

        // Float tessellation in metres, rotated plane, duplicated vertices and
        // slightly different normals, as supplied by CAD tessellation.
        var cad = Grid(20, false); cad.TexturePath = null; cad.TexCoords.Clear();
        var rotation = Matrix4x4.CreateFromYawPitchRoll(0.71f, 0.43f, 0.29f);
        var cadSoup = new MeshData();
        foreach (int index in cad.Indices)
        {
            cadSoup.Indices.Add(cadSoup.Positions.Count);
            cadSoup.Positions.Add(Vector3.Transform(cad.Positions[index] * 0.001f, rotation) + new Vector3(0.12f, -0.08f, 0.3f));
            cadSoup.Normals.Add(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, rotation) + new Vector3((cadSoup.Positions.Count % 3) * 1e-6f, 0, 0)));
        }
        var cadReport = MeshSimplification.Apply(SceneOf(cadSoup), new ExportOptions { Simplification = SimplificationMode.Automatic });
        if (cadReport.After != 2) throw new Exception("CAD plane not simplified: " + cadReport.Before + " -> " + cadReport.After);
        Console.WriteLine("PASS automatic CAD plane: 800 -> 2, rotated float positions and split normals");

        var ring = Grid(12, true); double ringArea = SurfaceArea(ring);
        MeshSimplification.Apply(SceneOf(ring), new ExportOptions { Simplification = SimplificationMode.Automatic });
        if (Math.Abs(SurfaceArea(ring) - ringArea) > 1e-4) throw new Exception("Automatic mode changed holed plane area");
        for (int i = 0; i < ring.Indices.Count; i += 3)
        {
            var a = ring.Positions[ring.Indices[i]]; var b = ring.Positions[ring.Indices[i + 1]]; var c = ring.Positions[ring.Indices[i + 2]];
            var p = new Vector3(6, 6, 0);
            if (Vector3.Cross(b-a,p-a).Z > 1e-6 && Vector3.Cross(c-b,p-b).Z > 1e-6 && Vector3.Cross(a-c,p-c).Z > 1e-6)
                throw new Exception("Automatic mode filled a hole");
        }
        Console.WriteLine("PASS automatic plane with hole: area and hole preserved");

        var curved = Grid(20, false);
        for (int i = 0; i < curved.Positions.Count; i++)
        {
            var p = curved.Positions[i]; float x = p.X / 20, y = p.Y / 20;
            p.Z = 2 * (float)Math.Sin(x * Math.PI) * (float)Math.Sin(y * Math.PI); curved.Positions[i] = p;
            var n = new Vector3(-(float)(0.1 * Math.PI * Math.Cos(x * Math.PI) * Math.Sin(y * Math.PI)),
                -(float)(0.1 * Math.PI * Math.Sin(x * Math.PI) * Math.Cos(y * Math.PI)), 1);
            curved.Normals[i] = Vector3.Normalize(n);
        }
        var positions = curved.Positions.ToArray();
        var unchanged = MeshSimplification.Apply(SceneOf(curved), new ExportOptions { Simplification = SimplificationMode.Automatic });
        if (unchanged.After != unchanged.Before || !positions.OrderBy(p => p.X).ThenBy(p => p.Y).SequenceEqual(curved.Positions.OrderBy(p => p.X).ThenBy(p => p.Y)))
            throw new Exception("Automatic mode changed curved geometry");
        var manual = MeshSimplification.Apply(SceneOf(curved), new ExportOptions { Simplification = SimplificationMode.Adjustable, KeepTrianglePercent = 50 });
        if (manual.After >= manual.Before || manual.After < 1 || manual.Target != 400) throw new Exception("Adjustable mode did not simplify");
        if (curved.Normals.Count != curved.Positions.Count || curved.TexCoords.Count != curved.Positions.Count) throw new Exception("Manual attributes missing");
        Console.WriteLine("PASS adjustable curve: " + manual.Before + " -> " + manual.After + " triangles, target=" + manual.Target);
        var off = MeshSimplification.Apply(SceneOf(curved), new ExportOptions { Simplification = SimplificationMode.None });
        if (off.After != off.Before) throw new Exception("Disabled simplification changed triangles");
    }

    private static SceneData SceneOf(MeshData mesh)
    {
        var scene = new SceneData { Name = "Simplification test" }; var node = new SceneNode { Name = "Body" };
        node.Meshes.Add(mesh); scene.Nodes.Add(node); return scene;
    }

    private static MeshData Grid(int size, bool hole)
    {
        var mesh = new MeshData { Name = "Plane", TexturePath = "synthetic-texture.png" };
        for (int y = 0; y <= size; y++) for (int x = 0; x <= size; x++)
        {
            mesh.Positions.Add(new Vector3(x, y, 0)); mesh.Normals.Add(Vector3.UnitZ);
            mesh.TexCoords.Add(new Vector2(x / (float)size, y / (float)size));
        }
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            if (hole && x >= 4 && x < 8 && y >= 4 && y < 8) continue;
            int a = y * (size + 1) + x, b = a + 1, c = a + size + 1, d = c + 1;
            mesh.Indices.AddRange(new[] { a, b, d, a, d, c });
        }
        return mesh;
    }

    private static double SurfaceArea(MeshData mesh)
    {
        double area = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
            area += Vector3.Cross(mesh.Positions[mesh.Indices[i+1]] - mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i+2]] - mesh.Positions[mesh.Indices[i]]).Length() * 0.5;
        return area;
    }
}
