using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.MediaLibrary;

public enum MediaType
{
    Video,
    Audio
}

public class MediaItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Extension { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public string DirectoryName { get; set; } = "";
    public MediaType Type { get; set; } = MediaType.Video;
    public double DurationSeconds { get; set; }
    public string DurationString { get; set; } = "--:--";
    public string Resolution { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public double ProgressRatio { get; set; } // 0.0 ~ 1.0 (续播进度条)
    public double LastPosition { get; set; }
    public long FileSizeBytes { get; set; }
    public string FileSizeString { get; set; } = "";
    public DateTime LastModified { get; set; }
}

public class FolderItem
{
    public string FolderPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public int VideoCount { get; set; }
    public int AudioCount { get; set; }
    public string CoverThumbnailPath { get; set; } = "";
    public List<MediaItem> Items { get; set; } = [];
}

public class MxMediaLibraryEngine
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".rmvb", ".mpg", ".mpeg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".aac", ".wma", ".mka"
    };

    public ObservableCollection<MediaItem> AllItems { get; } = [];
    public ObservableCollection<FolderItem> Folders { get; } = [];

    public static MxMediaLibraryEngine Instance { get; } = new();

    private string CacheFilePath => Path.Combine(Player.ConfigFolder, "media_library_cache.json");
    private string ThumbnailsDir => Path.Combine(Player.ConfigFolder, "thumbnails");

    public bool IsScanning { get; private set; }
    public event Action? ScanStarted;
    public event Action<int>? ScanProgress; // 已扫文件数
    public event Action? ScanCompleted;

    private MxMediaLibraryEngine()
    {
        try
        {
            if (!Directory.Exists(ThumbnailsDir))
                Directory.CreateDirectory(ThumbnailsDir);
        }
        catch {}

        LoadCache();
    }

    /// <summary>
    /// 读取本地持久化缓存
    /// </summary>
    public void LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return;

            string json = File.ReadAllText(CacheFilePath, Encoding.UTF8);
            var items = JsonSerializer.Deserialize<List<MediaItem>>(json);
            if (items != null && items.Count > 0)
            {
                AllItems.Clear();
                foreach (var it in items)
                {
                    if (File.Exists(it.FilePath))
                        AllItems.Add(it);
                }
                RebuildFolders();
            }
        }
        catch {}
    }

    /// <summary>
    /// 保存持久化缓存
    /// </summary>
    public void SaveCache()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(AllItems.ToList(), options);
            File.WriteAllText(CacheFilePath, json, Encoding.UTF8);
        }
        catch {}
    }

    /// <summary>
    /// 触发异步全量或增量扫描
    /// </summary>
    public async Task ScanAsync(IEnumerable<string>? customRoots = null)
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanStarted?.Invoke();

        await Task.Run(() =>
        {
            try
            {
                var roots = (customRoots != null && customRoots.Any())
                    ? customRoots.ToList()
                    : GetDefaultSearchRoots();

                var foundFiles = new ConcurrentBag<string>();

                Parallel.ForEach(roots, root =>
                {
                    if (!Directory.Exists(root)) return;
                    try
                    {
                        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)) ||
                                        AudioExtensions.Contains(Path.GetExtension(f)));

                        foreach (var f in files)
                            foundFiles.Add(f);
                    }
                    catch {}
                });

                int total = foundFiles.Count;
                int current = 0;
                var existingMap = AllItems.ToDictionary(it => it.FilePath, StringComparer.OrdinalIgnoreCase);
                var newItems = new ConcurrentBag<MediaItem>();

                Parallel.ForEach(foundFiles, file =>
                {
                    try
                    {
                        if (existingMap.TryGetValue(file, out var existing))
                        {
                            newItems.Add(existing);
                        }
                        else
                        {
                            var item = ParseMediaFile(file);
                            if (item != null)
                                newItems.Add(item);
                        }
                    }
                    catch {}

                    int c = System.Threading.Interlocked.Increment(ref current);
                    if (c % 10 == 0 || c == total)
                        ScanProgress?.Invoke(c);
                });

                var sorted = newItems.OrderByDescending(it => it.LastModified).ToList();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    AllItems.Clear();
                    foreach (var it in sorted)
                        AllItems.Add(it);

                    RebuildFolders();
                });

                SaveCache();
            }
            finally
            {
                IsScanning = false;
                ScanCompleted?.Invoke();
            }
        });
    }

    private List<string> GetDefaultSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 系统默认媒体库目录
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        // 默认扫描主要数据盘（L:\, J:\, D:\）
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
            {
                string root = drive.RootDirectory.FullName;
                roots.Add(root);
            }
        }

        return roots.Where(Directory.Exists).ToList();
    }

    private MediaItem? ParseMediaFile(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists) return null;

            string ext = fi.Extension;
            bool isVideo = VideoExtensions.Contains(ext);

            var item = new MediaItem
            {
                FilePath = fi.FullName,
                FileName = Path.GetFileNameWithoutExtension(fi.Name),
                Extension = ext.ToLowerInvariant(),
                DirectoryPath = fi.DirectoryName ?? "",
                DirectoryName = fi.Directory?.Name ?? "",
                Type = isVideo ? MediaType.Video : MediaType.Audio,
                FileSizeBytes = fi.Length,
                FileSizeString = FormatFileSize(fi.Length),
                LastModified = fi.LastWriteTime
            };

            // 提取或生成视频缩略图
            if (isVideo)
            {
                string thumbPath = GetOrCreateThumbnail(fi.FullName);
                item.ThumbnailPath = thumbPath;
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    private void RebuildFolders()
    {
        Folders.Clear();
        var groups = AllItems.GroupBy(it => it.DirectoryPath, StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            var firstVideo = g.FirstOrDefault(it => it.Type == MediaType.Video && !string.IsNullOrEmpty(it.ThumbnailPath));
            var folder = new FolderItem
            {
                FolderPath = g.Key,
                FolderName = Path.GetFileName(g.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                VideoCount = g.Count(it => it.Type == MediaType.Video),
                AudioCount = g.Count(it => it.Type == MediaType.Audio),
                CoverThumbnailPath = firstVideo?.ThumbnailPath ?? "",
                Items = g.ToList()
            };
            if (string.IsNullOrEmpty(folder.FolderName))
                folder.FolderName = g.Key; // 盘符根目录

            Folders.Add(folder);
        }
    }

    /// <summary>
    /// 更新播放进度（续播记忆）
    /// </summary>
    public void UpdatePlaybackProgress(string filePath, double position, double duration)
    {
        if (duration <= 0) return;
        var item = AllItems.FirstOrDefault(it => string.Equals(it.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.LastPosition = position;
            item.DurationSeconds = duration;
            item.ProgressRatio = Math.Clamp(position / duration, 0.0, 1.0);
            item.DurationString = FormatTime(duration);
            SaveCache();
        }
    }

    /// <summary>
    /// Windows 原生 Shell COM 快速抽取视频缩略图
    /// </summary>
    private string GetOrCreateThumbnail(string videoPath)
    {
        try
        {
            string hash = ComputeMd5(videoPath);
            string cachePath = Path.Combine(ThumbnailsDir, $"{hash}.jpg");

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
                return cachePath;

            using var bmp = WindowsThumbnailProvider.GetThumbnail(videoPath, 320, 180);
            if (bmp != null)
            {
                bmp.Save(cachePath, ImageFormat.Jpeg);
                return cachePath;
            }
        }
        catch {}

        return "";
    }

    private static string ComputeMd5(string input)
    {
        using var md5 = MD5.Create();
        byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024 * 1024)):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024)):F1} MB";
        return $"{(bytes / 1024.0):F0} KB";
    }

    public static string FormatTime(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1)
            return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
        return string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
    }
}

/// <summary>
/// Windows 原生 IShellItemImageFactory COM 缩略图极速提供器
/// </summary>
internal static class WindowsThumbnailProvider
{
    private const string IShellItem2Guid = "7E9EA090-3257-4761-9E77-05C0207BBEC7";

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] NativeSize size,
            [In] SIIGBF flags,
            [Out] out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
        public NativeSize(int width, int height) { Width = width; Height = height; }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_SCALEUP = 0x08
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static Bitmap? GetThumbnail(string filePath, int width, int height)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            Guid uuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, uuid, out var factory);
            if (hr != 0 || factory == null) return null;

            var size = new NativeSize(width, height);
            hr = factory.GetImage(size, SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_SCALEUP, out hBitmap);
            if (hr == 0 && hBitmap != IntPtr.Zero)
            {
                using var tempBmp = Image.FromHbitmap(hBitmap);
                return new Bitmap(tempBmp);
            }
        }
        catch {}
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
        }
        return null;
    }
}
