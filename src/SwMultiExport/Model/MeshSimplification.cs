using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MeshDecimator;
using MeshDecimator.Algorithms;
using SwMultiExport.Util;
using DVector3 = MeshDecimator.Math.Vector3d;
using FVector3 = MeshDecimator.Math.Vector3;
using FVector2 = MeshDecimator.Math.Vector2;

namespace SwMultiExport.Model
{
    public sealed class SimplificationReport
    {
        public int Before { get; set; }
        public int After { get; set; }
        public int Target { get; set; }
        public double Reduction => Before == 0 ? 0 : 100.0 * (Before - After) / Before;
    }

    public static class MeshSimplification
    {
        public static SimplificationReport Apply(SceneData scene, ExportOptions options)
        {
            if (options.KeepTrianglePercent < 1 || options.KeepTrianglePercent > 100) throw new ArgumentOutOfRangeException(nameof(options.KeepTrianglePercent));
            var report = new SimplificationReport { Before = Count(scene) };
            report.Target = options.Simplification == SimplificationMode.Adjustable ?
                (int)Math.Ceiling(report.Before * options.KeepTrianglePercent / 100.0) : report.Before;
            if (options.Simplification != SimplificationMode.None)
            {
                foreach (var mesh in scene.Nodes.SelectMany(n => n.Meshes).Distinct())
                {
                    int originalCount = mesh.Indices.Count / 3;
                    if (originalCount < 4) continue;
                    if (options.Simplification == SimplificationMode.Automatic)
                    {
                        SimplifyPlanes(mesh);
                    }
                    else
                    {
                        int target = Math.Max(1, (int)Math.Ceiling(originalCount * options.KeepTrianglePercent / 100.0));
                        if (target >= originalCount) continue;
                        // Normaliser l'echelle pour rendre les seuils QEM independants des metres.
                        var candidate = Decimate(mesh, target, false);
                        if (Valid(candidate) && candidate.Indices.Count < mesh.Indices.Count) Copy(candidate, mesh);
                    }
                }
                SceneOptimizer.Optimize(scene);
            }
            report.After = Count(scene);
            Logger.Log("Decimation " + options.Simplification + " : " + report.Before + " -> " + report.After + " triangles (" + report.Reduction.ToString("F1") + " %), cible=" + report.Target);
            return report;
        }

        private static int Count(SceneData scene) => scene.Nodes.Sum(n => n.Meshes.Sum(m => m.Indices.Count / 3));

        // Les courbes, transitions de normales et textures non affines restent intactes.
        // Les zones planes connectees peuvent etre retriangulees en preservant leur contour.
        private static void SimplifyPlanes(MeshData mesh)
        {
            int triangleCount = mesh.Indices.Count / 3;
            var normals = new Vector3[triangleCount];
            var eligible = new bool[triangleCount];
            var edges = new Dictionary<Tuple<int, int>, List<int>>();
            // La connexite geometrique ne depend pas des petites variations de
            // normales entre triangles de la tessellation SolidWorks.
            var vertices = new Dictionary<Vector3, int>();
            var topology = new int[mesh.Positions.Count];
            for (int i = 0; i < topology.Length; i++)
            {
                if (!vertices.TryGetValue(mesh.Positions[i], out int id))
                { id = vertices.Count; vertices.Add(mesh.Positions[i], id); }
                topology[i] = id;
            }
            for (int t = 0; t < triangleCount; t++)
            {
                int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
                var cross = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                if (cross.LengthSquared() > 1e-30f)
                {
                    normals[t] = Vector3.Normalize(cross);
                    eligible[t] = Vector3.DistanceSquared(normals[t], mesh.Normals[a]) < 1e-6f &&
                        Vector3.DistanceSquared(normals[t], mesh.Normals[b]) < 1e-6f &&
                        Vector3.DistanceSquared(normals[t], mesh.Normals[c]) < 1e-6f;
                }
                AddEdge(edges, topology[a], topology[b], t); AddEdge(edges, topology[b], topology[c], t); AddEdge(edges, topology[c], topology[a], t);
            }
            var visited = new bool[triangleCount];
            var output = EmptyLike(mesh);
            for (int seed = 0; seed < triangleCount; seed++)
            {
                if (visited[seed]) continue;
                var group = new List<int>();
                var pending = new Queue<int>(); pending.Enqueue(seed); visited[seed] = true;
                while (pending.Count > 0)
                {
                    int t = pending.Dequeue(); group.Add(t);
                    if (!eligible[seed] || !eligible[t]) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int a = mesh.Indices[t * 3 + j], b = mesh.Indices[t * 3 + (j + 1) % 3];
                        var adjacent = edges[Edge(topology[a], topology[b])];
                        if (adjacent.Count != 2) continue;
                        foreach (int next in adjacent)
                        {
                            if (!visited[next] && eligible[next] && Vector3.DistanceSquared(normals[seed], normals[next]) < 1e-6f)
                            { visited[next] = true; pending.Enqueue(next); }
                        }
                    }
                }
                var patch = ExtractPatch(mesh, group);
                if (group.Count >= 4 && eligible[seed] && PlanarAndAffine(patch, out var plane))
                {
                    // Unifier seulement les normales uniformes d'un plan valide.
                    // AffineUv a deja exclu les coutures et projections incompatibles.
                    if (!patch.Normals.All(n => Vector3.DistanceSquared(n, plane) < 1e-6f))
                    { Append(patch, output); continue; }
                    var welded = ExtractPatch(patch, Enumerable.Range(0, patch.Indices.Count / 3).ToList());
                    for (int i = 0; i < welded.Normals.Count; i++) welded.Normals[i] = plane;
                    var temporary = new SceneData(); var node = new SceneNode(); node.Meshes.Add(welded); temporary.Nodes.Add(node);
                    SceneOptimizer.Optimize(temporary);
                    // Un contour simple peut etre retriangule directement : n-2
                    // triangles, sans garder les sommets interieurs inutiles.
                    var candidate = Retriangulate(welded, plane) ?? Decimate(welded, 1, true);
                    Logger.Log("Plan : " + group.Count + " triangles, " + welded.Positions.Count + " sommets, candidat=" + candidate.Indices.Count / 3 + ", valide=" + Valid(candidate) + ", surface=" + SamePlanarSurface(welded, candidate, plane));
                    if (candidate.Indices.Count < patch.Indices.Count && Valid(candidate) && SamePlanarSurface(welded, candidate, plane)) patch = candidate;
                }
                Append(patch, output);
            }
            Copy(output, mesh);
            Logger.Log("Analyse plans : " + eligible.Count(e => e) + "/" + triangleCount + " triangles eligibles, resultat=" + mesh.Indices.Count / 3);
        }

        private static MeshData Decimate(MeshData source, int target, bool lossless)
        {
            var min = source.Positions.Aggregate(Vector3.Min); var max = source.Positions.Aggregate(Vector3.Max);
            double size = Vector3.Distance(min, max);
            if (size < 1e-15) return source;
            var positions = source.Positions.Select(p => new DVector3((p.X - (double)min.X) / size, (p.Y - (double)min.Y) / size, (p.Z - (double)min.Z) / size)).ToArray();
            var input = new MeshDecimator.Mesh(positions, source.Indices.ToArray())
            {
                Normals = source.Normals.Select(n => new FVector3(n.X, n.Y, n.Z)).ToArray()
            };
            if (source.TexCoords.Count == source.Positions.Count) input.UV1 = source.TexCoords.Select(uv => new FVector2(uv.X, uv.Y)).ToArray();
            var algorithm = new FastQuadricMeshSimplification
            {
                PreserveBorders = true, PreserveSeams = true, PreserveFoldovers = true,
                // Les ruptures de normales ont deja des sommets distincts. Ne pas les relier.
                EnableSmartLink = false
            };
            var result = lossless ? MeshDecimation.DecimateMeshLossless(algorithm, input) : MeshDecimation.DecimateMesh(algorithm, input, target);
            var output = EmptyLike(source);
            foreach (var p in result.Vertices) output.Positions.Add(new Vector3((float)(p.x * size + min.X), (float)(p.y * size + min.Y), (float)(p.z * size + min.Z)));
            foreach (var n in result.Normals)
            {
                var v = new Vector3(n.x, n.y, n.z);
                output.Normals.Add(v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : Vector3.UnitY);
            }
            if (result.UV1 != null) foreach (var uv in result.UV1) output.TexCoords.Add(new Vector2(uv.x, uv.y));
            output.Indices.AddRange(result.Indices);
            return output;
        }

        private static bool PlanarAndAffine(MeshData mesh, out Vector3 normal)
        {
            var a = mesh.Positions[mesh.Indices[0]]; var b = mesh.Positions[mesh.Indices[1]]; var c = mesh.Positions[mesh.Indices[2]];
            // Sur un petit triangle translate, le produit vectoriel des float
            // perd plus de precision que les normales fournies par SolidWorks.
            normal = Vector3.Normalize(mesh.Normals[mesh.Indices[0]]);
            float size = Vector3.Distance(mesh.Positions.Aggregate(Vector3.Min), mesh.Positions.Aggregate(Vector3.Max));
            float tolerance = Math.Max(1e-12f, size * 1e-6f);
            foreach (var p in mesh.Positions) if (Math.Abs(Vector3.Dot(p - a, normal)) > tolerance) return false;
            if (mesh.TexCoords.Count == 0) return true;
            return AffineUv(mesh, mesh);
        }

        private static bool AffineUv(MeshData source, MeshData candidate)
        {
            if (source.TexCoords.Count == 0) return candidate.TexCoords.Count == 0;
            if (candidate.TexCoords.Count != candidate.Positions.Count) return false;
            int ia = source.Indices[0], ib = source.Indices[1], ic = source.Indices[2];
            var a = source.Positions[ia]; var u = source.Positions[ib] - a; var v = source.Positions[ic] - a;
            double uu = Vector3.Dot(u, u), uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v);
            double denominator = uu * vv - uv * uv;
            if (Math.Abs(denominator) < 1e-30) return false;
            for (int i = 0; i < candidate.Positions.Count; i++)
            {
                var p = candidate.Positions[i] - a;
                double pu = Vector3.Dot(p, u), pv = Vector3.Dot(p, v);
                float x = (float)((pu * vv - pv * uv) / denominator), y = (float)((pv * uu - pu * uv) / denominator);
                var expected = source.TexCoords[ia] + x * (source.TexCoords[ib] - source.TexCoords[ia]) + y * (source.TexCoords[ic] - source.TexCoords[ia]);
                if (Vector2.Distance(expected, candidate.TexCoords[i]) > 1e-5f) return false;
            }
            return true;
        }

        private static bool SamePlanarSurface(MeshData source, MeshData candidate, Vector3 normal)
        {
            if (!PlanarAndAffine(candidate, out var newNormal) || Vector3.Dot(normal, newNormal) < 0.999999f || !AffineUv(source, candidate)) return false;
            var originalBoundary = Boundary(source); var candidateBoundary = Boundary(candidate);
            if (!originalBoundary.SetEquals(candidateBoundary) && !SameBoundary(originalBoundary, candidateBoundary)) return false;
            double before = Area(source), after = Area(candidate);
            double size = Vector3.Distance(source.Positions.Aggregate(Vector3.Min), source.Positions.Aggregate(Vector3.Max));
            if (Math.Abs(before - after) > Math.Max(before * 1e-6, size * FloatTolerance(source) * 4)) return false;
            return candidate.Normals.All(n => Vector3.DistanceSquared(n, normal) < 1e-6f);
        }

        private static MeshData Retriangulate(MeshData source, Vector3 normal)
        {
            var edgeCounts = new Dictionary<Tuple<int, int>, List<int>>();
            for (int i = 0; i < source.Indices.Count; i += 3)
                for (int j = 0; j < 3; j++) AddEdge(edgeCounts, source.Indices[i + j], source.Indices[i + (j + 1) % 3], i / 3);
            var neighbors = new Dictionary<int, List<int>>();
            foreach (var edge in edgeCounts.Where(e => e.Value.Count == 1))
            {
                foreach (var pair in new[] { Tuple.Create(edge.Key.Item1, edge.Key.Item2), Tuple.Create(edge.Key.Item2, edge.Key.Item1) })
                {
                    if (!neighbors.TryGetValue(pair.Item1, out var list)) { list = new List<int>(2); neighbors.Add(pair.Item1, list); }
                    list.Add(pair.Item2);
                }
            }
            if (neighbors.Count < 3 || neighbors.Count > 2048 || neighbors.Values.Any(n => n.Count != 2)) return null;
            int first = neighbors.Keys.First(), previous = -1, current = first;
            var polygon = new List<int>(); var seen = new HashSet<int>();
            do
            {
                if (!seen.Add(current)) return null;
                polygon.Add(current);
                int next = neighbors[current][0] == previous ? neighbors[current][1] : neighbors[current][0];
                previous = current; current = next;
            } while (current != first && polygon.Count <= neighbors.Count);
            // Plusieurs boucles = trous : conserver leurs bords avec le chemin QEM.
            if (polygon.Count != neighbors.Count) return null;
            bool changed;
            float contourTolerance = FloatTolerance(source);
            do
            {
                changed = false;
                for (int i = 0; i < polygon.Count && polygon.Count > 3; i++)
                {
                    var a = source.Positions[polygon[(i + polygon.Count - 1) % polygon.Count]];
                    var b = source.Positions[polygon[i]]; var c = source.Positions[polygon[(i + 1) % polygon.Count]];
                    var u = b - a; var v = c - b;
                    if (Vector3.Dot(u, v) > 0 && OnSegment(b, a, c, contourTolerance))
                    { polygon.RemoveAt(i); changed = true; i--; }
                }
            } while (changed);
            var origin = source.Positions[polygon[0]];
            var xAxis = Vector3.Normalize(source.Positions[polygon[1]] - origin); var yAxis = Vector3.Cross(normal, xAxis);
            var points = polygon.Select(i => new Vector2(Vector3.Dot(source.Positions[i] - origin, xAxis), Vector3.Dot(source.Positions[i] - origin, yAxis))).ToList();
            double signedArea = 0;
            for (int i = 0; i < points.Count; i++) signedArea += (double)points[i].X * points[(i + 1) % points.Count].Y - (double)points[i].Y * points[(i + 1) % points.Count].X;
            if (signedArea < 0) { polygon.Reverse(); points.Reverse(); }
            var output = EmptyLike(source);
            foreach (int index in polygon)
            {
                output.Positions.Add(source.Positions[index]); output.Normals.Add(source.Normals[index]);
                if (source.TexCoords.Count > 0) output.TexCoords.Add(source.TexCoords[index]);
            }
            var remaining = Enumerable.Range(0, polygon.Count).ToList();
            double epsilon = Math.Max(1e-30, Math.Abs(signedArea) * 1e-12);
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross2(points[a], points[b], points[c]) <= epsilon) continue;
                    bool blocked = remaining.Any(p => p != a && p != b && p != c && Cross2(points[a], points[b], points[p]) >= -epsilon &&
                        Cross2(points[b], points[c], points[p]) >= -epsilon && Cross2(points[c], points[a], points[p]) >= -epsilon);
                    if (blocked) continue;
                    output.Indices.AddRange(new[] { a, b, c }); remaining.RemoveAt(i); found = true; break;
                }
                if (!found) return null;
            }
            output.Indices.AddRange(remaining);
            return output;
        }

        private static double Cross2(Vector2 a, Vector2 b, Vector2 c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

        // Precision des float CAD : depend aussi de la distance a l'origine.
        private static float FloatTolerance(MeshData mesh) => Math.Max(1e-12f,
            mesh.Positions.Max(p => Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z)))) * 2e-7f);

        private static bool SameBoundary(HashSet<Tuple<Vector3, Vector3>> before, HashSet<Tuple<Vector3, Vector3>> after)
        {
            if (before.Count == 0 || after.Count == 0) return false;
            double beforeLength = before.Sum(e => (double)Vector3.Distance(e.Item1, e.Item2)), afterLength = after.Sum(e => (double)Vector3.Distance(e.Item1, e.Item2));
            if (Math.Abs(beforeLength - afterLength) > beforeLength * 1e-6) return false;
            float tolerance = (float)Math.Max(1e-12, beforeLength * 1e-6);
            foreach (var edge in before)
                if (!after.Any(e => OnSegment(edge.Item1, e.Item1, e.Item2, tolerance) && OnSegment(edge.Item2, e.Item1, e.Item2, tolerance))) return false;
            // Verifier chaque nouveau segment entier par ses extremites et son milieu.
            foreach (var edge in after)
                foreach (var p in new[] { edge.Item1, edge.Item2, (edge.Item1 + edge.Item2) * 0.5f })
                    if (!before.Any(e => OnSegment(p, e.Item1, e.Item2, tolerance))) return false;
            return true;
        }

        private static bool OnSegment(Vector3 p, Vector3 a, Vector3 b, float tolerance)
        {
            var edge = b - a; float lengthSquared = edge.LengthSquared();
            if (lengthSquared == 0) return Vector3.Distance(p, a) <= tolerance;
            float t = Vector3.Dot(p - a, edge) / lengthSquared;
            return t >= -1e-7f && t <= 1.0000001f && Vector3.Distance(p, a + Math.Max(0, Math.Min(1, t)) * edge) <= tolerance;
        }

        private static HashSet<Tuple<Vector3, Vector3>> Boundary(MeshData mesh)
        {
            var edges = new Dictionary<Tuple<int, int>, List<int>>();
            for (int i = 0; i < mesh.Indices.Count; i += 3)
                for (int j = 0; j < 3; j++) AddEdge(edges, mesh.Indices[i + j], mesh.Indices[i + (j + 1) % 3], i / 3);
            var boundary = new HashSet<Tuple<Vector3, Vector3>>();
            foreach (var edge in edges.Where(e => e.Value.Count == 1))
            {
                var a = mesh.Positions[edge.Key.Item1]; var b = mesh.Positions[edge.Key.Item2];
                if (Compare(a, b) > 0) { var temp = a; a = b; b = temp; }
                boundary.Add(Tuple.Create(a, b));
            }
            return boundary;
        }

        private static int Compare(Vector3 a, Vector3 b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
        private static double Area(MeshData mesh)
        {
            double area = 0;
            for (int i = 0; i < mesh.Indices.Count; i += 3)
                area += Vector3.Cross(mesh.Positions[mesh.Indices[i + 1]] - mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 2]] - mesh.Positions[mesh.Indices[i]]).Length() * 0.5;
            return area;
        }
        private static Tuple<int, int> Edge(int a, int b) => a < b ? Tuple.Create(a, b) : Tuple.Create(b, a);
        private static void AddEdge(Dictionary<Tuple<int, int>, List<int>> edges, int a, int b, int t)
        {
            var key = Edge(a, b);
            if (!edges.TryGetValue(key, out var adjacent)) { adjacent = new List<int>(2); edges.Add(key, adjacent); }
            adjacent.Add(t);
        }
        private static MeshData ExtractPatch(MeshData source, List<int> triangles)
        {
            var output = EmptyLike(source); var map = new Dictionary<int, int>();
            foreach (int t in triangles)
            for (int j = 0; j < 3; j++)
            {
                int old = source.Indices[t * 3 + j];
                if (!map.TryGetValue(old, out int index))
                {
                    index = output.Positions.Count; map.Add(old, index);
                    output.Positions.Add(source.Positions[old]); output.Normals.Add(source.Normals[old]);
                    if (source.TexCoords.Count != 0) output.TexCoords.Add(source.TexCoords[old]);
                }
                output.Indices.Add(index);
            }
            return output;
        }
        private static bool Valid(MeshData mesh) => mesh.Indices.Count >= 3 && mesh.Indices.Count % 3 == 0 &&
            mesh.Normals.Count == mesh.Positions.Count && (mesh.TexCoords.Count == 0 || mesh.TexCoords.Count == mesh.Positions.Count) &&
            mesh.Indices.All(i => i >= 0 && i < mesh.Positions.Count) && mesh.Positions.All(p => Finite(p.X) && Finite(p.Y) && Finite(p.Z)) &&
            mesh.Normals.All(n => Finite(n.X) && Finite(n.Y) && Finite(n.Z) && n.LengthSquared() > 1e-20f) &&
            mesh.TexCoords.All(uv => Finite(uv.X) && Finite(uv.Y));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static MeshData EmptyLike(MeshData source) => new MeshData { Name = source.Name, Color = source.Color, TexturePath = source.TexturePath };
        private static void Append(MeshData source, MeshData target)
        {
            int offset = target.Positions.Count;
            target.Positions.AddRange(source.Positions); target.Normals.AddRange(source.Normals); target.TexCoords.AddRange(source.TexCoords);
            target.Indices.AddRange(source.Indices.Select(i => i + offset));
        }
        private static void Copy(MeshData source, MeshData target)
        {
            target.Positions.Clear(); target.Positions.AddRange(source.Positions); target.Normals.Clear(); target.Normals.AddRange(source.Normals);
            target.TexCoords.Clear(); target.TexCoords.AddRange(source.TexCoords); target.Indices.Clear(); target.Indices.AddRange(source.Indices);
        }
    }
}
