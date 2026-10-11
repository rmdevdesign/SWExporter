using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;

namespace SwMultiExport.Util
{
    /// <summary>
    /// Icones PNG indexees, avec une vraie entree transparente et les six
    /// resolutions de l'API CommandManager SW 2016 et suivantes.
    /// </summary>
    internal static class IconGenerator
    {
        public static string[] GetIconPaths()
        {
            return Array.ConvertAll(new[] { 20, 32, 40, 64, 96, 128 },
                size => GetIconPath("export_logo_v3_" + size + ".png", size));
        }

        public static string GetSmallIconPath()
        {
            return GetIconPath("export_logo_v3_20.png", 20);
        }

        public static string GetLargeIconPath()
        {
            return GetIconPath("export_logo_v3_32.png", 32);
        }

        private static string GetIconPath(string fileName, int size)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwMultiExport", "icons");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                CreatePlaceholderIcon(path, size);
            }

            return path;
        }

        private static void CreatePlaceholderIcon(string path, int size)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.None;
                    g.ScaleTransform(size / 32f, size / 32f);
                    var top = new[] { new PointF(2, 10), new PointF(15, 3), new PointF(28, 10), new PointF(15, 17) };
                    var left = new[] { new PointF(2, 10), new PointF(15, 17), new PointF(15, 30), new PointF(2, 23) };
                    var right = new[] { new PointF(15, 17), new PointF(28, 10), new PointF(28, 23), new PointF(15, 30) };
                    using (var light = new SolidBrush(Color.FromArgb(231, 239, 255)))
                    using (var medium = new SolidBrush(Color.FromArgb(122, 167, 241)))
                    using (var dark = new SolidBrush(Color.FromArgb(31, 98, 218)))
                    using (var outline = new Pen(Color.FromArgb(26, 55, 105), 2))
                    {
                        g.FillPolygon(light, top); g.FillPolygon(medium, left); g.FillPolygon(dark, right);
                        g.DrawPolygon(outline, top); g.DrawPolygon(outline, left); g.DrawPolygon(outline, right);
                    }
                    // Fleche sortant du cube, avec un filet clair pour rester lisible.
                    using (var halo = new Pen(Color.White, 7))
                    using (var arrow = new Pen(Color.FromArgb(31, 98, 218), 3))
                    {
                        halo.StartCap = halo.EndCap = LineCap.Square;
                        g.DrawLine(halo, 18, 13, 28, 3);
                        g.DrawLine(halo, 21, 3, 28, 3); g.DrawLine(halo, 28, 3, 28, 10);
                        g.DrawLine(arrow, 18, 13, 28, 3);
                        g.DrawLine(arrow, 21, 3, 28, 3); g.DrawLine(arrow, 28, 3, 28, 10);
                    }
                }

                // Palette de 256 couleurs requise par les bandes CommandManager.
                // Index 0 = alpha nul : aucune cle rose dependante de l'hote.
                using (var indexed = new Bitmap(size, size, PixelFormat.Format8bppIndexed))
                {
                    var palette = indexed.Palette;
                    palette.Entries[0] = Color.FromArgb(0, 0, 0, 0);
                    for (int r = 0; r < 6; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 6; b++)
                        palette.Entries[1 + r * 36 + g * 6 + b] = Color.FromArgb(r * 51, g * 51, b * 51);
                    indexed.Palette = palette;
                    var data = indexed.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                    try
                    {
                        var bytes = new byte[data.Stride * size];
                        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                        {
                            var c = bmp.GetPixel(x, y);
                            bytes[y * data.Stride + x] = c.A == 0 ? (byte)0 :
                                (byte)(1 + (int)Math.Round(c.R / 51.0) * 36 + (int)Math.Round(c.G / 51.0) * 6 + (int)Math.Round(c.B / 51.0));
                        }
                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
                    }
                    finally { indexed.UnlockBits(data); }
                    // GDI+ omet tRNS lors de l'ecriture d'un PNG indexe.
                    // Ajouter explicitement l'alpha de la palette au fichier.
                    using (var encoded = new MemoryStream())
                    {
                        indexed.Save(encoded, ImageFormat.Png);
                        byte[] png = encoded.ToArray();
                        int offset = 8;
                        while (offset < png.Length)
                        {
                            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
                            if (png[offset + 4] == 'I' && png[offset + 5] == 'D' && png[offset + 6] == 'A' && png[offset + 7] == 'T') break;
                            offset += length + 12;
                        }
                        using (var output = File.Create(path))
                        {
                            output.Write(png, 0, offset);
                            var chunk = new byte[260];
                            chunk[0] = (byte)'t'; chunk[1] = (byte)'R'; chunk[2] = (byte)'N'; chunk[3] = (byte)'S';
                            for (int i = 5; i < chunk.Length; i++) chunk[i] = 255;
                            uint crc = 0xffffffff;
                            foreach (byte value in chunk)
                            {
                                crc ^= value;
                                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
                            }
                            crc ^= 0xffffffff;
                            output.Write(new byte[] { 0, 0, 1, 0 }, 0, 4);
                            output.Write(chunk, 0, chunk.Length);
                            output.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }, 0, 4);
                            output.Write(png, offset, png.Length - offset);
                        }
                    }
                }
            }
        }
    }
}
