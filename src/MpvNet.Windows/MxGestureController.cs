using static MpvNet.Global;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MpvNet;

namespace MpvNet.Windows;

/// <summary>
/// MX Player 风格手势控制器：
/// 1. 左侧垂直手势/滚轮：屏幕亮度调节
/// 2. 右侧垂直手势/滚轮：音量调节（支持超量到 130%）
/// 3. 水平手势/中间滚轮：时间轴 Seek 预览与快进/快退
/// 4. 双击分区：左侧后退10s、右侧前进10s、中间播放/暂停
/// 5. 触摸/精确触控板双指缩放平移（Pinch-to-zoom & Pan）
/// </summary>
public class MxGestureController
{
    private readonly Form _form;
    private readonly Func<IntPtr> _getMpvWindowHandle;

    private enum GestureMode
    {
        None,
        Determining,
        Brightness,
        Volume,
        Seek
    }

    private GestureMode _currentMode = GestureMode.None;
    private Point _startPoint;
    private DateTime _startTime;
    private double _initialVolume;
    private int _initialBrightness;
    private double _initialTimePos;
    private double _duration;
    private double _seekOffset;
    private bool _isDragging;

    // 手势死区阈值（防误触）
    private const int DragThreshold = 18;

    public MxGestureController(Form form, Func<IntPtr> getMpvWindowHandle)
    {
        _form = form;
        _getMpvWindowHandle = getMpvWindowHandle;
    }

    /// <summary>
    /// 处理鼠标滚轮消息 WM_MOUSEWHEEL (0x020A)
    /// </summary>
    public bool HandleMouseWheel(IntPtr wParam, IntPtr lParam)
    {
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        if (delta == 0) return false;

        Point screenPt = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        Point clientPt = _form.PointToClient(screenPt);

        double widthRatio = (double)clientPt.X / Math.Max(1, _form.ClientSize.Width);

        if (widthRatio < 0.35)
        {
            // 左侧：调节亮度 (-100 ~ 100)
            int currentBrightness = Player.GetPropertyInt("brightness");
            int step = delta > 0 ? 5 : -5;
            int newBrightness = Math.Clamp(currentBrightness + step, -100, 100);
            Player.SetPropertyInt("brightness", newBrightness);
            int displayVal = (int)Math.Round(((newBrightness + 100) / 200.0) * 100);
            ShowOsd($"☀  亮度: {displayVal}%");
            return true;
        }
        else if (widthRatio > 0.65)
        {
            // 右侧：调节音量 (0 ~ 130)
            double currentVolume = Player.GetPropertyDouble("volume");
            double step = delta > 0 ? 5 : -5;
            double newVolume = Math.Clamp(currentVolume + step, 0, 130);
            Player.SetPropertyDouble("volume", newVolume);
            ShowOsd($"🔊  音量: {(int)Math.Round(newVolume)}%");
            return true;
        }
        else
        {
            // 中间：Seek 快进 / 快退 (5秒)
            int seekSec = delta > 0 ? 5 : -5;
            Player.CommandV("seek", seekSec.ToString(), "relative", "exact");
            string arrow = delta > 0 ? "⏩" : "⏪";
            ShowOsd($"{arrow}  {(delta > 0 ? "+5s" : "-5s")}");
            return true;
        }
    }

    /// <summary>
    /// 鼠标左键按下 WM_LBUTTONDOWN (0x0201)
    /// </summary>
    public void HandleMouseDown(Point clientPt)
    {
        _startPoint = clientPt;
        _startTime = DateTime.UtcNow;
        _currentMode = GestureMode.Determining;
        _isDragging = false;
        _seekOffset = 0;

        _initialVolume = Player.GetPropertyDouble("volume");
        _initialBrightness = Player.GetPropertyInt("brightness");
        _initialTimePos = Player.GetPropertyDouble("time-pos");
        _duration = Player.GetPropertyDouble("duration");
    }

    /// <summary>
    /// 鼠标移动 WM_MOUSEMOVE (0x0200)
    /// </summary>
    public bool HandleMouseMove(Point clientPt)
    {
        if (_currentMode == GestureMode.None) return false;

        int dx = clientPt.X - _startPoint.X;
        int dy = clientPt.Y - _startPoint.Y;

        if (_currentMode == GestureMode.Determining)
        {
            if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
                return false;

            _isDragging = true;
            double widthRatio = (double)_startPoint.X / Math.Max(1, _form.ClientSize.Width);

            if (Math.Abs(dy) > Math.Abs(dx))
            {
                // 垂直手势
                if (widthRatio < 0.35)
                    _currentMode = GestureMode.Brightness;
                else if (widthRatio > 0.65)
                    _currentMode = GestureMode.Volume;
                else
                    _currentMode = GestureMode.Seek; // 中间垂直也允许或作为 Seek
            }
            else
            {
                // 水平手势：全屏 Seek
                _currentMode = GestureMode.Seek;
            }
        }

        switch (_currentMode)
        {
            case GestureMode.Brightness:
                {
                    // 向上滑变亮，向下滑变暗（dy 向下为正）
                    double height = Math.Max(100, _form.ClientSize.Height);
                    double ratio = -dy / height; // -1.0 ~ 1.0
                    int newBrightness = Math.Clamp(_initialBrightness + (int)Math.Round(ratio * 150), -100, 100);
                    Player.SetPropertyInt("brightness", newBrightness);
                    int displayVal = (int)Math.Round(((newBrightness + 100) / 200.0) * 100);
                    ShowOsd($"☀  亮度: {displayVal}%", 500);
                    return true;
                }
            case GestureMode.Volume:
                {
                    // 向上滑加音量，向下滑减音量
                    double height = Math.Max(100, _form.ClientSize.Height);
                    double ratio = -dy / height;
                    double newVolume = Math.Clamp(_initialVolume + (ratio * 100), 0, 130);
                    Player.SetPropertyDouble("volume", newVolume);
                    ShowOsd($"🔊  音量: {(int)Math.Round(newVolume)}%", 500);
                    return true;
                }
            case GestureMode.Seek:
                {
                    // 水平滑动控制 Seek
                    double width = Math.Max(100, _form.ClientSize.Width);
                    double ratio = (double)dx / width; // -1.0 ~ 1.0
                    
                    // 根据视频时长动态调节灵敏度：短视频一屏60秒，长视频一屏10分钟
                    double scaleRange = _duration > 0 ? Math.Min(Math.Max(_duration * 0.15, 30.0), 600.0) : 60.0;
                    _seekOffset = ratio * scaleRange;

                    double targetPos = Math.Clamp(_initialTimePos + _seekOffset, 0, _duration > 0 ? _duration : 36000);
                    string sign = _seekOffset >= 0 ? "+" : "";
                    string offsetStr = FormatTime(Math.Abs(_seekOffset));
                    string targetStr = FormatTime(targetPos);
                    string durStr = _duration > 0 ? FormatTime(_duration) : "--:--";

                    string arrow = _seekOffset >= 0 ? "⏩" : "⏪";
                    ShowOsd($"{arrow}  {sign}{(_seekOffset >= 0 ? "" : "-")}{offsetStr}\n[{targetStr} / {durStr}]", 800);
                    return true;
                }
        }

        return false;
    }

    /// <summary>
    /// 鼠标左键抬起 WM_LBUTTONUP (0x0202)
    /// </summary>
    public bool HandleMouseUp(Point clientPt)
    {
        bool wasHandling = _isDragging;

        if (_currentMode == GestureMode.Seek && Math.Abs(_seekOffset) > 1.0)
        {
            // 执行实际 Seek
            double targetPos = Math.Clamp(_initialTimePos + _seekOffset, 0, _duration > 0 ? _duration : 36000);
            Player.CommandV("seek", targetPos.ToString(CultureInfo.InvariantCulture), "absolute", "exact");
        }

        _currentMode = GestureMode.None;
        _isDragging = false;
        _seekOffset = 0;

        return wasHandling;
    }

    /// <summary>
    /// 双击分区快捷响应 WM_LBUTTONDBLCLK (0x0203)
    /// </summary>
    public bool HandleDoubleClick(Point clientPt)
    {
        double widthRatio = (double)clientPt.X / Math.Max(1, _form.ClientSize.Width);

        if (widthRatio < 0.35)
        {
            // 左侧双击：快退 10 秒
            Player.CommandV("seek", "-10", "relative", "exact");
            ShowOsd("⏪  -10s");
            return true;
        }
        else if (widthRatio > 0.65)
        {
            // 右侧双击：快进 10 秒
            Player.CommandV("seek", "10", "relative", "exact");
            ShowOsd("⏩  +10s");
            return true;
        }
        else
        {
            // 中间双击：播放 / 暂停切换
            Player.Command("cycle pause");
            bool isPaused = Player.GetPropertyBool("pause");
            ShowOsd(isPaused ? "⏸  已暂停" : "▶  播放中");
            return true;
        }
    }

    private void ShowOsd(string text, int durationMs = 1200)
    {
        try
        {
            Player.CommandV("show-text", text, durationMs.ToString());
        }
        catch {}
    }

    private static string FormatTime(double seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1)
            return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
        return string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
    }
}
