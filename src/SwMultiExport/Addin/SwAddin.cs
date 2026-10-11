using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SwMultiExport.Util;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using SwMultiExport.Extraction;
using SwMultiExport.Exporters;
using SwMultiExport.UI;
using SwMultiExport.Model;

namespace SwMultiExport.Addin
{
    /// <summary>
    /// Point d'entree COM de l'add-in. Etape 1 : chargement minimal + un bouton
    /// qui affiche un message, pour valider la chaine complete
    /// (build -> regasm -> chargement dans SolidWorks).
    /// </summary>
    // ClassInterfaceType.AutoDispatch (le defaut .NET pour une classe ComVisible sans
    // attribut ClassInterface explicite) est indispensable ici : SolidWorks invoque les
    // callbacks des commandes (ex: "ShowHelloMessage") par liaison tardive IDispatch,
    // par nom de methode. ClassInterfaceType.None (essaye initialement) n'expose que
    // ISwAddin et empeche SW de resoudre ces callbacks, ce qui provoquait un plantage
    // natif systematique dans ICommandGroup.Activate() (confirme par analyse de dump).
    [ComVisible(true)]
    [Guid("17829f30-3734-46d1-9247-83b8cca9a3f4")]
    [ProgId("SwMultiExport.Addin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class SwAddin : ISwAddin
    {
        private const int MainCmdGroupId = 1;
        private const int CmdShowMessageUserId = 0;
        private const string CommandTabTitle = "SW Multi Export";
        private static readonly int[] ExportDocumentTypes =
        {
            (int)swDocumentTypes_e.swDocPART,
            (int)swDocumentTypes_e.swDocASSEMBLY
        };

        private ISldWorks swApp;
        private int addinCookie;
        private ICommandManager cmdMgr;

        #region Enregistrement COM (appele par regasm via /codebase)

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string keyPath = string.Format(@"SOFTWARE\SolidWorks\AddIns\{{{0}}}", t.GUID);
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.CreateSubKey(keyPath))
                {
                    rk.SetValue(null, 1);
                    rk.SetValue("Description", "SW Multi Export");
                    rk.SetValue("Title", "SW Multi Export");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erreur d'enregistrement COM de l'add-in : " + ex.Message);
                throw;
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            string keyPath = string.Format(@"SOFTWARE\SolidWorks\AddIns\{{{0}}}", t.GUID);
            try
            {
                Registry.LocalMachine.DeleteSubKey(keyPath, false);
            }
            catch (ArgumentException)
            {
                // Cle deja absente : rien a faire
            }
        }

        #endregion

        #region ISwAddin

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            // Tout est dans le try, y compris le cast initial : une exception non
            // interceptee ici remonte brut jusqu'a SolidWorks et peut faire planter
            // le process hote plutot que simplement desactiver l'add-in.
            try
            {
                Logger.Log("ConnectToSW : demarrage de l'add-in SW Multi Export");
                DependencyResolver.Register();

                swApp = (ISldWorks)ThisSW;
                addinCookie = cookie;

                LogRevisionNumber();
                // API: disponible depuis SolidWorks 2015. Enregistrer l'objet
                // recevant les callbacks avant de creer les commandes.
                bool callbacksRegistered = swApp.SetAddinCallbackInfo2(0, this, addinCookie);
                Logger.Log("SetAddinCallbackInfo2 : " + callbacksRegistered);
                if (!callbacksRegistered)
                {
                    throw new InvalidOperationException("SolidWorks a refuse l'enregistrement des callbacks de l'add-in.");
                }
                SetupUserInterface();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Erreur dans ConnectToSW : " + ex);
                DependencyResolver.Unregister();
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                Logger.Log("DisconnectFromSW : arret de l'add-in SW Multi Export");
                RemoveUserInterface();
            }
            catch (Exception ex)
            {
                Logger.Log("Erreur dans DisconnectFromSW : " + ex);
            }
            finally
            {
                swApp = null;
                DependencyResolver.Unregister();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return true;
        }

        #endregion

        #region Infos de version

        private void LogRevisionNumber()
        {
            // API: RevisionNumber disponible depuis les tres anciennes versions de SW (isole par precaution)
            try
            {
                string revision = swApp.RevisionNumber();
                Logger.Log("Version SolidWorks (RevisionNumber) : " + revision);
            }
            catch (Exception ex)
            {
                Logger.Log("Impossible de lire RevisionNumber : " + ex.Message);
            }
        }

        #endregion

        #region Interface utilisateur

        // [HandleProcessCorruptedStateExceptions] : necessaire pour pouvoir intercepter
        // une AccessViolationException (exception "d'etat corrompu"), que .NET refuse
        // de laisser passer dans un try/catch ordinaire depuis la 4.0. Confirme par
        // analyse de dump memoire : CommandGroupClass.Activate() provoque une vraie
        // AccessViolationException cote natif SolidWorks ; sans cet attribut, notre
        // try/catch ci-dessous ne se declenchait jamais et SolidWorks plantait
        // integralement au lieu de basculer sur le repli AddMenuItem5.
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void SetupUserInterface()
        {
            try
            {
                SetupCommandManagerUI();
            }
            catch (Exception ex)
            {
                Logger.Log("Echec de l'UI via ICommandManager (" + ex.Message + "), repli sur AddMenuItem5");
                SetupMenuFallback();
            }
        }

        private void SetupCommandManagerUI()
        {
            Logger.Log("SetupCommandManagerUI : GetCommandManager");
            cmdMgr = (ICommandManager)swApp.GetCommandManager(addinCookie);

            // Recreer les commandes pour mettre a jour les preferences de la version minimale.
            Logger.Log("SetupCommandManagerUI : CreateCommandGroup2");
            int errors = 0;
            ICommandGroup cmdGroup = cmdMgr.CreateCommandGroup2(
                MainCmdGroupId,
                "SW Multi Export",
                "Export multi-format",
                "Exporte la piece ou l'assemblage actif en GLB, glTF et FBX",
                -1,
                true,
                ref errors);

            Logger.Log("SetupCommandManagerUI : CreateCommandGroup2 errors=" + errors + ", cmdGroup null=" + (cmdGroup == null));

            if (cmdGroup == null)
            {
                throw new InvalidOperationException("CreateCommandGroup2 a renvoye null (erreurs=" + errors + ")");
            }

            // API: SW 2016. Bandes PNG indexees aux six tailles documentees.
            // Les callbacks sont enregistres avant de creer le groupe.
            Logger.Log("SetupCommandManagerUI : generation des icones");
            string[] icons = IconGenerator.GetIconPaths();
            cmdGroup.IconList = icons;
            cmdGroup.MainIconList = icons;

            Logger.Log("SetupCommandManagerUI : AddCommandItem2");
            int cmdIndex = cmdGroup.AddCommandItem2(
                "Exporter...",
                -1,
                "Afficher la fenetre d'export multi-format",
                "Exporter...",
                0,
                "ShowHelloMessage",
                "EnableExport",
                CmdShowMessageUserId,
                (int)swCommandItemType_e.swMenuItem + (int)swCommandItemType_e.swToolbarItem);

            Logger.Log("SetupCommandManagerUI : HasToolbar/HasMenu/Activate (cmdIndex=" + cmdIndex + ")");
            if (cmdIndex < 0) throw new InvalidOperationException("SolidWorks n'a pas cree la commande Exporter.");
            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = true;
            if (!cmdGroup.Activate()) throw new InvalidOperationException("SolidWorks n'a pas active le groupe de commandes.");

            int commandId = cmdGroup.get_CommandID(cmdIndex);
            foreach (int docType in ExportDocumentTypes)
            {
                // API: CommandTabs disponibles depuis SolidWorks 2008.
                var existing = cmdMgr.GetCommandTab(docType, CommandTabTitle);
                if (existing != null && !cmdMgr.RemoveCommandTab(existing))
                    throw new InvalidOperationException("Impossible de remplacer l'onglet SW Multi Export.");
                var tab = cmdMgr.AddCommandTab(docType, CommandTabTitle);
                if (tab == null) throw new InvalidOperationException("Impossible de creer l'onglet SW Multi Export.");
                var box = tab.AddCommandTabBox();
                if (box == null || !box.AddCommands(new[] { commandId },
                        new[] { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow }))
                    throw new InvalidOperationException("Impossible d'ajouter le bouton dans l'onglet SW Multi Export.");
                tab.Visible = true;
                Logger.Log("Onglet SW Multi Export cree : DocType=" + docType + ", CommandID=" + commandId);
            }

            Logger.Log("Interface ICommandManager initialisee avec succes");
        }

        private void SetupMenuFallback()
        {
            // swDocNONE ne couvre que l'etat "aucun document ouvert" : chaque type de
            // document a son propre jeu de menus, il faut donc l'ajouter a chacun pour
            // que l'entree reste visible qu'une piece, un assemblage ou un plan soit actif.
            int[] docTypes =
            {
                (int)swDocumentTypes_e.swDocNONE,
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocDRAWING
            };

            foreach (int docType in docTypes)
            {
                try
                {
                    // AddMenuItem5 ne cree pas de menu de haut niveau arbitraire : le
                    // premier segment doit etre un menu existant (ici "Tools"), les
                    // segments suivants forment les sous-menus.
                    swApp.AddMenuItem5(
                        docType,
                        addinCookie,
                        "Tools\tSW Multi Export\tExporter...",
                        -1,
                        "ShowHelloMessage",
                        "",
                        "Exporter la piece ou l'assemblage actif",
                        "");
                }
                catch (Exception ex)
                {
                    Logger.Log("AddMenuItem5 a echoue pour DocType=" + docType + " : " + ex.Message);
                }
            }

            Logger.Log("Interface initialisee via le repli AddMenuItem5 (tous types de documents)");
        }

        private void RemoveUserInterface()
        {
            try
            {
                if (cmdMgr != null)
                {
                    foreach (int docType in ExportDocumentTypes)
                    {
                        var tab = cmdMgr.GetCommandTab(docType, CommandTabTitle);
                        if (tab != null) cmdMgr.RemoveCommandTab(tab);
                    }
                    cmdMgr.RemoveCommandGroup(MainCmdGroupId);
                    cmdMgr = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Erreur lors du retrait du CommandGroup : " + ex.Message);
            }
        }

        #endregion

        public int EnableExport()
        {
            try
            {
                var doc = swApp?.ActiveDoc as IModelDoc2;
                return doc != null && (doc.GetType() == (int)swDocumentTypes_e.swDocPART ||
                    doc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) ? 1 : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Callback COM du bouton "Exporter...". Le nom est conserve pour
        /// les commandes deja enregistrees dans les preferences SolidWorks.
        /// </summary>
        public void ShowHelloMessage()
        {
            DependencyResolver.BeginExport();
            try
            {
                ExportActiveDocument();
            }
            catch (Exception ex)
            {
                Logger.Log("Erreur dans ShowHelloMessage : " + ex);
                swApp.SendMsgToUser2("Export impossible : " + ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
            }
            finally { DependencyResolver.EndExport(); }
        }

        private void ExportActiveDocument()
        {
            var document = swApp.ActiveDoc as IModelDoc2;
            if (document == null) throw new InvalidOperationException("Ouvrir une piece ou un assemblage avant l'export.");
            int type = document.GetType();
            if (type != (int)swDocumentTypes_e.swDocPART && type != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException("Seules les pieces et les assemblages peuvent etre exportes.");
            using (var dialog = new ExportDialog(document.GetPathName(), document.GetTitle()))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                var options = dialog.Options;
                var successes = new List<string>();
                var failures = new List<string>();
                var warnings = new List<string>();
                SimplificationReport simplification = null;
                Cursor previous = Cursor.Current;
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    var scene = new SwSceneExtractor().Extract(document);
                    simplification = MeshSimplification.Apply(scene, options);
                    warnings.AddRange(scene.Warnings);
                    Directory.CreateDirectory(options.OutputDirectory);
                    foreach (string format in options.Formats)
                    {
                        try
                        {
                            IExporter exporter = format == "fbx" ? (IExporter)new AssimpExporter() : new GltfExporter(format == "glb");
                            string path = Path.Combine(options.OutputDirectory, options.FileName + "." + exporter.Extension);
                            Logger.Log("Export " + format + " : " + path);
                            exporter.Export(scene, path, options);
                            successes.Add(path);
                            Logger.Log("Export reussi : " + path);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Echec export " + format + " : " + ex);
                            failures.Add(format.ToUpperInvariant() + " : " + ex.Message);
                        }
                    }
                }
                finally { Cursor.Current = previous; }
                string message = successes.Count > 0 ? "Fichiers exportes :\n" + string.Join("\n", successes) : "Aucun fichier exporte.";
                if (simplification != null)
                {
                    message = "Triangles : " + simplification.Before.ToString("N0") + " -> " + simplification.After.ToString("N0") +
                        " (reduction de " + simplification.Reduction.ToString("F1") + " %)\n" +
                        (options.Simplification == SimplificationMode.Adjustable ? "Cible : " + simplification.Target.ToString("N0") + " triangles\n" : "") + "\n" + message;
                }
                if (failures.Count > 0) message += "\n\nErreurs :\n" + string.Join("\n", failures);
                if (warnings.Count > 0)
                {
                    message += "\n\nAvertissements sur les apparences :\n" + string.Join("\n", warnings.GetRange(0, Math.Min(5, warnings.Count)));
                    if (warnings.Count > 5) message += "\n" + (warnings.Count - 5) + " autres avertissements dans le journal.";
                }
                MessageBox.Show(message, "SW Multi Export", MessageBoxButtons.OK,
                    failures.Count > 0 || warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }
    }
}
