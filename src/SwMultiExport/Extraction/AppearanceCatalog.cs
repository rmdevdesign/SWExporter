using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwMultiExport.Util;

namespace SwMultiExport.Extraction
{
    internal sealed class AppearanceCatalog
    {
        private readonly Dictionary<object, string> textures = new Dictionary<object, string>();
        private readonly List<string> warnings;
        private readonly string sourceDirectory;

        public AppearanceCatalog(IModelDoc2 document, List<string> warnings)
        {
            this.warnings = warnings;
            string documentPath = document.GetPathName();
            // Path.GetDirectoryName("") leve ArgumentException sur .NET Framework.
            // Une piece non enregistree n'a aucun dossier pour les chemins relatifs.
            sourceDirectory = string.IsNullOrWhiteSpace(documentPath) ? null : Path.GetDirectoryName(documentPath);
            try
            {
                Read(document.Extension.GetRenderMaterials2((int)swDisplayStateOpts_e.swThisDisplayState, null) as object[], document);
            }
            catch (Exception ex) { Warn("Lecture des apparences : " + ex.Message); }
        }

        public void ReadComponent(IComponent2 component)
        {
            try { Read(component.GetRenderMaterials2((int)swDisplayStateOpts_e.swThisDisplayState, null) as object[], component); }
            catch (Exception ex) { Warn("Apparences du composant " + component.Name2 + " : " + ex.Message); }
        }

        private void Read(object[] materials, object defaultEntity)
        {
            foreach (IRenderMaterial material in materials ?? new object[0])
            {
                string path = ResolveImage(material.TextureFilename);
                if (path == null) continue;
                var entities = material.GetEntities() as object[];
                if (entities == null || entities.Length == 0) textures[defaultEntity] = path;
                else foreach (object entity in entities)
                {
                    if (entity is IFeature feature)
                    {
                        foreach (object face in feature.GetFaces() as object[] ?? new object[0]) textures[face] = path;
                    }
                    else textures[entity] = path;
                }
            }
        }

        public string Find(IFace2 face, IBody2 body, IComponent2 component, IModelDoc2 document)
        {
            if (textures.TryGetValue(face, out var path)) return path;
            if (textures.TryGetValue(body, out path)) return path;
            if (component != null && textures.TryGetValue(component, out path)) return path;
            if (textures.TryGetValue(document, out path)) return path;
            // Repli pour les anciennes textures definies directement sur les entites.
            try
            {
                var texture = face.GetTexture(document.ConfigurationManager.ActiveConfiguration.Name) as ITexture;
                if (texture == null && component != null) texture = component.GetTexture(component.ReferencedConfiguration) as ITexture;
                if (texture == null) texture = document.Extension.GetTexture(document.ConfigurationManager.ActiveConfiguration.Name) as ITexture;
                return texture == null ? null : ResolveImage(texture.MaterialName);
            }
            catch (Exception ex) { Logger.Log("Lecture texture historique : " + ex.Message); return null; }
        }

        private string ResolveImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(sourceDirectory)) path = Path.Combine(sourceDirectory, path);
            if (!File.Exists(path)) { Warn("Texture introuvable : " + path); return null; }
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp" && extension != ".tif" && extension != ".tiff")
            {
                Warn("Apparence non exportable comme image : " + path);
                return null;
            }
            return Path.GetFullPath(path);
        }

        public void Warn(string message)
        {
            if (!warnings.Contains(message)) warnings.Add(message);
            Logger.Log(message);
        }
    }
}
