using System;
using System.Linq;
using System.Numerics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwMultiExport.Model;
using SwMultiExport.Util;

namespace SwMultiExport.Extraction
{
    public sealed class SwSceneExtractor
    {
        public SceneData Extract(IModelDoc2 document)
        {
            var scene = new SceneData { Name = document.GetTitle() };
            var appearances = new AppearanceCatalog(document, scene.Warnings);
            int type = document.GetType();
            if (type == (int)swDocumentTypes_e.swDocPART)
            {
                var node = new SceneNode { Name = scene.Name };
                ExtractBodies(((IPartDoc)document).GetBodies2((int)swBodyType_e.swAllBodies, true) as object[],
                    node, ReadColor(document.MaterialPropertyValues, new Vector4(0.7f, 0.7f, 0.7f, 1)), scene, appearances, document, null);
            }
            else if (type == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                var assembly = (IAssemblyDoc)document;
                // API: disponible depuis SW 2006. Les composants restent resolus apres export.
                int resolved = assembly.ResolveAllLightWeightComponents(false);
                Logger.Log("Resolution des composants legers : " + resolved);
                foreach (IComponent2 component in assembly.GetComponents(false) as object[] ?? new object[0])
                {
                    if (component.IsSuppressed() || component.IsHidden(true)) continue;
                    var model = component.GetModelDoc2() as IModelDoc2;
                    if (model == null)
                        throw new InvalidOperationException("Composant non charge : " + component.Name2 + ". Resoudre les composants avant l'export.");
                    if (model.GetType() != (int)swDocumentTypes_e.swDocPART) continue;
                    var partAppearances = new AppearanceCatalog(model, scene.Warnings);
                    partAppearances.ReadComponent(component);
                    object info;
                    var solidBodies = component.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[] ?? new object[0];
                    var sheetBodies = component.GetBodies3((int)swBodyType_e.swSheetBody, out info) as object[] ?? new object[0];
                    var bodies = solidBodies.Concat(sheetBodies).ToArray();
                    var node = new SceneNode { Name = component.Name2, Transform = FromSolidWorksTransform(component.Transform2?.ArrayData as double[]) };
                    var color = ReadColor(component.MaterialPropertyValues,
                        ReadColor(model.MaterialPropertyValues, new Vector4(0.7f, 0.7f, 0.7f, 1)));
                    ExtractBodies(bodies, node, color, scene, partAppearances, model, component);
                }
            }
            else throw new InvalidOperationException("Ouvrir une piece ou un assemblage pour exporter.");
            int count = scene.Nodes.Sum(n => n.Meshes.Sum(m => m.Indices.Count / 3));
            Logger.Log("Extraction terminee : " + scene.Nodes.Count + " noeuds, " + count + " triangles");
            if (count == 0) throw new InvalidOperationException("Aucun triangle disponible. Afficher le modele en mode ombre, reconstruire puis reessayer.");
            int before = scene.Nodes.Sum(n => n.Meshes.Sum(m => m.Positions.Count));
            SceneOptimizer.Optimize(scene);
            var uniqueMeshes = scene.Nodes.SelectMany(n => n.Meshes).Distinct().ToArray();
            Logger.Log("Optimisation : " + before + " sommets avant, " + uniqueMeshes.Sum(m => m.Positions.Count) + " sommets uniques, " + uniqueMeshes.Length + " maillages partages");
            return scene;
        }

        private static void ExtractBodies(object[] bodies, SceneNode sourceNode, Vector4 defaultColor,
            SceneData scene, AppearanceCatalog appearances, IModelDoc2 document, IComponent2 component)
        {
            Logger.Log("Extraction " + sourceNode.Name + " : " + (bodies?.Length ?? 0) + " corps");
            int bodyIndex = 0;
            foreach (IBody2 body in bodies ?? new object[0])
            {
                // Les noms natifs de corps peuvent etre ceux des fonctions SW.
                // Un objet par corps, jamais un objet par face ou fonction.
                var node = new SceneNode { Name = sourceNode.Name + "/Corps_" + ++bodyIndex, Transform = sourceNode.Transform };
                var faces = body.GetFaces() as object[] ?? new object[0];
                var bodyColor = ReadColor(body.MaterialPropertyValues2, defaultColor);
                Logger.Log("Corps " + body.Name + " : " + faces.Length + " faces");
                int faceIndex = 0;
                foreach (IFace2 face in faces)
                {
                    // API: SW 2001Plus. true conserve les metres, independamment des unites du document.
                    var values = face.GetTessTriangles(true) as float[];
                    var normals = face.GetTessNorms() as float[];
                    if (values == null || values.Length == 0)
                        throw new InvalidOperationException("Tessellation absente sur une face de " + node.Name + ". Reconstruire le modele en mode ombre.");
                    if (values.Length % 9 != 0) throw new InvalidOperationException("Tessellation invalide : " + node.Name);
                    faceIndex++;
                    var color = ReadColor(face.MaterialPropertyValues, bodyColor);
                    string texturePath = appearances.Find(face, body, component, document);
                    Array textureValues = texturePath == null ? null : face.GetTessTextures() as Array;
                    int vertexCount = values.Length / 3;
                    int uvStride = textureValues != null && textureValues.Length == vertexCount * 2 ? 2 :
                        textureValues != null && textureValues.Length == vertexCount * 3 ? 3 : 0;
                    if (texturePath != null && uvStride == 0)
                    {
                        appearances.Warn("Texture sans coordonnees UV exploitables sur " + node.Name + " : " + texturePath);
                        texturePath = null;
                    }
                    var mesh = node.Meshes.FirstOrDefault(m => m.Color == color && m.TexturePath == texturePath);
                    if (mesh == null)
                    {
                        mesh = new MeshData { Name = "Materiau_" + (node.Meshes.Count + 1), Color = color, TexturePath = texturePath };
                        node.Meshes.Add(mesh);
                    }
                    int faceTriangles = 0;
                    for (int i = 0; i < values.Length; i += 9)
                    {
                        var a = ReadVector(values, i);
                        var b = ReadVector(values, i + 3);
                        var c = ReadVector(values, i + 6);
                        var cross = Vector3.Cross(b - a, c - a);
                        if (!Finite(a) || !Finite(b) || !Finite(c)) throw new InvalidOperationException("Sommet non fini : " + node.Name);
                        if (cross.LengthSquared() < 1e-30f) continue;
                        var geometricNormal = Vector3.Normalize(cross);
                        int start = mesh.Positions.Count;
                        mesh.Positions.AddRange(new[] { a, b, c });
                        for (int j = 0; j < 9; j += 3)
                        {
                            var n = normals != null && normals.Length == values.Length ? ReadVector(normals, i + j) : geometricNormal;
                            mesh.Normals.Add(Finite(n) && n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : geometricNormal);
                            if (texturePath != null)
                            {
                                int uvIndex = ((i + j) / 3) * uvStride;
                                var uv = new Vector2(Convert.ToSingle(textureValues.GetValue(uvIndex)), Convert.ToSingle(textureValues.GetValue(uvIndex + 1)));
                                if (float.IsNaN(uv.X) || float.IsInfinity(uv.X) || float.IsNaN(uv.Y) || float.IsInfinity(uv.Y))
                                    throw new InvalidOperationException("Coordonnees UV invalides : " + node.Name);
                                mesh.TexCoords.Add(uv);
                            }
                        }
                        bool reverse = Vector3.Dot(geometricNormal, mesh.Normals[start] + mesh.Normals[start + 1] + mesh.Normals[start + 2]) < 0;
                        mesh.Indices.AddRange(new[] { start, start + (reverse ? 2 : 1), start + (reverse ? 1 : 2) });
                        faceTriangles++;
                    }
                    Logger.Log("Face " + (faceIndex - 1) + " : " + faceTriangles + " triangles");
                }
                node.Meshes.RemoveAll(m => m.Indices.Count == 0);
                if (node.Meshes.Count > 0) scene.Nodes.Add(node);
            }
        }

        public static Matrix4x4 FromSolidWorksTransform(double[] a)
        {
            if (a == null) return Matrix4x4.Identity;
            if (a.Length < 13) throw new InvalidOperationException("Transformation SolidWorks invalide.");
            float s = (float)a[12];
            return new Matrix4x4((float)a[0] * s, (float)a[1] * s, (float)a[2] * s, 0,
                (float)a[3] * s, (float)a[4] * s, (float)a[5] * s, 0,
                (float)a[6] * s, (float)a[7] * s, (float)a[8] * s, 0,
                (float)a[9], (float)a[10], (float)a[11], 1);
        }

        private static Vector3 ReadVector(float[] a, int i) => new Vector3(a[i], a[i + 1], a[i + 2]);
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.X) || float.IsInfinity(v.X) || float.IsNaN(v.Y) || float.IsInfinity(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.Z));
        private static Vector4 ReadColor(object value, Vector4 fallback)
        {
            var a = value as double[];
            if (a == null || a.Length < 9 || a.Take(3).Any(v => v < 0 || v > 1 || double.IsNaN(v))) return fallback;
            return new Vector4((float)a[0], (float)a[1], (float)a[2], (float)Math.Max(0, Math.Min(1, 1 - a[7])));
        }
    }
}
