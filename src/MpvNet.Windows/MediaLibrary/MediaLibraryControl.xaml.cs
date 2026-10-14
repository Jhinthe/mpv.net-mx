using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MpvNet;
using static MpvNet.Global;

namespace MpvNet.Windows.MediaLibrary;

public partial class MediaLibraryControl : UserControl
{
    private readonly MxMediaLibraryEngine _engine = MxMediaLibraryEngine.Instance;
    private string _currentFilter = "";
    private string? _activeFolderPath = null;

    /// <summary>
    /// 当用户在媒体库中挑选媒体播放时触发（参数：文件绝对路径、断点秒数）
    /// </summary>
    public event Action<string, double>? RequestPlay;

    /// <summary>
    /// 当用户点击关闭或按 ESC 返回主播放画面时触发
    /// </summary>
    public event Action? RequestClose;

    public MediaLibraryControl()
    {
        InitializeComponent();

        _engine.ScanStarted += OnScanStarted;
        _engine.ScanProgress += OnScanProgress;
        _engine.ScanCompleted += OnScanCompleted;

        MxI18n.LanguageChanged += UpdateI18nTexts;

        Loaded += MediaLibraryControl_Loaded;
    }

    private void MediaLibraryControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateI18nTexts();
        RefreshViews();

        if (_engine.AllItems.Count == 0 && !_engine.IsScanning)
        {
            var scanTask = _engine.ScanAsync();
        }
    }

    /// <summary>
    /// 外部通知当前播放状态，以决定是否展示“▶ 返回播放”按钮
    /// </summary>
    public void SetPlayingState(bool hasMedia)
    {
        Dispatcher.Invoke(() =>
        {
            BtnBackToPlayer.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    public void UpdateI18nTexts()
    {
        Dispatcher.Invoke(() =>
        {
            TxtBrandTitle.Text = MxI18n.T("BrandTitle");
            TabAllVideos.Content = MxI18n.T("TabAllVideos");
            TabFolders.Content = MxI18n.T("TabFolders");
            TabMusic.Content = MxI18n.T("TabMusic");

            TxtSearchPlaceholder.Text = MxI18n.T("SearchPlaceholder");
            BtnAddFolder.Content = MxI18n.T("AddFolder");
            BtnRescan.Content = MxI18n.T("ScanDisk");
            BtnLangToggle.Content = MxI18n.T("LanguageToggle");

            TxtSortLabel.Text = MxI18n.T("SortBy");
            OptSortDate.Content = MxI18n.T("SortDate");
            OptSortName.Content = MxI18n.T("SortName");
            OptSortSize.Content = MxI18n.T("SortSize");
            OptSortDuration.Content = MxI18n.T("SortDuration");

            BtnBackToFolders.Content = MxI18n.T("BackToFolders");
            TxtEmptyTitle.Text = MxI18n.T("EmptyTitle");
            TxtEmptySub.Text = MxI18n.T("EmptySub");

            if (Resources["CardContextMenu"] is ContextMenu cm)
            {
                foreach (var item in cm.Items)
                {
                    if (item is MenuItem mi)
                    {
                        if (mi.Name == "CtxMenuPlay") mi.Header = MxI18n.T("MenuPlay");
                        else if (mi.Name == "CtxMenuOpenFolder") mi.Header = MxI18n.T("MenuOpenFolder");
                        else if (mi.Name == "CtxMenuCopyPath") mi.Header = MxI18n.T("MenuCopyPath");
                        else if (mi.Name == "CtxMenuRemove") mi.Header = MxI18n.T("MenuRemove");
                    }
                }
            }

            UpdateViewHeading();
        });
    }

    private void UpdateViewHeading()
    {
        if (_activeFolderPath != null)
        {
            BreadcrumbPanel.Visibility = Visibility.Visible;
            TxtViewHeading.Visibility = Visibility.Collapsed;
            string folderName = Path.GetFileName(_activeFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            TxtBreadcrumbFolder.Text = $"📂 {folderName}";
        }
        else
        {
            BreadcrumbPanel.Visibility = Visibility.Collapsed;
            TxtViewHeading.Visibility = Visibility.Visible;
            if (TabAllVideos.IsChecked == true)
                TxtViewHeading.Text = MxI18n.T("TabAllVideos");
            else if (TabFolders.IsChecked == true)
                TxtViewHeading.Text = MxI18n.T("TabFolders");
            else if (TabMusic.IsChecked == true)
                TxtViewHeading.Text = MxI18n.T("TabMusic");
        }
    }

    private void OnScanStarted()
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = MxI18n.T("ScanningStatus");
            ProgressBarScan.Visibility = Visibility.Visible;
            ProgressBarScan.IsIndeterminate = true;
            BtnRescan.IsEnabled = false;
        });
    }

    private void OnScanProgress(int count)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = MxI18n.T("ScanningProgress", count);
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

    public void RefreshViews()
    {
        var all = _engine.AllItems.ToList();

        if (_activeFolderPath != null)
        {
            all = all.Where(it => string.Equals(it.DirectoryPath, _activeFolderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        string query = _currentFilter.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(query))
        {
            all = all.Where(it => it.FileName.ToLowerInvariant().Contains(query) ||
                                  it.DirectoryName.ToLowerInvariant().Contains(query)).ToList();
        }

        all = SortMediaItems(all);

        var videos = all.Where(it => it.Type == MediaType.Video).ToList();
        var audios = all.Where(it => it.Type == MediaType.Audio).ToList();

        ItemsVideosGrid.ItemsSource = videos;
        ItemsVideosList.ItemsSource = videos;
        ItemsMusic.ItemsSource = audios;
        ItemsFolders.ItemsSource = _engine.Folders;

        long totalBytes = _engine.GetTotalSizeBytes();
        string totalSize = MxMediaLibraryEngine.FormatFileSize(totalBytes);
        TxtStatus.Text = MxI18n.T("ReadyStatus", videos.Count, audios.Count, totalSize);

        UpdateVisibility();
        UpdateViewHeading();
    }

    private List<MediaItem> SortMediaItems(List<MediaItem> items)
    {
        int sortIdx = CmbSort.SelectedIndex;
        return sortIdx switch
        {
            1 => items.OrderBy(it => it.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            2 => items.OrderByDescending(it => it.FileSizeBytes).ToList(),
            3 => items.OrderByDescending(it => it.DurationSeconds).ToList(),
            _ => items.OrderByDescending(it => it.LastModified).ToList()
        };
    }

    private void UpdateVisibility()
    {
        bool isGridView = BtnViewGrid.IsChecked == true;

        if (_activeFolderPath != null)
        {
            ScrollViewerVideosGrid.Visibility = isGridView ? Visibility.Visible : Visibility.Collapsed;
            ScrollViewerVideosList.Visibility = !isGridView ? Visibility.Visible : Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Collapsed;
            ScrollViewerMusic.Visibility = Visibility.Collapsed;
            PanelEmpty.Visibility = (ItemsVideosGrid.ItemsSource as IEnumerable<MediaItem>)?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        if (TabAllVideos.IsChecked == true)
        {
            ScrollViewerVideosGrid.Visibility = isGridView ? Visibility.Visible : Visibility.Collapsed;
            ScrollViewerVideosList.Visibility = !isGridView ? Visibility.Visible : Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Collapsed;
            ScrollViewerMusic.Visibility = Visibility.Collapsed;
            PanelEmpty.Visibility = (ItemsVideosGrid.ItemsSource as IEnumerable<MediaItem>)?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (TabFolders.IsChecked == true)
        {
            ScrollViewerVideosGrid.Visibility = Visibility.Collapsed;
            ScrollViewerVideosList.Visibility = Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Visible;
            ScrollViewerMusic.Visibility = Visibility.Collapsed;
            PanelEmpty.Visibility = _engine.Folders.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (TabMusic.IsChecked == true)
        {
            ScrollViewerVideosGrid.Visibility = Visibility.Collapsed;
            ScrollViewerVideosList.Visibility = Visibility.Collapsed;
            ScrollViewerFolders.Visibility = Visibility.Collapsed;
            ScrollViewerMusic.Visibility = Visibility.Visible;
            PanelEmpty.Visibility = (ItemsMusic.ItemsSource as IEnumerable<MediaItem>)?.Any() == true ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        _activeFolderPath = null;
        RefreshViews();
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        UpdateVisibility();
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshViews();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentFilter = SearchBox.Text;
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_currentFilter) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentFilter) ? Visibility.Collapsed : Visibility.Visible;
        RefreshViews();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void BtnLangToggle_Click(object sender, RoutedEventArgs e)
    {
        MxI18n.ToggleLanguage();
    }

    private void BtnBackToFolders_Click(object sender, RoutedEventArgs e)
    {
        _activeFolderPath = null;
        TabFolders.IsChecked = true;
        RefreshViews();
    }

    private void BtnBackToPlayer_Click(object sender, RoutedEventArgs e)
    {
        RequestClose?.Invoke();
    }

    private void BtnCloseView_Click(object sender, RoutedEventArgs e)
    {
        RequestClose?.Invoke();
    }

    private void FolderCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FolderItem folder)
        {
            _activeFolderPath = folder.FolderPath;
            SearchBox.Text = "";
            RefreshViews();
        }
    }

    private void MediaCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is MediaItem item)
        {
            RequestPlay?.Invoke(item.FilePath, item.LastPosition);
        }
    }

    private void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        var scanTask = _engine.ScanAsync();
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要加入 MX 媒体库的影视或音乐文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            var task = _engine.AddPathsAsync(new[] { dlg.SelectedPath });
        }
    }

    private void Control_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Control_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var task = _engine.AddPathsAsync(files);
            }
        }
    }

    private void Control_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_activeFolderPath != null)
            {
                BtnBackToFolders_Click(sender, e);
            }
            else
            {
                RequestClose?.Invoke();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            BtnRescan_Click(sender, e);
            e.Handled = true;
        }
    }

    private MediaItem? GetContextMediaItem(object sender)
    {
        if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement elem && elem.DataContext is MediaItem item)
            return item;

        if (Mouse.DirectlyOver is DependencyObject dep)
        {
            var fe = dep as FrameworkElement;
            while (fe != null && fe.DataContext is not MediaItem)
            {
                fe = System.Windows.Media.VisualTreeHelper.GetParent(fe) as FrameworkElement;
            }
            if (fe?.DataContext is MediaItem mi2) return mi2;
        }

        return null;
    }

    private void CtxMenuPlay_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextMediaItem(sender);
        if (item != null) RequestPlay?.Invoke(item.FilePath, item.LastPosition);
    }

    private void CtxMenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextMediaItem(sender);
        if (item != null && File.Exists(item.FilePath))
        {
            Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }
    }

    private void CtxMenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextMediaItem(sender);
        if (item != null)
        {
            Clipboard.SetText(item.FilePath);
        }
    }

    private void CtxMenuRemove_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextMediaItem(sender);
        if (item != null)
        {
            _engine.RemoveItem(item.FilePath);
            RefreshViews();
        }
    }
}
