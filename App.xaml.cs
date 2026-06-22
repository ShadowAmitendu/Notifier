using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Notifier.Models;
using Notifier.Services;

namespace Notifier
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Windows.Forms.ContextMenuStrip? _contextMenu;
        private System.Drawing.Icon? _appIcon;
        private DispatcherTimer? _checkTimer;
        private NotificationService? _notificationService;
        
        private string _lastClickedUrl = string.Empty;
        private bool _isChecking = false;

        public bool IsChecking => _isChecking;
        public DateTime? NextCheckTime
        {
            get
            {
                var config = ConfigManager.Load();
                if (!config.Settings.IsMonitoringEnabled || config.Sites.Count == 0)
                {
                    return null;
                }

                DateTime? earliest = null;
                foreach (var site in config.Sites)
                {
                    var next = site.NextCheck;
                    if (next == null)
                    {
                        return DateTime.Now; // Due immediately
                    }
                    if (earliest == null || next < earliest)
                    {
                        earliest = next;
                    }
                }
                return earliest;
            }
        }

        public static MainWindow? m_mainWindow;

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try { System.IO.File.WriteAllText("E:\\Notifier\\crash_domain.txt", e.ExceptionObject.ToString()); } catch {}
            };
            TaskScheduler.UnobservedTaskException += (s, e) => {
                try { System.IO.File.WriteAllText("E:\\Notifier\\crash_task.txt", e.Exception.ToString()); } catch {}
            };
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                System.IO.File.WriteAllText("E:\\Notifier\\crash.txt", e.Exception.ToString());
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Set up single-instance activation redirection
            var appInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
            appInstance.Activated += (s, e) =>
            {
                if (m_mainWindow != null)
                {
                    m_mainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowMainWindow();
                    });
                }
                else
                {
                    ShowMainWindow();
                }
            };

            // Initialize Config & Settings
            var config = ConfigManager.Load();

            // Set up dynamic tray icon
            _appIcon = CreateAppIcon();

            // Setup default Context Menu
            _contextMenu = new System.Windows.Forms.ContextMenuStrip();

            _contextMenu.Items.Add("Check Now", null, async (s, e) => await PerformChecksAsync(forceNotification: true));
            _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _contextMenu.Items.Add("Add Site...", null, (s, e) => ShowAddSiteWindow());
            _contextMenu.Items.Add("Manage Sites...", null, (s, e) => ShowMainWindow());
            _contextMenu.Items.Add("Settings...", null, (s, e) => ShowSettingsWindow());
            _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _contextMenu.Items.Add("Exit", null, (s, e) => ExitApp());

            // Initialize NotifyIcon
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = _appIcon,
                Text = "Notifier",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };

            _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            _notificationService = new NotificationService(_notifyIcon);

            // Pre-create and show the main window
            ShowMainWindow();
            
            // Setup periodic DispatcherTimer
            ConfigureTimer();

            // Run initial check (startup check) asynchronously
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500); // Allow tray initialization to settle
                // Perform check on the Dispatcher Queue thread
                m_mainWindow?.DispatcherQueue.TryEnqueue(async () =>
                {
                    await PerformChecksAsync(forceNotification: true, isStartup: true);
                });
            });
        }

        public void ConfigureTimer()
        {
            var config = ConfigManager.Load();

            if (_checkTimer == null)
            {
                _checkTimer = new DispatcherTimer();
                _checkTimer.Tick += async (s, e) => await PerformChecksAsync(forceNotification: false);
            }
            else
            {
                _checkTimer.Stop();
            }

            if (!config.Settings.IsMonitoringEnabled)
            {
                return;
            }

            // Run check timer every 30 seconds to evaluate if any site is due
            _checkTimer.Interval = TimeSpan.FromSeconds(30);
            _checkTimer.Start();
        }

        public static void ShowMainWindow()
        {
            if (m_mainWindow == null)
            {
                m_mainWindow = new MainWindow();
            }

            m_mainWindow.Activate();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(m_mainWindow);
            ShowWindow(hwnd, SW_SHOW);
            SetForegroundWindow(hwnd);
            
            // Refresh list
            m_mainWindow.LoadSites();
        }

        private void ShowAddSiteWindow()
        {
            ShowMainWindow();
            m_mainWindow?.NavigateTo("AddSite");
        }

        private void ShowSettingsWindow()
        {
            ShowMainWindow();
            m_mainWindow?.NavigateTo("Settings");
        }

        public async Task PerformChecksAsync(bool forceNotification, bool isStartup = false)
        {
            if (_isChecking) return;
            _isChecking = true;

            try
            {
                var config = ConfigManager.Load();
                if (config.Sites.Count == 0)
                {
                    if (isStartup || forceNotification)
                    {
                        _notifyIcon?.ShowBalloonTip(3000, "Site Notifier", "No sites configured. Right-click the tray icon to add a site.", System.Windows.Forms.ToolTipIcon.Info);
                    }
                    return;
                }

                var now = DateTime.Now;
                var sitesToProcess = new List<SiteEntry>();
                for (int i = 0; i < config.Sites.Count; i++)
                {
                    var site = config.Sites[i];
                    bool isDue = site.NextCheck == null || site.NextCheck <= now;
                    if (forceNotification || isStartup || isDue)
                    {
                        sitesToProcess.Add(site);
                    }
                }

                if (sitesToProcess.Count == 0)
                {
                    return;
                }

                if (isStartup)
                {
                    _notifyIcon?.ShowBalloonTip(3000, "Site Notifier", $"App startup: Checking {sitesToProcess.Count} site(s)...", System.Windows.Forms.ToolTipIcon.Info);
                }

                var random = new Random();
                int updatedCount = 0;
                int errorCount = 0;
                var updatedSites = new List<string>();

                for (int i = 0; i < sitesToProcess.Count; i++)
                {
                    var site = sitesToProcess[i];

                    // Jitter delay between sites (skipped for the very first site if force/startup)
                    if (config.Settings.MaxJitterSeconds > 0 && (i > 0 || (!forceNotification && !isStartup)))
                    {
                        int jitterMs = random.Next(1, config.Settings.MaxJitterSeconds * 1000);
                        await Task.Delay(jitterMs);
                    }

                    // Check if snapshot exists before checking
                    string snapshotsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SiteNotifier",
                        "Snapshots"
                    );
                    string snapshotPath = Path.Combine(snapshotsDir, $"{site.Id}.html");
                    bool snapshotExists = File.Exists(snapshotPath);

                    var result = await SiteChecker.CheckSiteAsync(site);
                    site.LastChecked = DateTime.Now;
                    
                    // Schedule next check
                    int intervalVal = site.UseCustomInterval ? site.CustomIntervalMinutes : config.Settings.CheckIntervalMinutes;
                    site.NextCheck = DateTime.Now.AddMinutes(Math.Max(1, intervalVal));

                    if (result.IsError)
                    {
                        errorCount++;
                        site.LastStatus = "Error";
                        site.LastStatusMessage = result.ErrorMessage;
                        LogManager.AddLog(site.Id, site.Name, site.Url, "Error", result.ErrorMessage);
                        if (forceNotification && !isStartup)
                        {
                            _notificationService?.ShowError(site.Name, result.ErrorMessage);
                        }
                    }
                    else
                    {
                        if (result.HasChanged)
                        {
                            updatedCount++;
                            _lastClickedUrl = site.Url;
                            updatedSites.Add(site.Name);

                            site.LastStatus = "Changed";
                            site.LastStatusMessage = "Changes detected on the webpage.";
                            site.PreviousContent = site.LastContent; // Move current content to previous
                            site.LastContent = result.NewContent;
                            site.LastContentHash = result.NewHash;

                            LogManager.AddLog(site.Id, site.Name, site.Url, "Changed", "Changes detected on the webpage.");

                            // For periodic background checks, notify immediately on each update
                            if (!forceNotification && !isStartup)
                            {
                                _notificationService?.ShowUpdate(site.Name, site.Url);
                            }
                        }
                        else
                        {
                            site.LastStatus = "Success";
                            if (!snapshotExists)
                            {
                                site.LastStatusMessage = "Initial snapshot created. Monitoring started.";
                                site.LastContent = result.NewContent;
                                site.LastContentHash = result.NewHash;
                                site.PreviousContent = string.Empty;
                                LogManager.AddLog(site.Id, site.Name, site.Url, "Success", "Initial snapshot created. Monitoring started.");
                            }
                            else
                            {
                                site.LastStatusMessage = "Checked. No changes detected.";
                                site.LastContent = result.NewContent;
                                site.LastContentHash = result.NewHash;
                                LogManager.AddLog(site.Id, site.Name, site.Url, "Success", "Checked. No changes detected.");
                            }
                        }
                    }
                }

                // Save changes
                ConfigManager.Save(config);

                // Auto prune logs if enabled
                if (config.Settings.AutoPruneLogs)
                {
                    LogManager.PruneLogs(config.Settings.PruneLogsDays);
                }

                // Play custom sound if updates detected
                if (updatedCount > 0 && config.Settings.PlaySoundOnUpdate && !string.IsNullOrEmpty(config.Settings.CustomSoundPath))
                {
                    NotificationService.PlaySound(config.Settings.CustomSoundPath);
                }

                // Refresh MainWindow if it's currently open
                m_mainWindow?.LoadSites();
                m_mainWindow?.LoadLogs(); // Refresh logs if open

                // Send summary notification for Startup or manual force checks
                if (isStartup)
                {
                    string title = "Startup Check Complete";
                    string msg = updatedCount > 0
                        ? $"{updatedCount} site(s) updated:\n" + string.Join(", ", updatedSites)
                        : $"All {sitesToProcess.Count} site(s) checked. Up to date.";
                    
                    var icon = updatedCount > 0 ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info;
                    _notifyIcon?.ShowBalloonTip(5000, title, msg, icon);
                }
                else if (forceNotification)
                {
                    string title = "Check Complete";
                    string msg = updatedCount > 0
                        ? $"{updatedCount} site(s) updated:\n" + string.Join(", ", updatedSites)
                        : $"No changes detected across {sitesToProcess.Count} site(s) checked.";

                    if (errorCount > 0)
                    {
                        msg += $" ({errorCount} check(s) failed)";
                    }
                    
                    var icon = (updatedCount > 0 || errorCount > 0) ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info;
                    _notifyIcon?.ShowBalloonTip(5000, title, msg, icon);
                }
            }
            catch (Exception ex)
            {
                if (forceNotification || isStartup)
                {
                    _notifyIcon?.ShowBalloonTip(5000, "Site Checker Error", ex.Message, System.Windows.Forms.ToolTipIcon.Warning);
                }
            }
            finally
            {
                _isChecking = false;
            }
        }

        private void OnBalloonTipClicked(object? sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastClickedUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _lastClickedUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open URL: {ex.Message}");
                }
            }
        }

        public static System.Drawing.Icon CreateAppIcon(int size = 16)
        {
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                // Draw a blue circle background
                using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(2, 132, 199)); // Sky-600
                g.FillEllipse(bgBrush, 0, 0, size - 1, size - 1);

                // Draw white letter 'N'
                float fontSize = size * 9f / 16f;
                using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold);
                using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                
                var sf = new System.Drawing.StringFormat
                {
                    Alignment = System.Drawing.StringAlignment.Center,
                    LineAlignment = System.Drawing.StringAlignment.Center
                };
                g.DrawString("N", font, textBrush, new System.Drawing.RectangleF(0, 0, size, size), sf);
            }
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
        }

        private void ExitApp()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            if (_appIcon != null)
            {
                IntPtr hIcon = _appIcon.Handle;
                _appIcon.Dispose();
                DestroyIcon(hIcon);
            }

            // Close MainWindow (bypass intercept) and exit
            if (m_mainWindow != null)
            {
                m_mainWindow.CanClose = true;
                m_mainWindow.Close();
            }

            this.Exit();
        }
    }


}
