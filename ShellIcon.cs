using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.IsoViewer
{
    /// <summary>
    /// System shell icons by extension / folder (no real file required).
    /// </summary>
    public static class ShellIcon
    {
        const uint SHGFI_ICON = 0x000000100;
        const uint SHGFI_SMALLICON = 0x000000001;
        const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource ForFolder()
        {
            return Get("folder", true);
        }

        public static ImageSource ForFile(string name)
        {
            string ext = System.IO.Path.GetExtension(name ?? "");
            if (string.IsNullOrEmpty(ext)) ext = ".file";
            return Get(ext, false);
        }

        static ImageSource Get(string key, bool isDir)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached))
                    return cached;
            }

            ImageSource src = null;
            try
            {
                var shinfo = new SHFILEINFO();
                uint attrs = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
                // Fake path — USEFILEATTRIBUTES looks up by extension only
                string fake = isDir ? "folder" : "file" + key;

                IntPtr h = SHGetFileInfo(fake, attrs, ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

                if (h != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        src = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            if (src == null)
            {
                // fallback empty 16x16
                src = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 16 * 4], 16 * 4);
                src.Freeze();
            }

            lock (Cache)
            {
                Cache[key] = src;
            }
            return src;
        }
    }
}
