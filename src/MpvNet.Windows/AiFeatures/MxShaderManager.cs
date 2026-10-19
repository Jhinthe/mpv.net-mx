using System;
using System.IO;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

public enum SuperResolutionMode
{
    Off,
    Anime4kModeA,  // 模式 A: 1080P 高清动漫线条重构
    Anime4kModeB,  // 模式 B: 720P 动漫去模糊与画质提升
    Anime4kModeC,  // 模式 C: 老番噪点压制与细节修复
    AmdFsr,        // AMD FSR 高保真影视超分辨率
    AmdCas         // AMD CAS 对比度自适应微细节锐化
}

/// <summary>
/// 方向四：二次元与影视超分辨率画质核弹管理器
/// 1. 集成 Anime4K v4.0.1 神经网络超分辨率着色器
/// 2. 集成 AMD FidelityFX Super Resolution (FSR) & CAS
/// 3. 支持快捷键与右键菜单一键无缝热切换
/// </summary>
public static class MxShaderManager
{
    public static SuperResolutionMode CurrentMode { get; private set; } = SuperResolutionMode.Off;

    public static void ApplyMode(SuperResolutionMode mode)
    {
        try
        {
            CurrentMode = mode;
            string shadersStr = "";

            switch (mode)
            {
                case SuperResolutionMode.Anime4kModeA:
                    // 模式 A: 线条重构 + CNN 画质还原 (适用于高质量 1080P)
                    shadersStr = "~~/shaders/Anime4K_Restore_CNN_M.glsl;~~/shaders/Anime4K_Upscale_CNN_x2_M.glsl;~~/shaders/Anime4K_AutoDownscalePre_x2.glsl";
                    Player.CommandV("change-list", "glsl-shaders", "set", shadersStr);
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🌸 Anime4K [模式 A]: 1080P 动漫线条重构 (已开启)" 
                        : "🌸 Anime4K Mode A: 1080p Line Refinement [ON]");
                    break;

                case SuperResolutionMode.Anime4kModeB:
                    // 模式 B: 去模糊 + CNN 画质提升 (适用于普通 720P)
                    shadersStr = "~~/shaders/Anime4K_Restore_CNN_Soft_M.glsl;~~/shaders/Anime4K_Upscale_CNN_x2_M.glsl;~~/shaders/Anime4K_AutoDownscalePre_x2.glsl";
                    Player.CommandV("change-list", "glsl-shaders", "set", shadersStr);
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🌸 Anime4K [模式 B]: 720P 动漫去模糊提升 (已开启)" 
                        : "🌸 Anime4K Mode B: 720p Deblur & Upscale [ON]");
                    break;

                case SuperResolutionMode.Anime4kModeC:
                    // 模式 C: 强力降噪 + 线条锐化 (适用于低清/老番修复)
                    shadersStr = "~~/shaders/Anime4K_Denoise_Bilateral_Mode.glsl;~~/shaders/Anime4K_Restore_CNN_M.glsl;~~/shaders/Anime4K_Upscale_CNN_x2_M.glsl;~~/shaders/Anime4K_AutoDownscalePre_x2.glsl";
                    Player.CommandV("change-list", "glsl-shaders", "set", shadersStr);
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🌸 Anime4K [模式 C]: 老番降噪与强力修复 (已开启)" 
                        : "🌸 Anime4K Mode C: Vintage Denoise & Restore [ON]");
                    break;

                case SuperResolutionMode.AmdFsr:
                    // AMD FSR 高保真超分辨率 (适用于真人影视、电影与 3D 质感)
                    shadersStr = "~~/shaders/FSR.glsl";
                    Player.CommandV("change-list", "glsl-shaders", "set", shadersStr);
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🎮 AMD FSR 超分辨率: 影视高保真锐化 (已开启)" 
                        : "🎮 AMD FSR: FidelityFX Super Resolution [ON]");
                    break;

                case SuperResolutionMode.AmdCas:
                    // AMD CAS 对比度自适应锐化
                    shadersStr = "~~/shaders/CAS.glsl";
                    Player.CommandV("change-list", "glsl-shaders", "set", shadersStr);
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🎮 AMD CAS: 对比度自适应微细节锐化 (已开启)" 
                        : "🎮 AMD CAS: Contrast Adaptive Sharpening [ON]");
                    break;

                case SuperResolutionMode.Off:
                default:
                    Player.CommandV("change-list", "glsl-shaders", "clr", "");
                    MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                        ? "🔄 超分辨率着色器: 全部已关闭" 
                        : "🔄 Super Resolution: OFF");
                    break;
            }
        }
        catch (Exception ex)
        {
            Terminal.WriteError(ex);
        }
    }

    public static void CycleAnime4K()
    {
        if (CurrentMode == SuperResolutionMode.Anime4kModeA)
            ApplyMode(SuperResolutionMode.Anime4kModeB);
        else if (CurrentMode == SuperResolutionMode.Anime4kModeB)
            ApplyMode(SuperResolutionMode.Anime4kModeC);
        else if (CurrentMode == SuperResolutionMode.Anime4kModeC)
            ApplyMode(SuperResolutionMode.Off);
        else
            ApplyMode(SuperResolutionMode.Anime4kModeA);
    }

    public static void ToggleAmdFsr()
    {
        if (CurrentMode == SuperResolutionMode.AmdFsr)
            ApplyMode(SuperResolutionMode.Off);
        else
            ApplyMode(SuperResolutionMode.AmdFsr);
    }

    public static void ToggleAmdCas()
    {
        if (CurrentMode == SuperResolutionMode.AmdCas)
            ApplyMode(SuperResolutionMode.Off);
        else
            ApplyMode(SuperResolutionMode.AmdCas);
    }
    private static void SetShaders(params string[] shaders)
    {
        Player.CommandV("change-list", "glsl-shaders", "clr", "");
        if (shaders != null)
        {
            foreach (var shader in shaders)
            {
                if (!string.IsNullOrWhiteSpace(shader))
                {
                    Player.CommandV("change-list", "glsl-shaders", "append", shader);
                }
            }
        }
    }
}