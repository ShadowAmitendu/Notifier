using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Notifier.Models;
using Notifier.Services;

namespace Notifier
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_HIDE = 0;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        private System.Drawing.Icon? _windowIconSmall;
        private System.Drawing.Icon? _windowIconBig;

        public bool CanClose { get; set; } = false;
        public string DisplayVersion => $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0"}";
        private System.Collections.Generic.Stack<string> _backStack = new();
        private string _currentTag = "Dashboard";
        private bool _lastIsCheckingState = false;
        private ObservableCollection<SiteEntry> _sitesList = new();
        private List<SiteEntry> _allSitesList = new();
        private string? _editingSiteId = null;
        private bool _isMonitoringEnabled = true;

        // Status bar timer
        private DispatcherTimer _statusBarTimer = new();

        // Settings change tracking
        private bool _isLoadingSettings = false;

        // Default settings values
        private const int DefaultIntervalMinutes = 60;
        private const int DefaultJitterSeconds = 30;
        private const bool DefaultRunAtStartup = false;
        private const bool DefaultMonitoringEnabled = true;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "Site Monitor Dashboard";

            // Extend content into the title bar region and register our TitleBar control.
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(920, 640));

            try
            {
                _windowIconSmall = App.CreateAppIcon(16);
                _windowIconBig = App.CreateAppIcon(32);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, _windowIconSmall.Handle);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, _windowIconBig.Handle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set window icon: {ex.Message}");
            }

            SetupWindowClosingIntercept();

            // Initialize status bar countdown timer
            _statusBarTimer.Interval = TimeSpan.FromSeconds(1);
            _statusBarTimer.Tick += (s, e) => UpdateStatusBar();
            _statusBarTimer.Start();

            NavView.SelectedItem = NavDashboard;
            LoadSites();

            var config = ConfigManager.Load();
            UpdateTitlebarStatus(config.Settings.IsMonitoringEnabled);
        }

        // ─── Window close intercept ──────────────────────────────────────────────

        private void SetupWindowClosingIntercept()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            appWindow.Closing += (s, e) =>
            {
                if (!CanClose)
                {
                    e.Cancel = true;
                    ShowWindow(hwnd, SW_HIDE);
                }
                else
                {
                    if (_windowIconSmall != null)
                    {
                        IntPtr hIcon = _windowIconSmall.Handle;
                        _windowIconSmall.Dispose();
                        DestroyIcon(hIcon);
                    }
                    if (_windowIconBig != null)
                    {
                        IntPtr hIcon = _windowIconBig.Handle;
                        _windowIconBig.Dispose();
                        DestroyIcon(hIcon);
                    }
                }
            };
        }

        // ─── Hamburger / pane toggle ─────────────────────────────────────────────

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }

        // ─── NavigationView selection ────────────────────────────────────────────

        private void NavView_SelectionChanged(NavigationView sender,
                                              NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;
            NavigateTo(item.Tag?.ToString() ?? string.Empty);
        }

        public void NavigateTo(string tag, bool clearForm = true)
        {
            NavigateToInternal(tag, clearForm, isBackNavigation: false);
        }

        private void NavigateToInternal(string tag, bool clearForm = true, bool isBackNavigation = false)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (tag == _currentTag && _backStack.Count > 0) return;

            // Push current tag to back stack if it's not a back navigation and is a new page
            if (!isBackNavigation && tag != _currentTag)
            {
                _backStack.Push(_currentTag);
            }

            // If navigating to Dashboard, clear back stack as it is the root page
            if (tag == "Dashboard")
            {
                _backStack.Clear();
            }

            _currentTag = tag;
            UpdateBackButtonState();

            NavView.SelectionChanged -= NavView_SelectionChanged;

            switch (tag)
            {
                case "Dashboard":
                    NavView.SelectedItem = NavDashboard;
                    ShowPage(DashboardPage);
                    LoadSites();
                    break;

                case "AddSite":
                    NavView.SelectedItem = NavAddSite;
                    ShowPage(AddSitePage);
                    if (clearForm)
                    {
                        ClearAddSiteForm();
                    }
                    break;

                case "Logs":
                    NavView.SelectedItem = NavLogs;
                    ShowPage(LogsPage);
                    LoadLogs();
                    break;

                case "Settings":
                    NavView.SelectedItem = NavSettings;
                    ShowPage(SettingsPage);
                    LoadSettingsForm();
                    break;

                case "Help":
                    NavView.SelectedItem = NavHelp;
                    ShowPage(HelpPage);
                    break;

                case "About":
                    NavView.SelectedItem = NavAbout;
                    ShowPage(AboutPage);
                    break;
            }

            NavView.SelectionChanged += NavView_SelectionChanged;
        }

        private void UpdateBackButtonState()
        {
            if (BackButton != null)
            {
                BackButton.IsEnabled = _backStack.Count > 0;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backStack.Count > 0)
            {
                string prevTag = _backStack.Pop();
                NavigateToInternal(prevTag, clearForm: false, isBackNavigation: true);
            }
        }

        private void ShowPage(UIElement page)
        {
            DashboardPage.Visibility = Visibility.Collapsed;
            AddSitePage.Visibility   = Visibility.Collapsed;
            SettingsPage.Visibility  = Visibility.Collapsed;
            HelpPage.Visibility      = Visibility.Collapsed;
            AboutPage.Visibility     = Visibility.Collapsed;
            if (LogsPage != null) LogsPage.Visibility = Visibility.Collapsed;

            // Setup initial values for animation
            page.Opacity = 0.0;
            var transform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = 24.0 };
            page.RenderTransform = transform;
            page.Visibility = Visibility.Visible;

            // Create storyboard for smooth fade-in and slide-in
            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

            var opacityAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnim, page);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, "Opacity");

            var slideAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 24.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slideAnim, page);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slideAnim, "(UIElement.RenderTransform).(TranslateTransform.X)");

            storyboard.Children.Add(opacityAnim);
            storyboard.Children.Add(slideAnim);
            storyboard.Begin();
        }

        // ─── Status Bar ──────────────────────────────────────────────────────────

        private void UpdateStatusBar()
        {
            UpdateCheckNowButtonState();

            if (StatusBarText == null || StatusBarCountdownText == null || StatusBarIcon == null) return;

            var app = Application.Current as App;
            if (app == null) return;

            var config = ConfigManager.Load();
            bool monitoringEnabled = config.Settings.IsMonitoringEnabled;

            if (app.IsChecking)
            {
                StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 251, 191, 36)); // amber
                StatusBarText.Text = "Checking sites now...";
                StatusBarCountdownText.Text = string.Empty;
            }
            else if (!monitoringEnabled)
            {
                StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 148, 163, 184)); // slate
                StatusBarText.Text = "Monitoring Disabled";
                StatusBarCountdownText.Text = string.Empty;
            }
            else
            {
                DateTime? nextCheck = null;
                string nextSiteName = string.Empty;
                foreach (var s in config.Sites)
                {
                    if (s.NextCheck.HasValue)
                    {
                        if (nextCheck == null || s.NextCheck.Value < nextCheck.Value)
                        {
                            nextCheck = s.NextCheck.Value;
                            nextSiteName = s.Name;
                        }
                    }
                    else
                    {
                        nextCheck = DateTime.Now;
                        nextSiteName = s.Name;
                        break;
                    }
                }

                if (nextCheck.HasValue)
                {
                    var remaining = nextCheck.Value - DateTime.Now;
                    StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94)); // green
                    StatusBarText.Text = "Monitoring Active";

                    string siteSuffix = string.IsNullOrEmpty(nextSiteName) ? "" : $" ({nextSiteName})";
                    if (remaining.TotalSeconds <= 0)
                    {
                        StatusBarCountdownText.Text = $"Checking now...{siteSuffix}";
                    }
                    else if (remaining.TotalHours >= 1)
                    {
                        StatusBarCountdownText.Text = $"Next check in {(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s{siteSuffix}";
                    }
                    else
                    {
                        StatusBarCountdownText.Text = $"Next check in {remaining.Minutes:D2}m {remaining.Seconds:D2}s{siteSuffix}";
                    }
                }
                else
                {
                    StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                    StatusBarText.Text = "Monitoring Active";
                    StatusBarCountdownText.Text = "No sites scheduled";
                }
            }
        }

        private void UpdateCheckNowButtonState()
        {
            if (TitleCheckNowButton == null) return;

            var app = Application.Current as App;
            if (app == null) return;

            bool isChecking = app.IsChecking;
            if (isChecking == _lastIsCheckingState) return;

            _lastIsCheckingState = isChecking;

            if (isChecking)
            {
                TitleCheckNowButton.IsEnabled = false;

                var checkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var progressRing = new ProgressRing
                {
                    IsActive = true,
                    Width = 12,
                    Height = 12
                };
                checkPanel.Children.Add(progressRing);
                checkPanel.Children.Add(new TextBlock { Text = "Checking...", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                TitleCheckNowButton.Content = checkPanel;
            }
            else
            {
                TitleCheckNowButton.IsEnabled = true;

                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                panel.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 12 });
                panel.Children.Add(new TextBlock { Text = "Check Now", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                TitleCheckNowButton.Content = panel;
            }
        }

        // ─── Dashboard ───────────────────────────────────────────────────────────

        public void LoadSites()
        {
            var config = ConfigManager.Load();
            _allSitesList.Clear();
            _allSitesList.AddRange(config.Sites);

            if (SitesListView != null)
            {
                SitesListView.ItemsSource = _sitesList;
            }

            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            if (SearchBox == null || FilterComboBox == null || SortComboBox == null || EmptyTextBlock == null) return;

            string query = SearchBox.Text?.Trim() ?? string.Empty;
            int filterIdx = FilterComboBox.SelectedIndex; // 0: All, 1: Changed, 2: Success, 3: Error, 4: Pending
            int sortIdx = SortComboBox.SelectedIndex;     // 0: A-Z, 1: Z-A, 2: Last Checked, 3: Next Check

            // 1. Filter
            var filtered = new List<SiteEntry>();
            foreach (var site in _allSitesList)
            {
                // Search query matching
                bool matchesSearch = string.IsNullOrEmpty(query) 
                    || site.Name.Contains(query, StringComparison.OrdinalIgnoreCase) 
                    || site.Url.Contains(query, StringComparison.OrdinalIgnoreCase);

                if (!matchesSearch) continue;

                // Status matching
                bool matchesFilter = filterIdx switch
                {
                    1 => site.LastStatus == "Changed",
                    2 => site.LastStatus == "Success",
                    3 => site.LastStatus == "Error",
                    4 => site.LastStatus == "Pending",
                    _ => true
                };

                if (matchesFilter)
                {
                    filtered.Add(site);
                }
            }

            // 2. Sort
            switch (sortIdx)
            {
                case 0: // Name A-Z
                    filtered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case 1: // Name Z-A
                    filtered.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case 2: // Last Checked (latest first, null last)
                    filtered.Sort((a, b) => {
                        if (!a.LastChecked.HasValue && !b.LastChecked.HasValue) return 0;
                        if (!a.LastChecked.HasValue) return 1;
                        if (!b.LastChecked.HasValue) return -1;
                        return b.LastChecked.Value.CompareTo(a.LastChecked.Value);
                    });
                    break;
                case 3: // Next Check (earliest first, null last)
                    filtered.Sort((a, b) => {
                        if (!a.NextCheck.HasValue && !b.NextCheck.HasValue) return 0;
                        if (!a.NextCheck.HasValue) return 1;
                        if (!b.NextCheck.HasValue) return -1;
                        return a.NextCheck.Value.CompareTo(b.NextCheck.Value);
                    });
                    break;
            }

            // 3. Update ObservableCollection
            _sitesList.Clear();
            foreach (var site in filtered)
            {
                _sitesList.Add(site);
            }

            // Update UI visibility
            EmptyTextBlock.Visibility = _sitesList.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilterAndSort();
        }

        private void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilterAndSort();
        }

        private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilterAndSort();
        }

        private async void OnViewDiffClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SiteEntry site)
            {
                var oldText = site.PreviousContent;
                var newText = site.LastContent;

                var diffLines = await Task.Run(() => DiffEngine.ComputeDiff(oldText, newText));

                var dialogContent = new Grid
                {
                    Width = 680,
                    Height = 460,
                    RowDefinitions = 
                    {
                        new RowDefinition { Height = GridLength.Auto },
                        new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                    }
                };

                // Header info
                var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                headerPanel.Children.Add(new TextBlock 
                { 
                    Text = $"Comparing previous snapshot vs current snapshot for {site.Name}", 
                    TextWrapping = TextWrapping.Wrap, 
                    Margin = new Thickness(0, 0, 0, 4), 
                    FontSize = 12, 
                    Foreground = Application.Current.Resources["SystemControlPageTextBaseMediumBrush"] as Brush 
                });
                dialogContent.Children.Add(headerPanel);
                Grid.SetRow(headerPanel, 0);

                // Scrollable container for lines
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Application.Current.Resources["SystemControlBackgroundChromeMediumLowBrush"] as Brush,
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(4)
                };
                dialogContent.Children.Add(scrollViewer);
                Grid.SetRow(scrollViewer, 1);

                // StackPanel to hold all lines
                var linesPanel = new StackPanel { Spacing = 2 };

                Brush GetBrushFromHex(string hex)
                {
                    if (string.IsNullOrEmpty(hex) || hex.Length < 7) 
                        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0));
                    
                    try
                    {
                        if (hex.Length == 9) // #AARRGGBB
                        {
                            byte a = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                            byte r = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                            byte g = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
                            byte b = byte.Parse(hex.Substring(7, 2), System.Globalization.NumberStyles.HexNumber);
                            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
                        }
                        else if (hex.Length == 7) // #RRGGBB
                        {
                            byte r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                            byte g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                            byte b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
                            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, r, g, b));
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                    return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0));
                }

                foreach (var line in diffLines)
                {
                    var row = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = new GridLength(36) },
                            new ColumnDefinition { Width = new GridLength(36) },
                            new ColumnDefinition { Width = new GridLength(16) },
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                        },
                        Background = GetBrushFromHex(line.BackgroundColor),
                        Padding = new Thickness(4, 2, 4, 2)
                    };

                    var textBrush = GetBrushFromHex(line.ForegroundColor);

                    // Old line no
                    var tbOld = new TextBlock 
                    { 
                        Text = line.DisplayOldLine, 
                        FontSize = 11, 
                        Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128)),
                        HorizontalAlignment = HorizontalAlignment.Right, 
                        Margin = new Thickness(0, 0, 8, 0),
                        FontFamily = new FontFamily("Consolas")
                    };
                    Grid.SetColumn(tbOld, 0);
                    row.Children.Add(tbOld);

                    // New line no
                    var tbNew = new TextBlock 
                    { 
                        Text = line.DisplayNewLine, 
                        FontSize = 11, 
                        Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 128, 128, 128)),
                        HorizontalAlignment = HorizontalAlignment.Right, 
                        Margin = new Thickness(0, 0, 8, 0),
                        FontFamily = new FontFamily("Consolas")
                    };
                    Grid.SetColumn(tbNew, 1);
                    row.Children.Add(tbNew);

                    // Prefix (+/-)
                    var tbPrefix = new TextBlock 
                    { 
                        Text = line.LinePrefix, 
                        FontSize = 12, 
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = textBrush, 
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontFamily = new FontFamily("Consolas")
                    };
                    Grid.SetColumn(tbPrefix, 2);
                    row.Children.Add(tbPrefix);

                    // Text content
                    var tbText = new TextBlock 
                    { 
                        Text = line.Text, 
                        FontSize = 11, 
                        Foreground = textBrush, 
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas")
                    };
                    Grid.SetColumn(tbText, 3);
                    row.Children.Add(tbText);

                    linesPanel.Children.Add(row);
                }

                scrollViewer.Content = linesPanel;

                var dialog = new ContentDialog
                {
                    Title = "Visual Content Diff",
                    Content = dialogContent,
                    CloseButtonText = "Close",
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowAsync();
            }
        }

        private void OnAddSiteNavClick(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavAddSite;
        }

        private async void OnCheckNowClick(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App myApp) return;

            try
            {
                var checkTask = myApp.PerformChecksAsync(forceNotification: true);
                UpdateCheckNowButtonState();
                await checkTask;
            }
            finally
            {
                UpdateCheckNowButtonState();
                LoadSites();
            }
        }

        private void OnRemoveSiteClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SiteEntry site)
            {
                var config = ConfigManager.Load();
                config.Sites.RemoveAll(s => s.Id == site.Id);
                ConfigManager.Save(config);

                // Clean up local HTML snapshot file
                try
                {
                    string snapshotsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SiteNotifier",
                        "Snapshots"
                    );
                    string snapshotPath = Path.Combine(snapshotsDir, $"{site.Id}.html");
                    if (System.IO.File.Exists(snapshotPath))
                    {
                        System.IO.File.Delete(snapshotPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete snapshot: {ex.Message}");
                }

                LoadSites();
            }
        }

        // ─── Add Site (inline form) ──────────────────────────────────────────────

        private void ClearAddSiteForm()
        {
            _editingSiteId = null;
            if (AddSiteTitleText != null) AddSiteTitleText.Text = "Add New Site";
            if (AddSiteSaveButton != null) AddSiteSaveButton.Content = "Add Site";
            if (AddSiteCancelButton != null) AddSiteCancelButton.Content = "Clear";

            AddNameTextBox.Text     = string.Empty;
            AddUrlTextBox.Text      = string.Empty;
            if (AddModeComboBox != null) AddModeComboBox.SelectedIndex = 0;
            
            if (AddSelectorTextBox != null)
            {
                AddSelectorTextBox.Text = string.Empty;
                AddSelectorTextBox.Visibility = Visibility.Collapsed;
            }

            if (AddCustomIntervalCheck != null) AddCustomIntervalCheck.IsChecked = false;
            if (AddCustomIntervalPanel != null) AddCustomIntervalPanel.Visibility = Visibility.Collapsed;
            if (AddIntervalBox != null) AddIntervalBox.Value = 60;
            if (AddIntervalUnit != null) AddIntervalUnit.SelectedIndex = 0;

            if (AddMethodComboBox != null) AddMethodComboBox.SelectedIndex = 0;
            if (AddHeadersTextBox != null) AddHeadersTextBox.Text = string.Empty;
            if (AddCookiesTextBox != null) AddCookiesTextBox.Text = string.Empty;
            if (AddBodyTextBox != null)
            {
                AddBodyTextBox.Text = string.Empty;
                AddBodyTextBox.Visibility = Visibility.Collapsed;
            }

            AddValidationText.Visibility     = Visibility.Collapsed;
            AddValidationText.Text           = string.Empty;
        }

        private void OnEditSiteClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is SiteEntry site)
            {
                _editingSiteId = site.Id;

                AddNameTextBox.Text = site.Name;
                AddUrlTextBox.Text = site.Url;
                
                int modeIdx = site.Mode switch
                {
                    DiffMode.DomDiff => 1,
                    DiffMode.Both => 2,
                    DiffMode.CssSelector => 3,
                    _ => 0
                };
                if (AddModeComboBox != null) AddModeComboBox.SelectedIndex = modeIdx;

                if (AddSelectorTextBox != null)
                {
                    AddSelectorTextBox.Text = site.Selector;
                    AddSelectorTextBox.Visibility = site.Mode == DiffMode.CssSelector ? Visibility.Visible : Visibility.Collapsed;
                }

                if (AddCustomIntervalCheck != null) AddCustomIntervalCheck.IsChecked = site.UseCustomInterval;
                if (AddCustomIntervalPanel != null)
                {
                    AddCustomIntervalPanel.Visibility = site.UseCustomInterval ? Visibility.Visible : Visibility.Collapsed;
                }

                if (site.UseCustomInterval)
                {
                    if (site.CustomIntervalMinutes >= 60 && site.CustomIntervalMinutes % 60 == 0)
                    {
                        if (AddIntervalBox != null) AddIntervalBox.Value = site.CustomIntervalMinutes / 60;
                        if (AddIntervalUnit != null) AddIntervalUnit.SelectedIndex = 1; // Hours
                    }
                    else
                    {
                        if (AddIntervalBox != null) AddIntervalBox.Value = site.CustomIntervalMinutes;
                        if (AddIntervalUnit != null) AddIntervalUnit.SelectedIndex = 0; // Minutes
                    }
                }
                else
                {
                    if (AddIntervalBox != null) AddIntervalBox.Value = 60;
                    if (AddIntervalUnit != null) AddIntervalUnit.SelectedIndex = 0;
                }

                if (AddMethodComboBox != null) AddMethodComboBox.SelectedIndex = site.HttpMethod == "POST" ? 1 : 0;
                if (AddHeadersTextBox != null) AddHeadersTextBox.Text = site.CustomHeaders;
                if (AddCookiesTextBox != null) AddCookiesTextBox.Text = site.CustomCookies;
                if (AddBodyTextBox != null)
                {
                    AddBodyTextBox.Text = site.RequestBody;
                    AddBodyTextBox.Visibility = site.HttpMethod == "POST" ? Visibility.Visible : Visibility.Collapsed;
                }

                AddValidationText.Visibility = Visibility.Collapsed;

                AddSiteTitleText.Text = "Edit Site Details";
                AddSiteSaveButton.Content = "Save Changes";
                AddSiteCancelButton.Content = "Cancel";

                NavigateTo("AddSite", clearForm: false);
            }
        }

        private void OnAddSiteSaveClick(object sender, RoutedEventArgs e)
        {
            string name     = AddNameTextBox.Text?.Trim() ?? string.Empty;
            string url      = AddUrlTextBox.Text?.Trim()  ?? string.Empty;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
            {
                ShowAddValidation("Please enter both a name and a URL.");
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ShowAddValidation("Please enter a valid HTTP or HTTPS URL.");
                return;
            }

            var selectedMode = AddModeComboBox.SelectedIndex switch
            {
                1 => DiffMode.DomDiff,
                2 => DiffMode.Both,
                3 => DiffMode.CssSelector,
                _ => DiffMode.FullPage
            };

            string selector = AddSelectorTextBox?.Text?.Trim() ?? string.Empty;
            if (selectedMode == DiffMode.CssSelector && string.IsNullOrEmpty(selector))
            {
                ShowAddValidation("Please enter a CSS Selector or XPath.");
                return;
            }

            bool useCustomInterval = AddCustomIntervalCheck?.IsChecked == true;
            int customIntervalMinutes = 60;
            if (useCustomInterval)
            {
                double intervalVal = AddIntervalBox?.Value ?? 60;
                if (double.IsNaN(intervalVal) || intervalVal < 1)
                {
                    ShowAddValidation("Custom interval must be at least 1.");
                    return;
                }
                int value = (int)intervalVal;
                customIntervalMinutes = AddIntervalUnit?.SelectedIndex == 1 ? value * 60 : value;
            }

            string httpMethod = AddMethodComboBox?.SelectedIndex == 1 ? "POST" : "GET";
            string headers = AddHeadersTextBox?.Text?.Trim() ?? string.Empty;
            string cookies = AddCookiesTextBox?.Text?.Trim() ?? string.Empty;
            string requestBody = AddBodyTextBox?.Text ?? string.Empty;

            var config = ConfigManager.Load();

            if (!string.IsNullOrEmpty(_editingSiteId))
            {
                var site = config.Sites.Find(s => s.Id == _editingSiteId);
                if (site != null)
                {
                    bool requestParamsChanged = site.Url != url 
                        || site.Mode != selectedMode 
                        || site.Selector != selector
                        || site.HttpMethod != httpMethod
                        || site.CustomHeaders != headers
                        || site.CustomCookies != cookies
                        || site.RequestBody != requestBody;

                    // If request parameters changed, clear the last hash so it gets a fresh check
                    if (requestParamsChanged)
                    {
                        site.LastContentHash = string.Empty;
                        site.LastContent = string.Empty;
                        site.LastChecked = null;
                        site.NextCheck = null;

                        // Delete the old HTML snapshot file
                        try
                        {
                            string snapshotsDir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "SiteNotifier",
                                "Snapshots"
                            );
                            string snapshotPath = Path.Combine(snapshotsDir, $"{site.Id}.html");
                            if (System.IO.File.Exists(snapshotPath))
                            {
                                System.IO.File.Delete(snapshotPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to delete old snapshot: {ex.Message}");
                        }
                    }
                    else if (site.UseCustomInterval != useCustomInterval || site.CustomIntervalMinutes != customIntervalMinutes)
                    {
                        // Interval changed but params didn't: just reschedule
                        site.NextCheck = DateTime.Now;
                    }

                    site.Name = name;
                    site.Url = url;
                    site.Mode = selectedMode;
                    site.Selector = selector;
                    site.UseCustomInterval = useCustomInterval;
                    site.CustomIntervalMinutes = customIntervalMinutes;
                    site.HttpMethod = httpMethod;
                    site.CustomHeaders = headers;
                    site.CustomCookies = cookies;
                    site.RequestBody = requestBody;
                }
            }
            else
            {
                var newSite = new SiteEntry
                {
                    Name     = name,
                    Url      = url,
                    Mode     = selectedMode,
                    Selector = selector,
                    UseCustomInterval = useCustomInterval,
                    CustomIntervalMinutes = customIntervalMinutes,
                    HttpMethod = httpMethod,
                    CustomHeaders = headers,
                    CustomCookies = cookies,
                    RequestBody = requestBody,
                    NextCheck = null // Will check immediately
                };
                config.Sites.Add(newSite);
            }

            ConfigManager.Save(config);

            // Trigger a background check for the new/updated site
            if (Application.Current is App myApp)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    DispatcherQueue.TryEnqueue(async () =>
                        await myApp.PerformChecksAsync(forceNotification: true));
                });
            }

            ClearAddSiteForm();

            // Navigate back to Dashboard and refresh
            NavigateTo("Dashboard");
        }

        private void OnAddSiteClearClick(object sender, RoutedEventArgs e)
        {
            bool wasEditing = !string.IsNullOrEmpty(_editingSiteId);
            ClearAddSiteForm();
            if (wasEditing)
            {
                NavigateTo("Dashboard");
            }
        }

        private void ShowAddValidation(string message)
        {
            AddValidationText.Text       = message;
            AddValidationText.Visibility = Visibility.Visible;
        }

        // ─── Settings (inline form) ──────────────────────────────────────────────

        private void LoadSettingsForm()
        {
            _isLoadingSettings = true;
            try
            {
                var config = ConfigManager.Load();

                int totalMinutes = config.Settings.CheckIntervalMinutes;
                if (totalMinutes >= 60 && totalMinutes % 60 == 0)
                {
                    SettingsIntervalBox.Value = totalMinutes / 60;
                    SettingsIntervalUnit.SelectedIndex = 1; // Hours
                }
                else
                {
                    SettingsIntervalBox.Value = totalMinutes;
                    SettingsIntervalUnit.SelectedIndex = 0; // Minutes
                }

                SettingsJitterBox.Value = config.Settings.MaxJitterSeconds;
                SettingsStartupCheck.IsChecked = StartupHelper.IsStartupEnabled();
                _isMonitoringEnabled = config.Settings.IsMonitoringEnabled;
                UpdateMonitoringButtonsState(config.Settings.IsMonitoringEnabled);
                SettingsValidationText.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isLoadingSettings = false;
            }

            // Disable save/discard by default — no unsaved changes
            SetSettingsButtonsSaved();
        }

        private bool HasSettingsChanged()
        {
            if (_isLoadingSettings) return false;
            if (SettingsIntervalBox == null || SettingsIntervalUnit == null || SettingsJitterBox == null || SettingsStartupCheck == null) return false;

            var config = ConfigManager.Load();
            var saved = config.Settings;

            // Compute current form interval in minutes
            double intervalVal = SettingsIntervalBox.Value;
            if (double.IsNaN(intervalVal)) return true;
            int value = (int)intervalVal;
            int currentIntervalMinutes = SettingsIntervalUnit.SelectedIndex == 1 ? value * 60 : value;

            double jitterVal = SettingsJitterBox.Value;
            if (double.IsNaN(jitterVal)) return true;
            int currentJitter = (int)jitterVal;

            bool currentStartup = SettingsStartupCheck.IsChecked == true;
            bool currentMonitoring = _isMonitoringEnabled;

            return currentIntervalMinutes != saved.CheckIntervalMinutes
                || currentJitter != saved.MaxJitterSeconds
                || currentStartup != StartupHelper.IsStartupEnabled()
                || currentMonitoring != saved.IsMonitoringEnabled;
        }

        private void UpdateSettingsButtonsState()
        {
            if (_isLoadingSettings) return;
            if (SettingsIntervalBox == null || SettingsIntervalUnit == null || SettingsJitterBox == null || SettingsStartupCheck == null || SettingsSaveButton == null || SettingsDiscardButton == null || SettingsValidationText == null) return;

            bool changed = HasSettingsChanged();
            if (changed)
            {
                // Re-enable buttons and restore Save text
                SettingsSaveButton.IsEnabled = true;
                SettingsDiscardButton.IsEnabled = true;

                // Reset Save button back to normal text (in case it was showing Done)
                SettingsSaveButton.Content = "Save Settings";
                SettingsSaveButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;

                SettingsValidationText.Visibility = Visibility.Collapsed;
            }
            else
            {
                SetSettingsButtonsSaved();
            }
        }

        private void SetSettingsButtonsSaved()
        {
            SettingsSaveButton.IsEnabled = false;
            SettingsDiscardButton.IsEnabled = false;
            // Keep the button text as "Save Settings" (not Done) when disabled with no changes
            SettingsSaveButton.Content = "Save Settings";
        }

        private void OnSettingsSaveClick(object sender, RoutedEventArgs e)
        {
            double intervalVal = SettingsIntervalBox.Value;
            double jitterVal   = SettingsJitterBox.Value;

            if (double.IsNaN(intervalVal) || intervalVal < 1)
            {
                ShowSettingsValidation("Check interval must be at least 1.");
                return;
            }
            if (double.IsNaN(jitterVal) || jitterVal < 0)
            {
                ShowSettingsValidation("Jitter delay cannot be negative.");
                return;
            }

            int value        = (int)intervalVal;
            int totalMinutes = SettingsIntervalUnit.SelectedIndex == 1 ? value * 60 : value;
            int jitter       = (int)jitterVal;
            bool runAtStart  = SettingsStartupCheck.IsChecked == true;
            bool enableCheck = _isMonitoringEnabled;

            var config = ConfigManager.Load();
            config.Settings.CheckIntervalMinutes = totalMinutes;
            config.Settings.MaxJitterSeconds     = jitter;
            config.Settings.RunAtStartup         = runAtStart;
            config.Settings.IsMonitoringEnabled  = enableCheck;
            ConfigManager.Save(config);

            StartupHelper.SetStartup(runAtStart);

            if (Application.Current is App myApp)
                myApp.ConfigureTimer();

            SettingsValidationText.Visibility = Visibility.Collapsed;

            // Show Done state — tick icon + "Done" text, disable both buttons
            var donePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            donePanel.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 13 });
            donePanel.Children.Add(new TextBlock { Text = "Done", VerticalAlignment = VerticalAlignment.Center });
            SettingsSaveButton.Content = donePanel;
            SettingsSaveButton.IsEnabled = false;
            SettingsDiscardButton.IsEnabled = false;
        }

        private void OnSettingsDiscardClick(object sender, RoutedEventArgs e)
        {
            LoadSettingsForm(); // reload from disk = discard edits
        }

        private async void OnExportConfigClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                
                // WinUI 3 HWND association
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("JSON Files", new List<string> { ".json" });
                savePicker.SuggestedFileName = "notifier_config_backup.json";

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    var config = ConfigManager.Load();
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    string json = System.Text.Json.JsonSerializer.Serialize(config, options);

                    await Windows.Storage.FileIO.WriteTextAsync(file, json);

                    SettingsValidationText.Text = "Configuration exported successfully!";
                    SettingsValidationText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129)); // Green-500
                    SettingsValidationText.Visibility = Visibility.Visible;

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(4000);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            SettingsValidationText.Visibility = Visibility.Collapsed;
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                ShowSettingsValidation($"Failed to export: {ex.Message}");
            }
        }

        private async void OnImportConfigClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var openPicker = new Windows.Storage.Pickers.FileOpenPicker();
                
                // WinUI 3 HWND association
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);

                openPicker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                openPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                openPicker.FileTypeFilter.Add(".json");

                var file = await openPicker.PickSingleFileAsync();
                if (file != null)
                {
                    string json = await Windows.Storage.FileIO.ReadTextAsync(file);
                    
                    // Deserialize and validate
                    var importedData = System.Text.Json.JsonSerializer.Deserialize<ConfigData>(json);
                    if (importedData == null || importedData.Sites == null || importedData.Settings == null)
                    {
                        ShowSettingsValidation("Invalid configuration file format.");
                        return;
                    }

                    // Prompt user for confirmation before importing
                    var confirmDialog = new ContentDialog
                    {
                        Title = "Import Configuration",
                        Content = $"This will merge {importedData.Sites.Count} imported site(s) and overwrite your current settings. Do you want to proceed?",
                        PrimaryButtonText = "Import & Merge",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.Content.XamlRoot
                    };

                    var dialogResult = await confirmDialog.ShowAsync();
                    if (dialogResult == ContentDialogResult.Primary)
                    {
                        var config = ConfigManager.Load();
                        
                        // Merge sites (by ID or URL to avoid duplicates)
                        int addedCount = 0;
                        foreach (var importedSite in importedData.Sites)
                        {
                            if (!config.Sites.Exists(s => s.Id == importedSite.Id || s.Url.Equals(importedSite.Url, StringComparison.OrdinalIgnoreCase)))
                            {
                                config.Sites.Add(importedSite);
                                addedCount++;
                            }
                        }

                        // Overwrite global settings
                        config.Settings = importedData.Settings;

                        ConfigManager.Save(config);

                        // Refresh Startup helper registry settings
                        StartupHelper.SetStartup(config.Settings.RunAtStartup);

                        // Reconfigure timers
                        if (Application.Current is App myApp)
                            myApp.ConfigureTimer();

                        // Reload settings form and sites list
                        LoadSettingsForm();
                        LoadSites();

                        SettingsValidationText.Text = $"Import complete! Merged {addedCount} site(s) and updated settings.";
                        SettingsValidationText.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 16, 185, 129)); // Green-500
                        SettingsValidationText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowSettingsValidation($"Failed to import: {ex.Message}");
            }
        }

        private void OnSettingsRestoreClick(object sender, RoutedEventArgs e)
        {
            // Load default values into the form controls
            _isLoadingSettings = true;
            try
            {
                SettingsIntervalBox.Value = DefaultIntervalMinutes;
                SettingsIntervalUnit.SelectedIndex = 0; // Minutes
                SettingsJitterBox.Value = DefaultJitterSeconds;
                SettingsStartupCheck.IsChecked = DefaultRunAtStartup;
                UpdateMonitoringButtonsState(DefaultMonitoringEnabled);
                SettingsValidationText.Visibility = Visibility.Collapsed;
            }
            finally
            {
                _isLoadingSettings = false;
            }

            // Now check for differences (defaults may or may not differ from saved)
            UpdateSettingsButtonsState();
        }

        private void OnSettingsEnableClick(object sender, RoutedEventArgs e)
        {
            UpdateMonitoringButtonsState(true);
            UpdateSettingsButtonsState();
        }

        private void OnSettingsDisableClick(object sender, RoutedEventArgs e)
        {
            UpdateMonitoringButtonsState(false);
            UpdateSettingsButtonsState();
        }

        // ─── Settings change tracking event handlers ─────────────────────────────

        private void OnSettingsIntervalBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateSettingsButtonsState();
        }

        private void OnSettingsIntervalUnitChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSettingsButtonsState();
        }

        private void OnSettingsJitterBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            UpdateSettingsButtonsState();
        }

        private void OnSettingsStartupCheckChanged(object sender, RoutedEventArgs e)
        {
            UpdateSettingsButtonsState();
        }

        // ─── Shared settings helpers ─────────────────────────────────────────────

        private void UpdateTitlebarStatus(bool enabled)
        {
            if (TitlebarStatusText != null)
            {
                TitlebarStatusText.Text = enabled ? "Active" : "Disabled";
            }
        }

        private void UpdateMonitoringButtonsState(bool enabled)
        {
            _isMonitoringEnabled = enabled;
            if (SettingsEnableButton == null || SettingsDisableButton == null) return;

            var accentStyle = Application.Current.Resources["AccentButtonStyle"] as Style;
            var defaultStyle = Application.Current.Resources["DefaultButtonStyle"] as Style;

            SettingsEnableButton.Style = enabled ? accentStyle : defaultStyle;
            SettingsDisableButton.Style = enabled ? defaultStyle : accentStyle;

            UpdateTitlebarStatus(enabled);
        }

        private void ShowSettingsValidation(string message)
        {
            SettingsValidationText.Text       = message;
            SettingsValidationText.Visibility = Visibility.Visible;
            SettingsValidationText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38)); // red-600
        }

        // ─── Help: Copy command ──────────────────────────────────────────────────

        private void OnCopyCommandClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string text)
            {
                try
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

                    var oldContent = button.Content;
                    var checkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    checkPanel.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12 });
                    checkPanel.Children.Add(new TextBlock { Text = "Copied", FontSize = 12 });
                    button.Content = checkPanel;

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            button.Content = oldContent;
                        });
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to copy: {ex.Message}");
                }
            }
        }

        // ─── Add/Edit form event handlers ────────────────────────────────────────

        private void OnAddModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AddSelectorTextBox != null && AddModeComboBox != null)
            {
                AddSelectorTextBox.Visibility = AddModeComboBox.SelectedIndex == 3 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        private void OnAddCustomIntervalCheckChanged(object sender, RoutedEventArgs e)
        {
            if (AddCustomIntervalPanel != null && AddCustomIntervalCheck != null)
            {
                AddCustomIntervalPanel.Visibility = AddCustomIntervalCheck.IsChecked == true 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        private void OnAddMethodChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AddBodyTextBox != null && AddMethodComboBox != null)
            {
                AddBodyTextBox.Visibility = AddMethodComboBox.SelectedIndex == 1 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        // ─── Logs Management ─────────────────────────────────────────────────────

        public void LoadLogs()
        {
            var logs = LogManager.LoadLogs();
            LogsListView.ItemsSource = logs;
            EmptyLogsTextBlock.Visibility = logs.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnClearLogsClick(object sender, RoutedEventArgs e)
        {
            LogManager.ClearLogs();
            LoadLogs();
        }
    }
}
