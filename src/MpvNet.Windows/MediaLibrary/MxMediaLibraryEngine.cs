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

    // 系统与缓存黑名单目录（避免递归卡死或扫出垃圾）
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "$RECYCLE.BIN", "Windows", "Program Files",
        "Program Files (x86)", "ProgramData", "AppData", "Recovery", "Config.Msi", "PerfLogs",
        "node_modules", ".git", ".vs", "bin", "obj", "temp", "cache",
        "WeatherBgCache", "QQNT", "Tencent", "WeChat Files", "WXWork", "DingTalk",
        "Packages", "LocalState", "IsolatedStorage", "CrashDumps", "CrashReport",
        "Thumbnails", "Thumb", "Asset", "Assets", "Caches"
    };

    public static bool IsJunkMedia(string fullPath)
    {
        string p = fullPath.ToLowerInvariant();
        return p.Contains("weatherbgcache") || p.Contains("qqnt") || p.Contains("tencent")
            || p.Contains("wechat") || p.Contains("wxwork") || p.Contains("dingtalk")
            || p.Contains("packages") || p.Contains("localstate") || p.Contains("isolatedstorage")
            || p.Contains("crashdumps") || p.Contains("thumbnails") || p.Contains(".thumb")
            || p.Contains("\\assets\\") || p.Contains("\\asset\\") || p.Contains("\\temp\\")
            || p.Contains("\\tmp\\") || p.Contains("cloudy_") || p.Contains("sunny_") || p.Contains("overcast");
    }

    // 最小文件大小过滤（默认 2MB，过滤极短无意义的系统音效或小碎片）
    private const long MinVideoBytes = 3 * 1024 * 1024;
    private const long MinAudioBytes = 100 * 1024;

    public ObservableCollection<MediaItem> AllItems { get; } = [];
    public ObservableCollection<FolderItem> Folders { get; } = [];

    public static MxMediaLibraryEngine Instance { get; } = new();

    private string CacheFilePath => Path.Combine(Player.ConfigFolder, "media_library_cache.json");
    private string ThumbnailsDir => Path.Combine(Player.ConfigFolder, "thumbnails");

    public bool IsScanning { get; private set; }
    public event Action? ScanStarted;
    public event Action<int>? ScanProgress; // 已扫文件数
        public event Action? ScanCompleted;
    public event Action? ItemsChanged;

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
                    if (File.Exists(it.FilePath) && !IsJunkMedia(it.FilePath))
                        if (it.DurationSeconds > 0 && (string.IsNullOrEmpty(it.DurationString) || it.DurationString == "--:--"))
                        {
                            it.DurationString = FormatTime(it.DurationSeconds);
                        }
                        AllItems.Add(it);
                }
                RebuildFolders();
                if (items.Count != AllItems.Count) SaveCache();
            }
        }
        catch {}
    }

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

    public void RemoveItem(string filePath)
    {
        Action act = () =>
        {
            var target = AllItems.FirstOrDefault(it => string.Equals(it.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                AllItems.Remove(target);
                RebuildFolders();
                SaveCache();
                ItemsChanged?.Invoke();
            }
        };

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            System.Windows.Application.Current.Dispatcher.Invoke(act);
        else
            act();
    }
    /// <summary>
    /// 扫描全盘或指定根目录
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

                            var rootOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4) };
            Parallel.ForEach(roots, rootOptions, root =>
                {
                    if (!Directory.Exists(root)) return;
                    ScanDirectoryRecursive(root, foundFiles);
                });

                int total = foundFiles.Count;
                int current = 0;
                var existingMap = AllItems.ToDictionary(it => it.FilePath, StringComparer.OrdinalIgnoreCase);
                var newItems = new ConcurrentBag<MediaItem>();

                                var parseOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4) };
                Parallel.ForEach(foundFiles, parseOptions, file =>
                {
                    try
                    {
                        if (existingMap.TryGetValue(file, out var existing))
                        {
                        if (existing.Type == MediaType.Video && (existing.DurationSeconds <= 0 || string.IsNullOrEmpty(existing.DurationString) || existing.DurationString == "--:--"))
                        {
                            var (dur, res) = WindowsThumbnailProvider.GetMediaDetails(file);
                            if (dur > 0)
                            {
                                existing.DurationSeconds = dur;
                                existing.DurationString = FormatTime(dur);
                            }
                            if (!string.IsNullOrEmpty(res))
                            {
                                existing.Resolution = res;
                            }
                        }
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
                    if (c % 15 == 0 || c == total)
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

    /// <summary>
    /// 手动添加单个文件夹或文件（支持拖拽）
    /// </summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (paths == null) return;
        var foundFiles = new List<string>();

        await Task.Run(() =>
        {
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    if (VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext))
                        foundFiles.Add(p);
                }
                else if (Directory.Exists(p))
                {
                    var bag = new ConcurrentBag<string>();
                    ScanDirectoryRecursive(p, bag);
                    foundFiles.AddRange(bag);
                }
            }

            var existingMap = AllItems.ToDictionary(it => it.FilePath, StringComparer.OrdinalIgnoreCase);
            var toAdd = new List<MediaItem>();

            foreach (var file in foundFiles)
            {
                if (!existingMap.ContainsKey(file))
                {
                    var item = ParseMediaFile(file);
                    if (item != null)
                        toAdd.Add(item);
                }
            }

            if (toAdd.Count > 0)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var it in toAdd)
                        AllItems.Insert(0, it);

                    RebuildFolders();
                });
                SaveCache();
            }
        });
    }

    private void ScanDirectoryRecursive(string dir, ConcurrentBag<string> bag)
    {
        DirectoryInfo di;
        try
        {
            di = new DirectoryInfo(dir);
            if (!di.Exists) return;
        }
        catch { return; }

        // 仅当不是驱动器根目录时才检查跳过名称和隐藏属性
        bool isRoot = string.IsNullOrEmpty(di.Parent?.FullName);
        if (!isRoot)
        {
            if (SkipDirNames.Contains(di.Name)) return;
            try
            {
                if ((di.Attributes & FileAttributes.Hidden) != 0) return;
            }
            catch {}
        }

        // 1. 安全枚举文件
        try
        {
            foreach (var fi in di.EnumerateFiles())
            {
                try
                {
                    string ext = fi.Extension.ToLowerInvariant();
                    if (VideoExtensions.Contains(ext))
                    {
                        if (fi.Length >= MinVideoBytes)
                            bag.Add(fi.FullName);
                    }
                    else if (AudioExtensions.Contains(ext))
                    {
                        if (fi.Length >= MinAudioBytes)
                            bag.Add(fi.FullName);
                    }
                }
                catch {}
            }
        }
        catch {}

        // 2. 独立安全递归子目录
        try
        {
            foreach (var subDir in di.EnumerateDirectories())
            {
                try
                {
                    if (!SkipDirNames.Contains(subDir.Name))
                    {
                        if ((subDir.Attributes & FileAttributes.Hidden) == 0)
                        {
                            ScanDirectoryRecursive(subDir.FullName, bag);
                        }
                    }
                }
                catch {}
            }
        }
        catch {}
    }

    private List<string> GetDefaultSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 系统媒体文件夹
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        // 本地数据盘根目录（L:\, J:\, D:\ 等，跳过 C 盘系统盘深度扫描，防止卡死）
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
            {
                string root = drive.RootDirectory.FullName;
                if (!string.Equals(root, "C:\\", StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(root);
                }
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

            if (isVideo)
            {
                if (IsJunkMedia(fi.FullName))
                    return null;

                var (dur, res) = WindowsThumbnailProvider.GetMediaDetails(fi.FullName);
                item.DurationSeconds = dur;
                if (dur > 0)
                {
                    item.DurationString = FormatTime(dur);
                }
                item.Resolution = res;

                // 过滤极短且体积较小的非视频动画/小表情
                if (dur > 0 && dur < 6.0 && fi.Length < 25 * 1024 * 1024)
                {
                    return null;
                }

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

    public void RebuildFolders()
    {
        var groups = AllItems.GroupBy(it => it.DirectoryPath, StringComparer.OrdinalIgnoreCase).ToList();
        var folderList = new List<FolderItem>();

        foreach (var g in groups)
        {
            if (string.IsNullOrEmpty(g.Key)) continue;

            string dirName = Path.GetFileName(g.Key.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(dirName))
                dirName = g.Key;

            var firstVideo = g.FirstOrDefault(it => it.Type == MediaType.Video && !string.IsNullOrEmpty(it.ThumbnailPath));

            folderList.Add(new FolderItem
            {
                FolderPath = g.Key,
                FolderName = dirName,
                VideoCount = g.Count(it => it.Type == MediaType.Video),
                AudioCount = g.Count(it => it.Type == MediaType.Audio),
                CoverThumbnailPath = firstVideo?.ThumbnailPath ?? "",
                Items = g.ToList()
            });
        }

        var sorted = folderList.OrderBy(f => f.FolderName).ToList();

        Action update = () =>
        {
            Folders.Clear();
            foreach (var f in sorted)
                Folders.Add(f);
        };

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            System.Windows.Application.Current.Dispatcher.Invoke(update);
        else
            update();
    }

    private DateTime _lastProgressSave = DateTime.MinValue;

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

            // 节流写入，每 3 秒最多落盘一次，保护磁盘健康
            if ((DateTime.Now - _lastProgressSave).TotalSeconds > 3.0)
            {
                _lastProgressSave = DateTime.Now;
                SaveCache();
            }
        }
    }
    public long GetTotalSizeBytes() => AllItems.Sum(it => it.FileSizeBytes);

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
        if (bytes >= 1024L * 1024 * 1024)
            return $"{(bytes / (1024.0 * 1024 * 1024)):F1} GB";
        if (bytes >= 1024L * 1024)
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
/// Windows 原生 IShellItemImageFactory COM 缩略图极速提取
/// </summary>
internal static class WindowsThumbnailProvider
{
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
        IShellItemImageFactory? factory = null;
        try
        {
            Guid uuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, uuid, out factory);
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
            if (factory != null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
        return null;
    }

    [ComImport]
    [Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName();
        void GetAttributes();
        void Compare();
        [PreserveSig]
        int GetPropertyStore(int flags, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, [Out] out IntPtr ppv);
        void GetPropertyDescriptionList();
        void Update();
        void GetProperty();
        void GetCLSID();
        void GetFileTime();
        void GetInt32();
        void GetString();
        [PreserveSig]
        int GetUInt64([In] ref PropertyKey key, out ulong pull);
        [PreserveSig]
        int GetUInt32([In] ref PropertyKey key, out uint pull);
        void GetBool();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid f, uint p) { fmtid = f; pid = p; }
    }

    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName2(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem2 ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        int flags,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppv);

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PropertyKey pkey);
        [PreserveSig] int GetValue(ref PropertyKey key, [In, Out] PropVariant pv);
    }

    [StructLayout(LayoutKind.Explicit)]
    private class PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public ulong uhVal;
        [FieldOffset(8)] public uint ulVal;
    }

    public static (double DurationSec, string Resolution) GetMediaDetails(string filePath)
    {
        IPropertyStore? store = null;
        try
        {
            Guid riid = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
            int hr = SHGetPropertyStoreFromParsingName(filePath, IntPtr.Zero, 0, riid, out store);
            if (hr == 0 && store != null)
            {
                // PKEY_Media_Duration = {64440490-4c8b-11d1-8b70-080036b11a03}, 3
                PropertyKey kDur = new PropertyKey(new Guid("64440490-4c8b-11d1-8b70-080036b11a03"), 3);
                PropVariant pvDur = new PropVariant();
                store.GetValue(ref kDur, pvDur);
                double duration = (pvDur.uhVal > 0) ? ((double)pvDur.uhVal / 10000000.0) : 0;

                // PKEY_Video_FrameWidth / Height
                PropertyKey kWidth = new PropertyKey(new Guid("64440491-4c8b-11d1-8b70-080036b11a03"), 3);
                PropertyKey kHeight = new PropertyKey(new Guid("64440491-4c8b-11d1-8b70-080036b11a03"), 4);

                PropVariant pvW = new PropVariant();
                PropVariant pvH = new PropVariant();
                store.GetValue(ref kWidth, pvW);
                store.GetValue(ref kHeight, pvH);

                uint width = pvW.ulVal;
                uint height = pvH.ulVal;

                string res = "";
                if (width >= 3800 || height >= 2100) res = "4K";
                else if (width >= 1900 || height >= 1000) res = "1080P";
                else if (width >= 1200 || height >= 700) res = "720P";
                else if (width > 0 && height > 0) res = $"{width}x{height}";

                return (duration, res);
            }
        }
        catch {}
        finally
        {
            if (store != null && Marshal.IsComObject(store))
                Marshal.ReleaseComObject(store);
        }
        return (0, "");
    }
}
