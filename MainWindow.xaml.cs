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
        private ObservableCollection<SiteEntry> _sitesList = new();
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

        private void ShowPage(UIElement page)
        {
            DashboardPage.Visibility = Visibility.Collapsed;
            AddSitePage.Visibility   = Visibility.Collapsed;
            SettingsPage.Visibility  = Visibility.Collapsed;
            HelpPage.Visibility      = Visibility.Collapsed;
            AboutPage.Visibility     = Visibility.Collapsed;

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
            else if (app.NextCheckTime.HasValue)
            {
                var remaining = app.NextCheckTime.Value - DateTime.Now;
                StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94)); // green
                StatusBarText.Text = "Monitoring Active";

                if (remaining.TotalSeconds <= 0)
                {
                    StatusBarCountdownText.Text = "Checking now...";
                }
                else if (remaining.TotalHours >= 1)
                {
                    StatusBarCountdownText.Text = $"Next check in {(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
                }
                else
                {
                    StatusBarCountdownText.Text = $"Next check in {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
                }
            }
            else
            {
                StatusBarIcon.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                StatusBarText.Text = "Monitoring Active";
                StatusBarCountdownText.Text = string.Empty;
            }
        }

        // ─── Dashboard ───────────────────────────────────────────────────────────

        public void LoadSites()
        {
            var config = ConfigManager.Load();
            _sitesList.Clear();
            foreach (var site in config.Sites)
                _sitesList.Add(site);

            SitesListView.ItemsSource = _sitesList;
            EmptyTextBlock.Visibility = _sitesList.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnAddSiteNavClick(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavAddSite;
        }

        private async void OnCheckNowClick(object sender, RoutedEventArgs e)
        {
            if (Application.Current is not App myApp) return;

            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;

                var checkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var progressRing = new ProgressRing
                {
                    IsActive = true,
                    Width = 12,
                    Height = 12
                };
                checkPanel.Children.Add(progressRing);
                checkPanel.Children.Add(new TextBlock { Text = "Checking...", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                btn.Content = checkPanel;
            }

            try
            {
                await myApp.PerformChecksAsync(forceNotification: true);
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                    panel.Children.Add(new FontIcon { Glyph = "\uE72C", FontSize = 12 });
                    panel.Children.Add(new TextBlock { Text = "Check Now", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    btn.Content = panel;
                }
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
                if (AddModeComboBox != null) AddModeComboBox.SelectedIndex = site.Mode == DiffMode.DomDiff ? 1 : (site.Mode == DiffMode.Both ? 2 : 0);
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

            var config = ConfigManager.Load();

            if (!string.IsNullOrEmpty(_editingSiteId))
            {
                var site = config.Sites.Find(s => s.Id == _editingSiteId);
                if (site != null)
                {
                    var selectedMode = AddModeComboBox.SelectedIndex == 1 ? DiffMode.DomDiff : (AddModeComboBox.SelectedIndex == 2 ? DiffMode.Both : DiffMode.FullPage);
                    // If URL or Mode changed, clear the last hash so it gets a fresh check
                    if (site.Url != url || site.Mode != selectedMode)
                    {
                        site.LastContentHash = string.Empty;
                        site.LastContent = string.Empty;
                        site.LastChecked = null;

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

                    site.Name = name;
                    site.Url = url;
                    site.Mode = selectedMode;
                    site.Selector = string.Empty;
                }
            }
            else
            {
                var newSite = new SiteEntry
                {
                    Name     = name,
                    Url      = url,
                    Mode     = AddModeComboBox.SelectedIndex == 1 ? DiffMode.DomDiff : (AddModeComboBox.SelectedIndex == 2 ? DiffMode.Both : DiffMode.FullPage),
                    Selector = string.Empty
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
    }
}
