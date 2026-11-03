using System;
using System.Globalization;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows;

/// <summary>
/// MX Player 级视听画质增强管理器（支持中英双语 OSD）
/// </summary>
public static class MxEnhancementManager
{
    private static bool _nightModeActive = false;
    private static bool _sharpenActive = false;
    private static bool _vibrantActive = false;
    private static bool _volumeBoostActive = false;
    private static bool _animeModeActive = false;

    // 饱和度/对比度改用加法增量后不再使用恢复基准（保留占位避免外部引用断裂）
    private const int _origContrast = 0;
    private const int _origSaturation = 0;

    public static void ToggleNightMode()
    {
        _nightModeActive = !_nightModeActive;
        if (_nightModeActive)
        {
            Player.Command("af add @mx_night:lavfi=[dynaudnorm=f=75:g=25:p=0.55:m=5.0]");
            ShowOsd(MxI18n.T("OsdNightModeOn"), 2000);
        }
        else
        {
            Player.Command("af remove @mx_night");
            ShowOsd(MxI18n.T("OsdNightModeOff"), 1500);
        }
    }

    public static void ToggleSharpen()
    {
        _sharpenActive = !_sharpenActive;
        if (_sharpenActive)
        {
            Player.Command("vf add @mx_sharpen:lavfi=[unsharp=lx=5:ly=5:la=1.2:cx=3:cy=3:ca=0.8]");
            ShowOsd(MxI18n.T("OsdSharpenOn"), 2000);
        }
        else
        {
            Player.Command("vf remove @mx_sharpen");
            ShowOsd(MxI18n.T("OsdSharpenOff"), 1500);
        }
    }

    public static void ToggleVibrantColor()
    {
        _vibrantActive = !_vibrantActive;
        // 加法式增量（开关顺序无关）：修复与动漫增强交叉开关时的饱和度漂移
        int contrastDelta = _vibrantActive ? 8 : -8;
        int saturationDelta = _vibrantActive ? 15 : -15;
        Player.SetPropertyInt("contrast", Math.Clamp(Player.GetPropertyInt("contrast") + contrastDelta, -100, 100));
        Player.SetPropertyInt("saturation", Math.Clamp(Player.GetPropertyInt("saturation") + saturationDelta, -100, 100));
        ShowOsd(_vibrantActive ? MxI18n.T("OsdVibrantOn") : MxI18n.T("OsdVibrantOff"), _vibrantActive ? 2000 : 1500);
    }

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
            ShowOsd(MxI18n.T("OsdVolBoostOn", (int)Math.Round(Player.GetPropertyDouble("volume"))), 2000);
        }
        else
        {
            Player.SetPropertyDouble("volume-max", 100.0);
            double currentVol = Player.GetPropertyDouble("volume");
            if (currentVol > 100.0)
            {
                Player.SetPropertyDouble("volume", 100.0);
            }
            ShowOsd(MxI18n.T("OsdVolBoostOff"), 1500);
        }
    }

    public static void ToggleAnimeMode()
    {
        _animeModeActive = !_animeModeActive;
        Player.Command(_animeModeActive
            ? "vf add @mx_anime:lavfi=[hqdn3d=1.5:1.5:4:4]"
            : "vf remove @mx_anime");
        int saturationDelta = _animeModeActive ? 12 : -12;
        Player.SetPropertyInt("saturation", Math.Clamp(Player.GetPropertyInt("saturation") + saturationDelta, -100, 100));
        ShowOsd(_animeModeActive ? MxI18n.T("OsdAnimeOn") : MxI18n.T("OsdAnimeOff"), _animeModeActive ? 2000 : 1500);
    }

    public static void ResetAll()
    {
        try { Player.Command("af remove @mx_night"); } catch {}
        try { Player.Command("vf remove @mx_sharpen"); } catch {}
        try { Player.Command("vf remove @mx_anime"); } catch {}
        try { AiFeatures.MxShaderManager.ApplyMode(AiFeatures.SuperResolutionMode.Off); } catch {}

        Player.SetPropertyInt("contrast", 0);
        Player.SetPropertyInt("saturation", 0);
        Player.SetPropertyDouble("volume-max", 100.0);

        _nightModeActive = false;
        _sharpenActive = false;
        _vibrantActive = false;
        _volumeBoostActive = false;
        _animeModeActive = false;

        ShowOsd(MxI18n.T("OsdResetAll"), 2000);
    }

    public static void ShowOsd(string text, int durationMs = 1500)
    {
        try
        {
            Player.CommandV("show-text", text, durationMs.ToString(CultureInfo.InvariantCulture));
        }
        catch {}
    }
}
