using System;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace Notifier.Services
{
    public static class StartupHelper
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "SiteNotifier";
        private const string PackagedTaskId = "SiteNotifierStartupTask";

        private static bool IsPackaged()
        {
            try
            {
                return Windows.ApplicationModel.Package.Current.Id != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsStartupEnabled()
        {
            if (IsPackaged())
            {
                try
                {
                    return Task.Run(async () =>
                    {
                        var task = await Windows.ApplicationModel.StartupTask.GetAsync(PackagedTaskId);
                        return task.State == Windows.ApplicationModel.StartupTaskState.Enabled ||
                               task.State == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                    }).Result;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static async Task SetStartupAsync(bool enable)
        {
            if (IsPackaged())
            {
                try
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(PackagedTaskId);
                    if (enable)
                    {
                        var state = await task.RequestEnableAsync();
                        if (state == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                        {
                            MessageBox.Show("Startup has been disabled in Task Manager. Please enable it under the Startup Apps tab in Task Manager or Windows Settings.", 
                                            "Startup Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        task.Disable();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to configure startup task: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enable)
                {
                    string executablePath = Application.ExecutablePath;
                    key.SetValue(AppName, $"\"{executablePath}\"");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to configure startup registry: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
