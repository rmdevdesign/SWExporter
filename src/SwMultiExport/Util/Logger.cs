using System;
using System.IO;

namespace SwMultiExport.Util
{
    /// <summary>
    /// Logger texte minimal, ecrit dans %LOCALAPPDATA%\SwMultiExport\logs (ou, en
    /// repli, dans le dossier temporaire). Ne doit jamais lever d'exception : un
    /// probleme de log ne doit pas faire planter l'add-in.
    ///
    /// IMPORTANT : tout calcul de chemin se fait a l'interieur du try, jamais dans
    /// un champ static readonly. Si un tel champ levait une exception lors de son
    /// initialisation paresseuse, le .cctor echouerait et TOUT appel futur a
    /// Logger.Log lancerait une TypeInitializationException non liee au try/catch
    /// de cette methode (meme piege que le cast fait hors du try dans ConnectToSW).
    /// </summary>
    internal static class Logger
    {
        private static readonly object SyncRoot = new object();

        public static void Log(string message)
        {
            string line = string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}",
                DateTime.Now,
                message);

            lock (SyncRoot)
            {
                if (TryWrite(GetPrimaryLogPath, line))
                {
                    return;
                }

                if (TryWrite(GetFallbackLogPath, line))
                {
                    return;
                }

                TryWrite(GetHardcodedLogPath, line);
            }
        }

        private static bool TryWrite(Func<string> getPath, string line)
        {
            try
            {
                string path = getPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + Environment.NewLine);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetPrimaryLogPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwMultiExport", "logs");
            return Path.Combine(dir, "SwMultiExport_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        private static string GetFallbackLogPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "SwMultiExport");
            return Path.Combine(dir, "SwMultiExport_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
        }

        // Chemin absolu en dur, sur le meme disque que le projet : elimine toute
        // ambiguite de resolution de dossier special (LOCALAPPDATA/TEMP) pendant
        // le diagnostic.
        private static string GetHardcodedLogPath()
        {
            return @"D:\Git\SWExporter\addin_debug.log";
        }
    }
}
