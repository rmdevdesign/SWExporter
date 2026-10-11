using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SwMultiExport.Util
{
    internal static class DependencyResolver
    {
        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SharpGLTF.Core", "SharpGLTF.Runtime", "SharpGLTF.Toolkit", "AssimpNet", "MeshDecimator",
            "System.Memory", "System.Buffers", "System.Numerics.Vectors",
            "System.Runtime.CompilerServices.Unsafe", "System.Text.Json",
            "System.Text.Encodings.Web", "System.Threading.Tasks.Extensions",
            "System.IO.Pipelines", "Microsoft.Bcl.AsyncInterfaces"
        };
        private static readonly string DirectoryPath = Path.GetDirectoryName(typeof(DependencyResolver).Assembly.Location);
        [ThreadStatic] private static bool exporting;

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        public static void Unregister() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        public static void BeginExport() => exporting = true;
        public static void EndExport() => exporting = false;

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            // Une DLL COM ne peut pas imposer ses bindingRedirects au processus
            // SolidWorks. Ne traiter que les dependances de notre propre add-in.
            var requester = args.RequestingAssembly;
            // Le CLR peut omettre RequestingAssembly lors de la resolution d'une
            // facade .NET. Dans ce cas, limiter le repli au thread de notre export.
            if (requester == null && !exporting) return null;
            if (requester != null && requester != typeof(DependencyResolver).Assembly)
            {
                if (!Names.Contains(requester.GetName().Name) ||
                    !string.Equals(Path.GetDirectoryName(requester.Location), DirectoryPath, StringComparison.OrdinalIgnoreCase)) return null;
            }
            var requested = new AssemblyName(args.Name);
            if (!Names.Contains(requested.Name)) return null;
            string path = Path.Combine(DirectoryPath, requested.Name + ".dll");
            if (!File.Exists(path)) return null;
            try
            {
                var available = AssemblyName.GetAssemblyName(path);
                if (!(requested.GetPublicKeyToken() ?? new byte[0]).SequenceEqual(available.GetPublicKeyToken() ?? new byte[0])) return null;
                Logger.Log("Resolution dependance : " + args.Name + " -> " + available.FullName);
                return Assembly.LoadFrom(path);
            }
            catch (Exception ex)
            {
                Logger.Log("Echec resolution " + args.Name + " : " + ex.Message);
                return null;
            }
        }
    }
}
