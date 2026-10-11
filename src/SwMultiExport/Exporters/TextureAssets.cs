using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;

namespace SwMultiExport.Exporters
{
    internal static class TextureAssets
    {
        public static byte[] ReadPng(string source)
        {
            if (Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(source);
            using (var image = Image.FromFile(source))
            using (var stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public static string Name(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return "texture_" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static string CopyForFbx(string source, string exportPath)
        {
            byte[] bytes = ReadPng(source);
            string relative = Path.Combine(Path.GetFileNameWithoutExtension(exportPath) + "_textures", Name(bytes) + ".png");
            string destination = Path.Combine(Path.GetDirectoryName(exportPath), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, bytes);
            return relative.Replace('\\', '/');
        }
    }
}
