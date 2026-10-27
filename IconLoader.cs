using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SebLauncher
{
    internal static class IconLoader
    {
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" };

        public static string Expand(string s) =>
            string.IsNullOrWhiteSpace(s) ? s : Environment.ExpandEnvironmentVariables(s.Trim().Trim('"'));

        public static void ClearCache() => Cache.Clear();

        public static ImageSource Get(string iconPath, string targetPath, int px)
        {
            var src = !string.IsNullOrWhiteSpace(iconPath) ? Expand(iconPath) : Expand(targetPath);
            if (string.IsNullOrWhiteSpace(src)) return null;
            var key = src + "|" + px;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            ImageSource img = null;
            try
            {
                bool isFile = File.Exists(src);
                if (isFile && Array.IndexOf(ImageExt, Path.GetExtension(src).ToLowerInvariant()) >= 0 &&
                    !src.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    img = LoadImageFile(src, px);
                else if (isFile || Directory.Exists(src) || src.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    img = FromShell(src, px) ?? FromAssociated(src);
            }
            catch { img = null; }

            // ikona z pliku nie wyszła -> spróbuj z celu
            if (img == null && !string.IsNullOrWhiteSpace(iconPath) && !string.IsNullOrWhiteSpace(targetPath))
            {
                var t = Expand(targetPath);
                if (!string.Equals(t, src, StringComparison.OrdinalIgnoreCase)) img = Get(null, t, px);
            }

            Cache[key] = img;
            return img;
        }

        static ImageSource LoadImageFile(string path, int px)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = px;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        static ImageSource FromShell(string path, int px)
        {
            IShellItemImageFactory factory = null;
            try
            {
                NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
                var size = new NativeMethods.SIZE { cx = px, cy = px };
                int hr = factory.GetImage(size, NativeMethods.SIIGBF_BIGGERSIZEOK | NativeMethods.SIIGBF_ICONONLY, out IntPtr hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                return HBitmapToSource(hbm);
            }
            catch { return null; }
            finally
            {
                if (factory != null) Marshal.ReleaseComObject(factory);
            }
        }

        static ImageSource HBitmapToSource(IntPtr hbm)
        {
            try
            {
                var ds = new NativeMethods.DIBSECTION();
                int r = NativeMethods.GetObject(hbm, Marshal.SizeOf<NativeMethods.DIBSECTION>(), ref ds);
                if (r > 0 && ds.dsBm.bmBits != IntPtr.Zero && ds.dsBm.bmBitsPixel == 32)
                {
                    int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
                    var buf = new byte[stride * h];
                    Marshal.Copy(ds.dsBm.bmBits, buf, 0, buf.Length);
                    if (ds.dsBmih.biHeight > 0) // bottom-up -> odwróć wiersze
                    {
                        var row = new byte[stride];
                        for (int y = 0; y < h / 2; y++)
                        {
                            int a = y * stride, b = (h - 1 - y) * stride;
                            Buffer.BlockCopy(buf, a, row, 0, stride);
                            Buffer.BlockCopy(buf, b, buf, a, stride);
                            Buffer.BlockCopy(row, 0, buf, b, stride);
                        }
                    }
                    var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, buf, stride);
                    bs.Freeze();
                    return bs;
                }
                var b2 = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                b2.Freeze();
                return b2;
            }
            finally
            {
                NativeMethods.DeleteObject(hbm);
            }
        }

        static ImageSource FromAssociated(string path)
        {
            if (!File.Exists(path)) return null;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico == null) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
    }
}
