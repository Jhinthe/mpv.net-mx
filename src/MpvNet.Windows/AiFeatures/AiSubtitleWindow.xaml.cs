using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.AiFeatures;

public partial class AiSubtitleWindow : Window
{
    private static AiSubtitleWindow? _instance;
    private readonly DispatcherTimer _pollTimer;
    private string _lastSubtitleText = "";

    public AiSubtitleWindow()
    {
        InitializeComponent();
        Closing += (s, e) => { e.Cancel = true; Hide(); };
        KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Hide(); e.Handled = true; } };

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _pollTimer.Tick += PollTimer_Tick;
        _pollTimer.Start();
    }

    public static void ToggleInstance()
    {
        try
        {
            Action act = () =>
            {
                if (System.Windows.Application.Current == null)
                    new System.Windows.Application();

                if (_instance == null || !_instance.IsLoaded)
                    _instance = new AiSubtitleWindow();

                if (_instance.IsVisible)
                {
                    _instance.Hide();
                }
                else
                {
                    _instance.Show();
                    _instance.Activate();
                    _instance.UpdateCurrentDialogue();
                }
            };

            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                System.Windows.Application.Current.Dispatcher.BeginInvoke(act);
            else
                act();
        }
        catch {}
    }

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || ChkAutoSync.IsChecked != true) return;
        UpdateCurrentDialogue();
    }

    private void UpdateCurrentDialogue()
    {
        string currentSub = MxAiSubtitleAssistant.GetCurrentSubtitleText();
        if (string.IsNullOrWhiteSpace(currentSub)) return;

        if (currentSub != _lastSubtitleText)
        {
            _lastSubtitleText = currentSub;
            TxtOriginal.Text = currentSub;

            // 1. 词汇拆解与音标释义
            var words = MxAiSubtitleAssistant.AnalyzeWords(currentSub);
            ItemsWordChips.ItemsSource = words;

            // 2. 异步翻译整句
            Task.Run(async () =>
            {
                string trans = await MxAiSubtitleAssistant.TranslateSentenceAsync(currentSub);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TxtTranslation.Text = trans;
                }));
            });
        }
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnCopyDialogue_Click(object sender, RoutedEventArgs e)
    {
        string text = $"{TxtOriginal.Text}\r\n{TxtTranslation.Text}";
        Clipboard.SetText(text);
        MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh ? "📋 台词与翻译已复制到剪贴板！" : "📋 Dialogue copied!");
    }

    private void BtnLoadSub_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = MxI18n.CurrentLanguage == MxLanguage.Zh ? "选择字幕文件" : "Select Subtitle File",
            Filter = "字幕文件 (*.srt;*.ass;*.vtt;*.sub)|*.srt;*.ass;*.vtt;*.sub|所有文件 (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            MxAiSubtitleAssistant.MountSubtitleFile(dlg.FileName);
        }
    }

    private void BtnExtractSub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Player.CommandV("cycle", "sub");
            string currentTrack = Player.GetPropertyString("current-tracks/sub/title");
            if (string.IsNullOrEmpty(currentTrack))
                currentTrack = Player.GetPropertyString("current-tracks/sub/lang");

            MxEnhancementManager.ShowOsd(MxI18n.CurrentLanguage == MxLanguage.Zh 
                ? $"🔄 已切换字幕轨: {(string.IsNullOrEmpty(currentTrack) ? "默认" : currentTrack)}" 
                : $"🔄 Subtitle Track: {currentTrack}");
            UpdateCurrentDialogue();
        }
        catch {}
    }

    private void WordChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is WordDefinition def)
        {
            Clipboard.SetText(def.Word);
            MxEnhancementManager.ShowOsd($"🔍 [{def.Word}] {def.Phonetic} ({def.Pos}) {def.Meaning}");
        }
    }

    private void BtnSearchOnlineSub_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(async () =>
        {
            await MxSubtitleSuite.SearchAndMountShooterSubtitlesAsync(Player.Path);
            Dispatcher.BeginInvoke(new Action(() => UpdateCurrentDialogue()));
        });
    }

    private void BtnDualSub_Click(object sender, RoutedEventArgs e)
    {
        MxSubtitleSuite.ToggleDualSubtitles();
    }
}
