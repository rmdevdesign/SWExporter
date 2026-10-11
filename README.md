# SW Multi Export

Add-in SolidWorks x64, .NET Framework 4.8. Export de geometrie triangulee en **GLB**, **glTF 2.0** et **FBX**.

## Utilisation

1. Fermer SolidWorks avant de compiler ou de mettre a jour la DLL.
2. Compiler avec `dotnet build SwMultiExport.sln -p:Platform=x64`.
3. Pour une premiere installation, executer `scripts/register.ps1` dans PowerShell administrateur. Une mise a jour au meme chemin ne demande pas de nouvel enregistrement.
4. Relancer SolidWorks et activer **Outils > Complements > SW Multi Export**.
5. Ouvrir une piece ou un assemblage. L'onglet **SW Multi Export** du CommandManager contient **Exporter...**.
6. Choisir les formats, le dossier, le nom sans extension et l'echelle. Echelle 1 = metres.

La fenetre s'ouvre plus large, reste dans les limites de l'ecran et permet de faire defiler tout le contenu. Les boutons Exporter et Annuler restent visibles. Les pieces non enregistrees sont exportables ; le dossier propose est alors Documents.

Trois modes d'optimisation sont disponibles :

- **Automatique** (par defaut) : retriangulation conservatrice des zones planes a normales uniformes, avec tolerance aux arrondis des positions et normales de tessellation CAD. La connexite utilise les positions pour retrouver les plans meme lorsque les normales dupliquent les sommets. Un contour simple est reduit a n-2 triangles apres suppression des points collineaires inutiles. Les contours, trous, aires et UV affines sont verifies avant d'accepter le resultat. Les courbes et les textures non affines restent intactes. Un rectangle plan peut passer a deux triangles. Un modele essentiellement courbe peut donc avoir un faible gain en mode automatique.
- **Reglable** : pourcentage de triangles a conserver, de 1 a 100 %. Decimation QEM avec protection des bords, ruptures de normales et coutures UV. Elle peut modifier les courbes et les petits details ; la cible n'est pas garantie si les protections l'empechent.
- **Maillage original** : aucune decimation, mais fusion des sommets identiques et partage des occurrences.

Le resultat et les logs indiquent les triangles avant et apres ainsi que le pourcentage de reduction. Le compteur tient compte des occurrences dans la scene, meme si leurs maillages sont partages dans le fichier. L'echelle et les textures ne sont pas reduites par ces modes.

Si le CommandManager est masque, l'activer dans SolidWorks. L'onglet est disponible pour les pieces et assemblages. Le menu et la barre d'outils donnent aussi acces a la commande.

Les DLL gerees et le dossier `runtimes/win-x64/native` doivent rester a cote de `SwMultiExport.dll`. L'export ne lance pas de convertisseur externe. SharpGLTF ecrit GLB/glTF et Assimp ecrit FBX dans le processus SolidWorks. GLB/glTF sont en metres ; FBX declare des centimetres et les coordonnees sont converties pour conserver les memes dimensions physiques.

## Premiere validation dans SolidWorks

- Piece simple : exporter les trois formats, ouvrir les fichiers dans un lecteur 3D et comparer dimensions, couleurs et orientation.
- Le fichier glTF peut etre accompagne d'un `.bin` : conserver ces fichiers ensemble.
- Assemblage : tester deux occurrences d'une piece, dont une translatee et pivotee, puis une piece en miroir. Verifier les positions et les composants caches.
- Les transformations des corps de composants doivent encore etre validees sur un assemblage reel SolidWorks 2023.
- Logs : `%LOCALAPPDATA%/SwMultiExport/logs`.

## Limites actuelles

La reduction automatique peut rester tres faible sur un modele reel : un assemblage teste est passe de 233 942 a 233 700 triangles (0,1 %). Ce mode conserve les courbes et les contours ; il ne garantit pas un gain important sur tous les modeles. Utiliser le mode reglable pour reduire davantage, avec verification visuelle du resultat.

La qualite utilise la tessellation courante de SolidWorks. En cas de tessellation absente, afficher le modele en mode ombre et reconstruire. Un noeud est cree par corps, avec des primitives par materiau. Les sommets identiques sont fusionnes en conservant normales et coutures UV. Les maillages identiques des occurrences sont partages, avec une variante geometrique pour les miroirs.

Les couleurs, la transparence et les textures de couleur basees sur des images sont exportees lorsque SolidWorks expose les coordonnees UV. GLB embarque les images ; conserver les images externes generees avec le glTF. Le FBX utilise un dossier `<nom>_textures` qui doit l'accompagner. Les textures absentes, les formats d'image non pris en charge et les UV indisponibles sont signales dans le resultat et les logs. Les shaders proceduraux RealView, les decals et les effets avances de rendu ne sont pas convertis en images par cette version.

Les corps des composants sont des noeuds distincts sous une racine, sans reconstruction de la hierarchie des sous-assemblages. Les composants legers sont resolus lors de l'export. Les dessins ne sont pas exportables.

## Test des exporteurs

```powershell
dotnet build tests/ExportSmoke/ExportSmoke.csproj -p:Platform=x64
& ./tests/ExportSmoke/bin/x64/Debug/net48/ExportSmoke.exe ./tests/ExportSmoke/artifacts
```

Le test genere et relit les trois formats avec une scene contenant une rotation, une translation, une echelle, un miroir, une texture et une occurrence repetee. Il verifie le nombre de triangles, les bornes et la coherence faces/normales, les unites declarees dans le FBX, les liens de texture, les UV et le partage des maillages. Il verifie aussi la fusion des sommets sans perte des coutures UV, la retriangulation d'un plan de 800 a 2 triangles, un plan avec trou, la conservation des courbes en mode automatique et une decimation reglable de 800 a 400 triangles. Les fichiers GLB/glTF sont aussi relus par SharpGLTF. Ce test ne remplace pas la validation de l'extraction dans SolidWorks.

La decimation QEM utilise les sources MIT de [MeshDecimator](https://github.com/Whinarn/MeshDecimator), conservees dans `third_party/MeshDecimator` avec leur licence. Aucun convertisseur externe n'est lance.
