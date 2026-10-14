using System;
using System.Collections.Generic;
using System.Globalization;

namespace MpvNet.Windows;

public enum MxLanguage
{
    Zh, // 简体中文
    En  // English
}

/// <summary>
/// MX Player 风格中英双语本地化中心
/// 自动匹配系统语言/mpv配置，并支持界面即时一键切换
/// </summary>
public static class MxI18n
{
    public static MxLanguage CurrentLanguage { get; private set; } = MxLanguage.Zh;
    public static event Action? LanguageChanged;

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // 顶部导航与分类
        ["AppTitle"] = ("MX 本地媒体库", "MX Media Library"),
        ["BrandTitle"] = (" 媒体库", " Library"),
        ["TabAllVideos"] = ("🎬 全部视频", "🎬 All Videos"),
        ["TabFolders"] = ("📁 文件夹", "📁 Folders"),
        ["TabMusic"] = ("🎵 音乐与音频", "🎵 Music & Audio"),
        ["SearchPlaceholder"] = ("搜索媒体、影视剧集或目录...", "Search media, movies, or folders..."),
        ["ScanDisk"] = ("🔄 扫描磁盘", "🔄 Scan Drives"),
        ["AddFolder"] = ("➕ 添加目录", "➕ Add Folder"),
        ["LanguageToggle"] = ("EN / 中", "中 / EN"),

        // 排序与视图
        ["SortBy"] = ("排序:", "Sort:"),
        ["SortDate"] = ("最新修改", "Date Added"),
        ["SortName"] = ("名称 (A-Z)", "Name (A-Z)"),
        ["SortSize"] = ("文件大小", "File Size"),
        ["SortDuration"] = ("播放时长", "Duration"),
        ["ViewGrid"] = ("卡片网格", "Grid View"),
        ["ViewList"] = ("详细列表", "List View"),

        // 面包屑与文件夹导航
        ["AllFolders"] = ("全部文件夹", "All Folders"),
        ["BackToFolders"] = ("⬅ 返回文件夹列表", "⬅ Back to Folders"),
        ["VideoCountFormat"] = ("{0} 个视频", "{0} videos"),
        ["AudioCountFormat"] = ("{0} 首音频", "{0} audio tracks"),

        // 状态栏与空提示
        ["ReadyStatus"] = ("媒体库就绪：共 {0} 部视频，{1} 首音乐，总计 {2}", "Library ready: {0} videos, {1} tracks, {2} total"),
        ["ScanningStatus"] = ("正在全速扫描磁盘媒体文件...", "Scanning drives for media files..."),
        ["ScanningProgress"] = ("已发现 {0} 个媒体文件...", "Discovered {0} media files..."),
        ["EmptyTitle"] = ("未找到匹配的媒体文件", "No media files found"),
        ["EmptySub"] = ("点击右上角「扫描磁盘」或直接将视频文件夹拖入此窗口", "Click 'Scan Drives' or drag & drop folders here"),

        // 右键菜单
        ["MenuPlay"] = ("▶ 立即播放", "▶ Play Now"),
        ["MenuOpenFolder"] = ("📂 在文件管理器中显示", "📂 Show in File Explorer"),
        ["MenuCopyPath"] = ("📋 复制完整路径", "📋 Copy Full Path"),
        ["MenuRemove"] = ("🗑 从媒体库移除", "🗑 Remove from Library"),

        // OSD 提示文本
        ["OsdBrightness"] = ("☀  亮度: {0}%", "☀  Brightness: {0}%"),
        ["OsdVolume"] = ("🔊  音量: {0}%", "🔊  Volume: {0}%"),
        ["OsdPaused"] = ("⏸  已暂停", "⏸  Paused"),
        ["OsdPlaying"] = ("▶  播放中", "▶  Playing"),
        ["OsdNightModeOn"] = ("🎧  夜间人声防扰: [已开启]\n(自动抑制爆炸刺耳声，突出人声对白)", "🎧  Night Mode (Vocal Boost): [ON]\n(Compresses harsh dynamics & amplifies dialogue)"),
        ["OsdNightModeOff"] = ("🎧  夜间人声防扰: [已关闭]", "🎧  Night Mode: [OFF]"),
        ["OsdSharpenOn"] = ("✨  智能细节锐化: [已开启]\n(边缘轮廓增强，画面更清晰锐利)", "✨  Adaptive Sharpening: [ON]\n(Enhanced edge clarity & crisp details)"),
        ["OsdSharpenOff"] = ("✨  智能细节锐化: [已关闭]", "✨  Adaptive Sharpening: [OFF]"),
        ["OsdVibrantOn"] = ("🎨  鲜艳色彩增强: [已开启]\n(提升对比度与饱和度，告别发灰泛白)", "🎨  Vibrant Color Boost: [ON]\n(Optimized contrast & saturation)"),
        ["OsdVibrantOff"] = ("🎨  鲜艳色彩增强: [已关闭]", "🎨  Vibrant Color Boost: [OFF]"),
        ["OsdVolBoostOn"] = ("📢  200% 音量超量增益: [已开启]\n(上限解锁至 200%，当前: {0}%)", "📢  200% Super Volume Boost: [ON]\n(Limit unlocked to 200%, current: {0}%)"),
        ["OsdVolBoostOff"] = ("📢  200% 音量超量增益: [已关闭] (上限恢复 100%)", "📢  200% Volume Boost: [OFF] (Restored to 100%)"),
        ["OsdAnimeOn"] = ("🌸  二次元动漫增强: [已开启]\n(线条平滑去噪，色彩纯净鲜活)", "🌸  Anime Enhancement: [ON]\n(Clean contours & pure vivid palette)"),
        ["OsdAnimeOff"] = ("🌸  二次元动漫增强: [已关闭]", "🌸  Anime Enhancement: [OFF]"),
        ["OsdResetAll"] = ("🔄  已重置所有画质与音质增强", "🔄  All enhancements have been reset"),
        ["OsdSubScale"] = ("🔤  字幕字号: {0}%", "🔤  Subtitle Font Size: {0}%"),
        ["OsdSubPos"] = ("↕  字幕垂直位置: {0}%", "↕  Subtitle Vertical Position: {0}%"),
        ["OsdSubDelay"] = ("⏱  字幕时间轴校准: {0}{1:F1}s", "⏱  Subtitle Sync Calibration: {0}{1:F1}s")
    };

    static MxI18n()
    {
        // 默认根据系统 UI 语言初始化
        try
        {
            string twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            CurrentLanguage = twoLetter == "zh" ? MxLanguage.Zh : MxLanguage.En;
        }
        catch
        {
            CurrentLanguage = MxLanguage.Zh;
        }
    }

    public static string T(string key, params object[] args)
    {
        if (Strings.TryGetValue(key, out var pair))
        {
            string template = CurrentLanguage == MxLanguage.Zh ? pair.Zh : pair.En;
            if (args != null && args.Length > 0)
            {
                try { return string.Format(template, args); } catch { return template; }
            }
            return template;
        }
        return key;
    }

    public static void ToggleLanguage()
    {
        CurrentLanguage = CurrentLanguage == MxLanguage.Zh ? MxLanguage.En : MxLanguage.Zh;
        LanguageChanged?.Invoke();
    }

    public static void SetLanguage(MxLanguage lang)
    {
        if (CurrentLanguage != lang)
        {
            CurrentLanguage = lang;
            LanguageChanged?.Invoke();
        }
    }
}
