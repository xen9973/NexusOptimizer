
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexusOptimizer.Services;

public static class GameIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    private static readonly ConcurrentDictionary<string, byte> MissingIcons = new();

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    public static ImageSource? GetIcon(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return null;

        string key = installPath.Trim();

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        if (MissingIcons.ContainsKey(key))
            return null;

        var icon = LoadIcon(key);

        if (icon is not null)
            Cache.TryAdd(key, icon);
        else
            MissingIcons.TryAdd(key, 0);

        return icon;
    }

    private static ImageSource? LoadIcon(string installPath)
    {
        try
        {
            if (!Directory.Exists(installPath))
                return null;

            var executables = Directory
                .EnumerateFiles(
                    installPath,
                    "*.exe",
                    SearchOption.TopDirectoryOnly)
                .ToArray();

            if (executables.Length == 0)
                return null;

            string folderName = Path.GetFileName(
                installPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            string? exe = executables
                .OrderByDescending(path =>
                    string.Equals(
                        Path.GetFileNameWithoutExtension(path),
                        folderName,
                        StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path =>
                {
                    try { return new FileInfo(path).Length; }
                    catch { return 0L; }
                })
                .FirstOrDefault();

            if (exe is null)
                return null;

            var info = new SHFILEINFO();

            IntPtr result = SHGetFileInfo(
                exe,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(64, 64));

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    public static void ClearCache()
    {
        Cache.Clear();
        MissingIcons.Clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
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
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
