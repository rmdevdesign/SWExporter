# SW Multi Export : add-in SolidWorks d'export multi-format

## Objectif

Add-in SolidWorks (DLL .NET, COM) qui exporte la pièce ou l'assemblage actif vers plusieurs formats 3D en une seule action : **GLB, glTF, FBX, OBJ, STL** (extensible).

- Version de test : **SolidWorks 2023** (seule version disponible pour valider).
- Cible : rester compatible avec un maximum de versions (idéalement 2018 et plus). Voir la section "Compatibilité".

## Contraintes techniques

- **.NET Framework 4.8**, plateforme **x64** uniquement. Pas de .NET 6/8 : SolidWorks charge les add-ins via COM sur le Framework classique.
- Projet SDK-style accepté (on développe dans Rider), mais l'enregistrement COM se fait par script (`RegisterForComInterop` ne fonctionne pas avec dotnet build).
- Références d'interop depuis l'installation locale : `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`
  - `SolidWorks.Interop.sldworks.dll`
  - `SolidWorks.Interop.swconst.dll`
  - `SolidWorks.Interop.swpublished.dll`
  - Chemin à rendre configurable via une propriété MSBuild (`SwInstallDir`) plutôt qu'en dur.
  - `EmbedInteropTypes = false`.
- Dépendances NuGet :
  - **SharpGLTF.Toolkit** pour glTF/GLB (pur C#, netstandard2.0).
  - **AssimpNet** pour FBX, OBJ, STL et les autres formats. Vérifier que la DLL native assimp est bien copiée dans le dossier de sortie et que l'export FBX fonctionne avec la version choisie.
- Rider est lancé en administrateur (écriture dans `HKLM`).

## Architecture

Séparer strictement l'extraction SolidWorks de l'écriture des formats.

```
SolidWorks -> Extraction -> SceneData (modèle neutre) -> IExporter (un par format)
```

```
src/SwMultiExport/
  Addin/          SwAddin.cs (ISwAddin, menu/toolbar), enregistrement COM
  Extraction/     SwSceneExtractor.cs (pièce, assemblage, couleurs, transforms)
  Model/          SceneData, SceneNode, MeshData, MaterialData, ExportOptions
  Exporters/      IExporter.cs, GltfExporter.cs (SharpGLTF), AssimpExporter.cs
  UI/             ExportDialog (WinForms) : formats, dossier, options
  Util/           Logger (fichier texte dans %LOCALAPPDATA%\SwMultiExport\logs)
scripts/
  register.ps1    regasm /codebase (Framework64)
  unregister.ps1
```

### Modèle neutre

- `SceneNode` : nom, matrice locale 4x4, enfants, référence optionnelle vers un `MeshData`, dictionnaire de métadonnées (string -> string).
- `MeshData` : positions, normales, indices, sous-meshes par matériau. Partagé entre instances.
- `MaterialData` : couleur RGBA, metallic, roughness, emissive.
- `ExportOptions` : facteur d'échelle, axe vertical (Y-up / Z-up), qualité de tessellation, hiérarchie conservée ou fusionnée, dossier de sortie, formats cochés.

### Interface exporteur

```csharp
public interface IExporter
{
    string Name { get; }
    string Extension { get; }
    void Export(SceneData scene, string path, ExportOptions options);
}
```

- `GltfExporter` : gère `.glb` et `.gltf`, matériaux PBR, instanciation, métadonnées dans `extras`.
- `AssimpExporter(formatId, extension)` : un exporteur générique paramétré (`fbx`, `obj`, `stl`, etc.). Lister les formats disponibles via `AssimpContext.GetSupportedExportFormats()`.

## Extraction SolidWorks

- Unités API SolidWorks : **mètres**.
- Pièce : `PartDoc.GetBodies2(swSolidBody, true)` -> `Body2.GetFaces()` -> par face :
  - `Face2.GetTessellationTriangles(true, false, false)` : float[], 9 valeurs par triangle.
  - `Face2.GetTessellationTriNorms()` : normales, même découpage.
- Couleurs (`MaterialPropertyValues`, double[9] : R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission). Priorité : face > corps > composant (surcharge d'apparence) > document.
- Assemblage : `AssemblyDoc.GetComponents(false)` pour la hiérarchie, `Component2.Transform2.ArrayData` (9 valeurs de rotation, 3 de translation, 1 d'échelle).
  - Ignorer les composants supprimés ou cachés.
  - Instanciation : clé = chemin du fichier + configuration référencée. Une même pièce n'est tessellée qu'une fois.
  - **À vérifier par un test** : dans quel repère sont renvoyés les sommets des corps obtenus via un composant (local pièce ou assemblage). Tester avec un composant décalé et pivoté avant de généraliser.
- Pièces légères (lightweight) : les résoudre avant extraction ou les signaler dans le log.

## Compatibilité multi-versions

- Compiler contre les interops de 2023, mais **n'utiliser que des méthodes API anciennes et stables**. En cas de doute sur l'ancienneté d'une méthode, le noter dans un commentaire `// API: disponible depuis SW xxxx ?` et privilégier une alternative plus ancienne.
- Lire la version au démarrage via `ISldWorks.RevisionNumber()` (2023 = 31.x) et la logger.
- Tout appel susceptible de ne pas exister sur une version ancienne est isolé dans une méthode avec try/catch et repli.
- Ne pas dépendre de fonctionnalités d'export natives récentes de SolidWorks : toute la tessellation passe par l'API.

## UI

- Menu ou bouton dans SolidWorks via `ICommandManager` (fallback `AddMenuItem5` si problème).
- Fenêtre d'export WinForms : cases à cocher des formats, dossier de sortie (par défaut à côté du fichier source), échelle, axe vertical, qualité (grossier / normal / fin), hiérarchie oui/non.
- Mémoriser les derniers réglages (fichier JSON dans `%LOCALAPPDATA%`).

## Ordre de développement

1. Add-in minimal qui se charge dans SolidWorks 2023 avec un bouton affichant un message. Scripts `register.ps1` / `unregister.ps1`.
2. Extraction d'une **pièce simple** vers `SceneData` + export **GLB**. Vérification visuelle dans un viewer glTF.
3. Couleurs et matériaux.
4. `AssimpExporter` : FBX, OBJ, STL.
5. Assemblages : hiérarchie, transforms, instanciation, composants cachés/supprimés.
6. Fenêtre d'options et persistance des réglages.
7. Export par lot d'un dossier (ouverture silencieuse avec `OpenDoc6`), en dernier.

À chaque étape : compiler, enregistrer, et me dire exactement quoi tester dans SolidWorks.

## Hors périmètre v1

- Textures d'apparences SolidWorks.
- Export vers Blender (`.blend`) : à rediscuter plus tard.
- Animations, études de mouvement.

## Règles de travail

- Pas de caractère "—" (tiret cadratin) dans le code, les commentaires, les messages ou la doc.
- Logs détaillés pendant l'extraction (nombre de corps, faces, triangles, composants) pour faciliter le débogage sans débogueur.
- Avant de toucher au registre ou de lancer `regasm`, le dire.
