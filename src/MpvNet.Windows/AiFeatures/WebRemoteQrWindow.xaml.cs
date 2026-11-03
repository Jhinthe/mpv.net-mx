using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QRCoder;
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
        try
        {
            if (!MxWebRemoteServer.IsRunning)
            {
                TxtUrl.Text = "⚠ 遥控服务启动失败（端口被占用或权限不足）";
                ImgQrCode.Source = null;
                return;
            }

            string url = MxWebRemoteServer.GetServerUrl();
            TxtUrl.Text = url;

            // QRCoder (MIT)：ISO/IEC 18004 全合规实现，任何手机相机/微信均可秒扫
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 8);

            var bi = new BitmapImage();
            using var ms = new MemoryStream(png);
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();

            ImgQrCode.Source = bi;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebRemoteQrWindow] 生成二维码失败: {ex.Message}");
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
            Player.CommandV("show-text", MxI18n.T("OsdCopied"), "2000");
        }
        catch { }
    }
}
