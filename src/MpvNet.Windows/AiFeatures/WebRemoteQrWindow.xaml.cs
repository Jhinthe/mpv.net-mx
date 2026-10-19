using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

public partial class WebRemoteQrWindow : Window
{
    private static WebRemoteQrWindow? _instance;

    public WebRemoteQrWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public static void ShowInstance()
    {
        if (_instance == null)
        {
            _instance = new WebRemoteQrWindow();
        }

        if (!MxWebRemoteServer.IsRunning)
        {
            MxWebRemoteServer.StartServer();
        }

        _instance.RefreshQr();
        _instance.Show();
        _instance.Activate();
        _instance.Focus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshQr();
    }

    private void RefreshQr()
    {
        string localIp = MxWebRemoteServer.GetPrimaryLocalIp();
        string url = $"http://{localIp}:{MxWebRemoteServer.Port}/";
        TxtUrl.Text = url;

        try
        {
            using var bmp = MxQrCode.GenerateBitmap(url, 240);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();

            ImgQrCode.Source = bi;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemoteQrWindow] 生成真实二维码失败: {ex.Message}");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void TxtUrl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CopyUrl();
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = TxtUrl.Text,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemoteQrWindow] 打开浏览器失败: {ex.Message}");
        }
    }

    private void CopyUrl()
    {
        try
        {
            Clipboard.SetText(TxtUrl.Text);
            Player.CommandV("show-text", "已复制遥控网址到剪贴板", "2000");
        }
        catch { }
    }
}
