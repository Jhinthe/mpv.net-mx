using MpvNet.Windows.WinForms;
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

public partial class MediaLibraryWindow : Window
{
        private static MediaLibraryWindow? _instance;
    private static bool _isExiting = false;

    public static void CloseInstance()
    {
        _isExiting = true;
        _instance?.Dispatcher?.Invoke(() =>
        {
            _instance?.Close();
            _instance = null;
        });
    }
    public static MediaLibraryWindow Instance => _instance ??= new MediaLibraryWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void ShowInstance()
    {
        Action act = () =>
        {
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application();
                if (System.Windows.Application.Current != null) System.Windows.Application.Current.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            }

            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new MediaLibraryWindow();
            }

            _instance.Show();
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(_instance).Handle;
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);
        };

        if (MainForm.Instance != null && MainForm.Instance.InvokeRequired)
        {
            MainForm.Instance.BeginInvoke(act);
        }
        else
        {
            act();
        }
    }
    private readonly MxMediaLibraryEngine _engine = MxMediaLibraryEngine.Instance;
    private string _currentFilter = "";
    private string? _activeFolderPath = null; // 当前正在深入浏览的文件夹路径

    public MediaLibraryWindow()
    {
        InitializeComponent();

        _engine.ScanStarted += OnScanStarted;
        _engine.ScanProgress += OnScanProgress;
                _engine.ScanCompleted += OnScanCompleted;
        _engine.ItemsChanged += OnItemsChanged;

        MxI18n.LanguageChanged += UpdateI18nTexts;

        Loaded += MediaLibraryWindow_Loaded;
        Closing += MediaLibraryWindow_Closing;
    }

    private void MediaLibraryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateI18nTexts();
        RefreshViews();

        if (_engine.AllItems.Count == 0 && !_engine.IsScanning)
        {
            var scanTask = _engine.ScanAsync();
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(hwnd, 19, ref darkMode, sizeof(int));
            int captionColor = 0x161414;
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
        }
    }

    private void MediaLibraryWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting) return;

        if (MainForm.Instance == null || !MainForm.Instance.Visible || string.IsNullOrEmpty(Player.Path))
        {
            _isExiting = true;
            Environment.Exit(0);
            return;
        }

        e.Cancel = true;
        this.Hide();
        MainForm.Instance.Activate();
    }

    private void UpdateI18nTexts()
    {
        Action update = () =>
        {
            Title = MxI18n.T("AppTitle");
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
        };

        if (Dispatcher.CheckAccess())
            update();
        else
            Dispatcher.Invoke(update);
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

        private void OnItemsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            RefreshViews();
            UpdateViewHeading();
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

        // 文件夹深入过滤
        if (_activeFolderPath != null)
        {
            all = all.Where(it => string.Equals(it.DirectoryPath, _activeFolderPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 搜索关键词过滤
        string query = _currentFilter.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(query))
        {
            all = all.Where(it => it.FileName.ToLowerInvariant().Contains(query) ||
                                  it.DirectoryName.ToLowerInvariant().Contains(query)).ToList();
        }

        // 排序处理
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
            1 => items.OrderBy(it => it.FileName, StringComparer.OrdinalIgnoreCase).ToList(), // 名称
            2 => items.OrderByDescending(it => it.FileSizeBytes).ToList(),                     // 大小
            3 => items.OrderByDescending(it => it.DurationSeconds).ToList(),                   // 时长
            _ => items.OrderByDescending(it => it.LastModified).ToList()                      // 最新修改
        };
    }

    private void UpdateVisibility()
    {
        bool isGridView = BtnViewGrid.IsChecked == true;

        if (_activeFolderPath != null)
        {
            // 正在查看某个文件夹内部
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

    private void FolderCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FolderItem folder)
        {
            // 真实深入浏览该文件夹
            _activeFolderPath = folder.FolderPath;
            SearchBox.Text = "";
            RefreshViews();
        }
    }

    private void MediaCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is MediaItem item)
        {
            PlayMedia(item);
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

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_activeFolderPath != null)
            {
                // 先返回文件夹列表
                BtnBackToFolders_Click(sender, e);
            }
            else
            {
                Close();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            BtnRescan_Click(sender, e);
            e.Handled = true;
        }
    }

    // 右键上下文菜单事件处理
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
        if (item != null) PlayMedia(item);
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

    private void PlayMedia(MediaItem item)
    {
        try
        {
            // 1. 先安全还原并呈现主播放器窗口，杜绝黑屏或窗口全隐引发的假闪退
            if (MainForm.Instance != null)
            {
                MainForm.Instance.Invoke(new Action(() =>
                {
                    try
                    {
                        MainForm.Instance.Show();
                        MainForm.Instance.Visible = true;
                        if (MainForm.Instance.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                        {
                            MainForm.Instance.WindowState = System.Windows.Forms.FormWindowState.Normal;
                        }
                        MainForm.Instance.BringToFront();
                        MainForm.Instance.Activate();
                        SetForegroundWindow(MainForm.Instance.Handle);
                    }
                    catch {}
                }));
            }

            // 2. 调用播放器核心标准接口加载视频
            Player.CommandV("loadfile", item.FilePath);

            // 3. 毫秒级断点续播
            if (item.LastPosition > 5.0 && item.LastPosition < (item.DurationSeconds - 10.0))
            {
                Player.CommandV("seek", item.LastPosition.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), "absolute");
            }

            // 4. 播放器已就位后平滑隐藏媒体库
            this.Hide();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
