using System;
using System.Globalization;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows;

/// <summary>
/// MX Player 级视听画质增强管理器：
/// 1. 人声增强与夜间防扰（动态范围压缩，压制刺耳特效，放大低音人声对白）
/// 2. 细节智能锐化（提升 720P/1080P 剧集清晰度）
/// 3. 鲜艳色彩与对比度优化（改善画面灰蒙蒙）
/// 4. 200% 极限音量超量增益（软解放大）
/// 5. 二次元动漫风格画质增强
/// </summary>
public static class MxEnhancementManager
{
    private static bool _nightModeActive = false;
    private static bool _sharpenActive = false;
    private static bool _vibrantActive = false;
    private static bool _volumeBoostActive = false;
    private static bool _animeModeActive = false;

    // 默认对比度与饱和度备份
    private static int _origContrast = 0;
    private static int _origSaturation = 0;

    /// <summary>
    /// 人声增强 / 夜间防扰模式切换
    /// </summary>
    public static void ToggleNightMode()
    {
        _nightModeActive = !_nightModeActive;
        if (_nightModeActive)
        {
            // dynaudnorm: 动态音频正规化滤镜，高保真压制爆炸声，将低沉对白提升
            Player.Command("af add @mx_night:lavfi=[dynaudnorm=f=75:g=25:p=0.55:m=5.0]");
            ShowOsd("🎧  夜间人声防扰: [已开启]\n(自动抑制爆炸刺耳声，突出人声对白)", 2000);
        }
        else
        {
            Player.Command("af remove @mx_night");
            ShowOsd("🎧  夜间人声防扰: [已关闭]", 1500);
        }
    }

    /// <summary>
    /// 画面智能锐化切换
    /// </summary>
    public static void ToggleSharpen()
    {
        _sharpenActive = !_sharpenActive;
        if (_sharpenActive)
        {
            // 使用 libmpv 内置高质量 unsharp/sharpen 滤镜
            Player.Command("vf add @mx_sharpen:lavfi=[unsharp=lx=5:ly=5:la=1.2:cx=3:cy=3:ca=0.8]");
            ShowOsd("✨  智能细节锐化: [已开启]\n(边缘轮廓增强，画面更清晰锐利)", 2000);
        }
        else
        {
            Player.Command("vf remove @mx_sharpen");
            ShowOsd("✨  智能细节锐化: [已关闭]", 1500);
        }
    }

    /// <summary>
    /// 鲜艳色彩与对比度增强切换
    /// </summary>
    public static void ToggleVibrantColor()
    {
        _vibrantActive = !_vibrantActive;
        if (_vibrantActive)
        {
            _origContrast = Player.GetPropertyInt("contrast");
            _origSaturation = Player.GetPropertyInt("saturation");

            Player.SetPropertyInt("contrast", Math.Clamp(_origContrast + 8, -100, 100));
            Player.SetPropertyInt("saturation", Math.Clamp(_origSaturation + 15, -100, 100));
            ShowOsd("🎨  鲜艳色彩增强: [已开启]\n(提升对比度与饱和度，告别发灰泛白)", 2000);
        }
        else
        {
            Player.SetPropertyInt("contrast", _origContrast);
            Player.SetPropertyInt("saturation", _origSaturation);
            ShowOsd("🎨  鲜艳色彩增强: [已关闭]", 1500);
        }
    }

    /// <summary>
    /// 200% 超量音量增益切换
    /// </summary>
    public static void ToggleVolumeBoost()
    {
        _volumeBoostActive = !_volumeBoostActive;
        if (_volumeBoostActive)
        {
            Player.SetPropertyDouble("volume-max", 200.0);
            double currentVol = Player.GetPropertyDouble("volume");
            if (currentVol < 120.0)
            {
                Player.SetPropertyDouble("volume", 130.0);
            }
            ShowOsd($"📢  200% 音量超量增益: [已开启]\n(上限解锁至 200%，当前: {(int)Math.Round(Player.GetPropertyDouble("volume"))}%)", 2000);
        }
        else
        {
            Player.SetPropertyDouble("volume-max", 100.0);
            double currentVol = Player.GetPropertyDouble("volume");
            if (currentVol > 100.0)
            {
                Player.SetPropertyDouble("volume", 100.0);
            }
            ShowOsd("📢  200% 音量超量增益: [已关闭] (上限恢复 100%)", 1500);
        }
    }

    /// <summary>
    /// 二次元动漫增强模式
    /// </summary>
    public static void ToggleAnimeMode()
    {
        _animeModeActive = !_animeModeActive;
        if (_animeModeActive)
        {
            // 针对动漫：微调色彩饱和度、轻度锐化线条
            Player.Command("vf add @mx_anime:lavfi=[hqdn3d=1.5:1.5:4:4]");
            Player.SetPropertyInt("saturation", Math.Clamp(Player.GetPropertyInt("saturation") + 12, -100, 100));
            ShowOsd("🌸  二次元动漫增强: [已开启]\n(线条平滑去噪，色彩纯净鲜活)", 2000);
        }
        else
        {
            Player.Command("vf remove @mx_anime");
            Player.SetPropertyInt("saturation", Math.Clamp(Player.GetPropertyInt("saturation") - 12, -100, 100));
            ShowOsd("🌸  二次元动漫增强: [已关闭]", 1500);
        }
    }

    /// <summary>
    /// 重置所有增强
    /// </summary>
    public static void ResetAll()
    {
        try { Player.Command("af remove @mx_night"); } catch {}
        try { Player.Command("vf remove @mx_sharpen"); } catch {}
        try { Player.Command("vf remove @mx_anime"); } catch {}

        Player.SetPropertyInt("contrast", 0);
        Player.SetPropertyInt("saturation", 0);
        Player.SetPropertyDouble("volume-max", 100.0);

        _nightModeActive = false;
        _sharpenActive = false;
        _vibrantActive = false;
        _volumeBoostActive = false;
        _animeModeActive = false;

        ShowOsd("🔄  已重置所有画质与音质增强", 2000);
    }

    private static void ShowOsd(string text, int durationMs = 1500)
    {
        try
        {
            Player.CommandV("show-text", text, durationMs.ToString(CultureInfo.InvariantCulture));
        }
        catch {}
    }
}
