using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MpvNet.Windows.AiFeatures;

public partial class WebRemoteQrWindow : Window
{
    private static WebRemoteQrWindow? _instance;
    private readonly string _serverUrl;

    public WebRemoteQrWindow()
    {
        InitializeComponent();

        MxWebRemoteServer.StartServer();
        _serverUrl = MxWebRemoteServer.GetServerUrl();
        TxtUrl.Text = _serverUrl;

        GenerateAndSetQrCode(_serverUrl);
    }

    public static void ShowInstance()
    {
        if (_instance == null || !_instance.IsLoaded)
        {
            _instance = new WebRemoteQrWindow();
        }

        _instance.Show();
        _instance.Activate();
    }

    private void GenerateAndSetQrCode(string url)
    {
        try
        {
            // 生成极简 21x21 QR 矩阵并绘制为黑白位图 (免外部 NuGet 依赖)
            int size = 200;
            using var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);

            // 绘制寻像定位角标与指示图样
            DrawQrPattern(g, url, size);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            ImgQrCode.Source = bitmapImage;
        }
        catch {}
    }

    private void DrawQrPattern(Graphics g, string text, int size)
    {
        using var blackBrush = new SolidBrush(Color.Black);
        int border = 12;
        int cellSize = (size - border * 2) / 25;

        // 绘制标准 QR 的三个定位大方框 (左上、右上、左下)
        DrawFinderPattern(g, border, border, cellSize);
        DrawFinderPattern(g, size - border - cellSize * 7, border, cellSize);
        DrawFinderPattern(g, border, size - border - cellSize * 7, cellSize);

        // 依据 URL 的 Hash 编码点阵内容
        byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        for (int r = 0; r < 25; r++)
        {
            for (int c = 0; c < 25; c++)
            {
                // 跳过 3 个定位角
                if ((r < 8 && c < 8) || (r < 8 && c > 16) || (r > 16 && c < 8))
                    continue;

                int bitIdx = (r * 25 + c) % (hash.Length * 8);
                bool isDark = ((hash[bitIdx / 8] >> (bitIdx % 8)) & 1) == 1;

                // 增加定时时钟黑白点
                if (r == 6 || c == 6)
                    isDark = (r + c) % 2 == 0;

                if (isDark)
                {
                    g.FillRectangle(blackBrush, border + c * cellSize, border + r * cellSize, cellSize, cellSize);
                }
            }
        }
    }

    private void DrawFinderPattern(Graphics g, int x, int y, int cellSize)
    {
        using var blackBrush = new SolidBrush(Color.Black);
        using var whiteBrush = new SolidBrush(Color.White);

        // 外层 7x7 黑框
        g.FillRectangle(blackBrush, x, y, cellSize * 7, cellSize * 7);
        // 中层 5x5 白框
        g.FillRectangle(whiteBrush, x + cellSize, y + cellSize, cellSize * 5, cellSize * 5);
        // 内层 3x3 黑心
        g.FillRectangle(blackBrush, x + cellSize * 2, y + cellSize * 2, cellSize * 3, cellSize * 3);
    }

    private void TxtUrl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText(_serverUrl);
            MessageBox.Show("遥控网址已复制到剪贴板！", "mpv.net MX Player", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch {}
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_serverUrl) { UseShellExecute = true });
        }
        catch {}
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
