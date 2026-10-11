using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.MediaLibrary;

public partial class MediaLibraryWindow : Window
{
    private readonly MxMediaLibraryEngine _engine = MxMediaLibraryEngine.Instance;
    private string _currentFilter = "";

    public MediaLibraryWindow()
    {
        InitializeComponent();

        _engine.ScanStarted += OnScanStarted;
        _engine.ScanProgress += OnScanProgress;
        _engine.ScanCompleted += OnScanCompleted;

        Loaded += MediaLibraryWindow_Loaded;
        Closing += MediaLibraryWindow_Closing;
    }

    private void MediaLibraryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshViews();

        // 如果媒体库尚无内容，自动在后台触发首次轻量扫描
        if (_engine.AllItems.Count == 0 && !_engine.IsScanning)
        {
            var scanTask = _engine.ScanAsync();
        }
    }

    private void MediaLibraryWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _engine.ScanStarted -= OnScanStarted;
        _engine.ScanProgress -= OnScanProgress;
        _engine.ScanCompleted -= OnScanCompleted;
    }

    private void OnScanStarted()
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = "正在全速扫描磁盘媒体文件...";
            ProgressBarScan.Visibility = Visibility.Visible;
            ProgressBarScan.IsIndeterminate = true;
            BtnRescan.IsEnabled = false;
        });
    }

    private void OnScanProgress(int count)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = $"已发现 {count} 个媒体文件...";
        });
    }

    private void OnScanCompleted()
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBarScan.Visibility = Visibility.Collapsed;
            BtnRescan.IsEnabled = true;
            RefreshViews();
        });
    }

    private void RefreshViews()
    {
        var all = _engine.AllItems.ToList();
        string query = _currentFilter.Trim().ToLowerInvariant();

        var filtered = string.IsNullOrEmpty(query)
            ? all
            : all.Where(it => it.FileName.ToLowerInvariant().Contains(query) ||
                              it.DirectoryName.ToLowerInvariant().Contains(query)).ToList();

        var videos = filtered.Where(it => it.Type == MediaType.Video).ToList();
        var audios = filtered.Where(it => it.Type == MediaType.Audio).ToList();

        ItemsVideos.ItemsSource = videos;
        ItemsMusic.ItemsSource = audios;
        ItemsFolders.ItemsSource = _engine.Folders;

        int videoCount = videos.Count;
        int audioCount = audios.Count;
        TxtStatus.Text = $"媒体库就绪：共 {videoCount} 部视频，{audioCount} 首音乐";

        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (TabAllVideos.IsChecked == true)
        {
            ScrollViewerVideos.Visibility = Visibility.Visible;
            ScrollViewerFolders.Visibility = Visibility.Collapsed;
            ScrollViewerMusic.Visibility = Visibility.Collapsed;
            PanelEmpty.Visibility = (ItemsVideos.ItemsSource as IEnumerable<MediaItem>)?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (TabFolders.IsChecked == true)
        {
            ScrollViewerVideos.Visibility = Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Visible;
            ScrollViewerMusic.Visibility = Visibility.Collapsed;
            PanelEmpty.Visibility = _engine.Folders.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (TabMusic.IsChecked == true)
        {
            ScrollViewerVideos.Visibility = Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Collapsed;
            ScrollViewerMusic.Visibility = Visibility.Visible;
            PanelEmpty.Visibility = (ItemsMusic.ItemsSource as IEnumerable<MediaItem>)?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        UpdateVisibility();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentFilter = SearchBox.Text;
        RefreshViews();
    }

    private void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        var scanTask = _engine.ScanAsync();
    }

    private void MediaCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is MediaItem item)
        {
            PlayMedia(item);
        }
    }

    private void FolderCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FolderItem folder)
        {
            // 切换到视频列表并填充搜索为该文件夹名称
            SearchBox.Text = folder.FolderName;
            TabAllVideos.IsChecked = true;
            UpdateVisibility();
        }
    }

    private void PlayMedia(MediaItem item)
    {
        try
        {
            // 调用底层 libmpv 秒级起播
            Player.CommandV("loadfile", item.FilePath);

            // 如果有续播记忆且播放过 5 秒以上，则询问或自动 seek
            if (item.LastPosition > 5.0 && item.LastPosition < (item.DurationSeconds - 10.0))
            {
                Player.CommandV("seek", item.LastPosition.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute", "exact");
            }

            // 激活主窗口并最小化或关闭媒体库
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
